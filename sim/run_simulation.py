"""Simulate active play and project retention from the result.

The first prestige wall is the first moment when a reset would grant at
least min_points_to_prestige AND the best next purchase takes at least
stall_seconds. Time is active play only. The default path never accepts a
monetization offer.

    python -m sim.run_simulation
    python -m sim.run_simulation --trace
    python -m sim.run_simulation --save exports/runs/baseline.json
"""

from __future__ import annotations

import argparse
import hashlib
import json
import random
import sys
from pathlib import Path

from model.generators import production_is_subexponential, production_total, tiers_are_subexponential, upgrade_multiplier
from model.load import ROOT, Economy, load_economy
from model.monetization import monetization_gate
from model.prestige import RunState, prestige_multiplier, prestige_points
from model.retention import evaluate_constraints, project_retention
from sim.policies import Choice, choose

_PURCHASE_TOLERANCE = 1e-8


def production_rate(economy: Economy, state: RunState) -> float:
    """Clicks plus every generator. Prestige multiplies the whole rate."""
    prestige = prestige_multiplier(state.prestige_points, economy.prestige.point_value)
    total = economy.active_click_per_second * prestige
    for generator in economy.generators:
        owned = state.owned[generator.id]
        multipliers = upgrade_multiplier(owned, economy.milestones) * prestige
        total += production_total(generator.production_base, owned, multipliers)
    return total


def initial_state(economy: Economy) -> RunState:
    return RunState(
        t=0.0,
        currency=economy.starting_currency,
        lifetime_currency=0.0,
        owned={generator.id: 0 for generator in economy.generators},
        prestige_points=0,
        prestige_count=0,
    )


def _points_ready(economy: Economy, state: RunState) -> int:
    config = economy.prestige
    return prestige_points(
        state.lifetime_currency,
        coeff=config.coeff,
        divisor=config.divisor,
        exponent=config.exponent,
    )


def _record_sample(samples: list[dict[str, object]], state: RunState, economy: Economy) -> None:
    samples.append(
        {
            "t_seconds": round(state.t, 4),
            "currency": round(state.currency, 4),
            "lifetime_currency": round(state.lifetime_currency, 4),
            "production_per_second": round(production_rate(economy, state), 6),
            "owned": dict(state.owned),
        }
    )


def _advance(economy: Economy, state: RunState, dt: float, samples: list[dict[str, object]], next_sample: float) -> float:
    if dt < 0:
        raise ValueError("cannot advance negative time")
    rate = production_rate(economy, state)
    step = economy.simulation.sample_every_seconds
    if step > 0:
        while next_sample <= state.t + dt + 1e-9:
            if next_sample >= state.t - 1e-9:
                frac = max(0.0, next_sample - state.t)
                probe = state.copy()
                probe.t = next_sample
                probe.currency = state.currency + rate * frac
                probe.lifetime_currency = state.lifetime_currency + rate * frac
                _record_sample(samples, probe, economy)
            next_sample += step
    state.t += dt
    state.currency += rate * dt
    state.lifetime_currency += rate * dt
    return next_sample


def _buy(state: RunState, choice: Choice) -> None:
    if state.currency + _PURCHASE_TOLERANCE < choice.cost:
        raise RuntimeError(f"purchase of {choice.generator_id} without currency")
    state.currency -= choice.cost
    if state.currency < 0 and abs(state.currency) < _PURCHASE_TOLERANCE * max(1.0, choice.cost):
        state.currency = 0.0
    state.owned[choice.generator_id] += 1


def unlock_order(purchases: list[dict[str, object]]) -> list[str]:
    order: list[str] = []
    for purchase in purchases:
        generator_id = str(purchase["generator_id"])
        if generator_id not in order:
            order.append(generator_id)
    return order


def simulate_run(
    economy: Economy,
    *,
    policy: str,
    seed: int | None = None,
    state: RunState | None = None,
    stop_at_wall: bool = True,
    stop_at_lifetime: float | None = None,
    grant_starting_currency: bool = True,
) -> dict[str, object]:
    """Simulate one policy until the wall, a lifetime target, or the time cap."""
    if state is None:
        current = initial_state(economy)
        if not grant_starting_currency:
            current.currency = 0.0
    else:
        current = state.copy()
    rng = random.Random(seed) if policy == "roi_noisy" else None
    purchases: list[dict[str, object]] = []
    samples: list[dict[str, object]] = []
    _record_sample(samples, current, economy)
    next_sample = economy.simulation.sample_every_seconds
    wall: dict[str, object] | None = None
    stopped_on = "time_cap"

    while current.t < economy.simulation.max_active_seconds and len(purchases) < economy.simulation.max_purchases:
        if stop_at_lifetime is not None and current.lifetime_currency >= stop_at_lifetime:
            stopped_on = "lifetime"
            break
        choice = choose(economy, current, policy, rng)
        if choice is None:
            stopped_on = "no_purchase"
            break
        if current.currency + _PURCHASE_TOLERANCE >= choice.cost:
            _buy(current, choice)
            purchases.append(_purchase_record(current, choice))
            continue
        rate = production_rate(economy, current)
        if rate <= 0:
            stopped_on = "no_production"
            break
        dt = (choice.cost - current.currency) / rate
        available_points = _points_ready(economy, current)
        stall = economy.prestige.stall_seconds
        points_needed = economy.prestige.min_points_to_prestige
        if wall is None and dt >= stall and available_points >= points_needed:
            wall = _wall_record(economy, current, choice, dt, available_points)
            stopped_on = "prestige_wall"
            if stop_at_wall:
                break
        remaining = economy.simulation.max_active_seconds - current.t
        if stop_at_lifetime is not None:
            lifetime_gap = stop_at_lifetime - current.lifetime_currency
            if lifetime_gap <= 0:
                stopped_on = "lifetime"
                break
            dt_to_lifetime = lifetime_gap / rate
            if dt_to_lifetime <= dt and dt_to_lifetime <= remaining:
                next_sample = _advance(economy, current, dt_to_lifetime, samples, next_sample)
                stopped_on = "lifetime"
                break
        if dt > remaining:
            next_sample = _advance(economy, current, remaining, samples, next_sample)
            stopped_on = "time_cap"
            break
        next_sample = _advance(economy, current, dt, samples, next_sample)
        if abs(current.currency - choice.cost) <= _PURCHASE_TOLERANCE * max(1.0, choice.cost):
            current.currency = choice.cost
        _buy(current, choice)
        purchases.append(_purchase_record(current, choice))
    else:
        if len(purchases) >= economy.simulation.max_purchases:
            stopped_on = "purchase_cap"

    return {
        "policy": policy,
        "stopped_on": stopped_on,
        "t_seconds": current.t,
        "lifetime_currency": current.lifetime_currency,
        "currency": current.currency,
        "owned": dict(current.owned),
        "purchases": purchases,
        "samples": samples,
        "unlock_order": unlock_order(purchases),
        "wall": wall,
        "state": current,
    }


def _purchase_record(state: RunState, choice: Choice) -> dict[str, object]:
    return {
        "generator_id": choice.generator_id,
        "owned_after": state.owned[choice.generator_id],
        "cost": round(choice.cost, 6),
        "active_seconds": round(state.t, 4),
    }


def _wall_record(
    economy: Economy,
    state: RunState,
    choice: Choice,
    wait_seconds: float,
    points: int,
) -> dict[str, object]:
    config = economy.prestige
    multiplier_after = prestige_multiplier(state.prestige_points + points, config.point_value)
    return {
        "t_seconds": state.t,
        "t_minutes": state.t / 60.0,
        "lifetime_currency": state.lifetime_currency,
        "points_gained": points,
        "multiplier_after": multiplier_after,
        "wait_seconds": wait_seconds,
        "waiting_on": choice.generator_id,
        "waiting_cost": choice.cost,
        "production_per_second": production_rate(economy, state),
        "owned": dict(state.owned),
    }


def _is_legible(order: list[str], authored: list[str]) -> bool:
    return len(order) > 0 and order == authored[: len(order)]


def purchase_agreement(economy: Economy, primary: dict[str, object]) -> dict[str, object]:
    """Weighted share of policies whose unlock order follows the authored tiers."""
    authored = [generator.id for generator in economy.generators]
    weights = economy.simulation.population_weights
    orders: list[tuple[list[str], float]] = []

    primary_order = list(primary["unlock_order"])
    orders.append((primary_order, weights.get("roi", 0.0)))

    noisy_weight = weights.get("roi_noisy", 0.0)
    runs = max(1, economy.simulation.noisy_runs)
    for index in range(runs):
        noisy = simulate_run(
            economy,
            policy="roi_noisy",
            seed=economy.simulation.noisy_seed + index,
        )
        orders.append((list(noisy["unlock_order"]), noisy_weight / runs))

    if weights.get("patient", 0) > 0:
        patient = simulate_run(economy, policy="patient")
        orders.append((list(patient["unlock_order"]), weights["patient"]))
    if weights.get("cheapest", 0) > 0:
        cheapest = simulate_run(economy, policy="cheapest")
        orders.append((list(cheapest["unlock_order"]), weights["cheapest"]))

    total = sum(weight for _, weight in orders)
    legible = sum(weight for order, weight in orders if _is_legible(order, authored))
    agreement = legible / total if total else 0.0
    return {
        "agreement": agreement,
        "authored_order": authored,
        "primary_order": primary_order,
        "primary_is_prefix": _is_legible(primary_order, authored),
    }


def _round_number(value: object, digits: int = 6) -> object:
    if isinstance(value, float):
        return round(value, digits)
    if isinstance(value, dict):
        return {key: _round_number(item, digits) for key, item in value.items()}
    if isinstance(value, list):
        return [_round_number(item, digits) for item in value]
    return value


def data_fingerprint(data_dir: Path) -> str:
    digest = hashlib.sha256()
    for path in sorted(data_dir.glob("*.json")):
        digest.update(path.name.encode())
        digest.update(path.read_bytes())
    return digest.hexdigest()[:12]


def build_report(economy: Economy, *, include_population: bool = True) -> dict[str, object]:
    primary = simulate_run(economy, policy=economy.simulation.primary_policy)
    wall = primary["wall"]
    wall_minutes = None if wall is None else float(wall["t_minutes"])
    points = 0 if wall is None else int(wall["points_gained"])
    multiplier = 1.0 if wall is None else float(wall["multiplier_after"])
    if include_population:
        agreement_report = purchase_agreement(economy, primary)
    else:
        authored = [generator.id for generator in economy.generators]
        order = list(primary["unlock_order"])
        agreement_report = {
            "agreement": 1.0 if _is_legible(order, authored) else 0.0,
            "authored_order": authored,
            "primary_order": order,
            "primary_is_prefix": _is_legible(order, authored),
        }
    cadence = economy.offline.session_cadence
    retention = project_retention(
        wall_minutes=wall_minutes,
        session_cadence=cadence,
        first_multiplier=multiplier,
        purchase_agreement=float(agreement_report["agreement"]),
        targets=economy.retention,
    )
    subexponential = all(
        production_is_subexponential(generator, economy.milestones) for generator in economy.generators
    ) and tiers_are_subexponential(economy.generators)
    gate = monetization_gate(
        prestige_count=0,
        at_first_wall=wall is not None,
        every_nth_prestige=economy.monetization.every_nth_prestige,
    )
    constraints = evaluate_constraints(
        wall_minutes=wall_minutes,
        retention=retention,
        session_cadence=cadence,
        offline_rate=economy.offline.offline_rate,
        purchase_agreement=float(agreement_report["agreement"]),
        unlock_order=list(agreement_report["primary_order"]),
        authored_order=list(agreement_report["authored_order"]),
        tiers_unlocked=len(agreement_report["primary_order"]),
        subexponential=subexponential,
        free_path_reached_wall=wall is not None,
        targets=economy.retention,
    )
    owned = primary["owned"]
    report: dict[str, object] = {
        "data_fingerprint": data_fingerprint(economy.data_dir) if economy.data_dir.is_dir() else None,
        "policy": economy.simulation.primary_policy,
        "stopped_on": primary["stopped_on"],
        "prestige_wall_seconds": None if wall is None else wall["t_seconds"],
        "prestige_wall_minutes": wall_minutes,
        "lifetime_currency_at_wall": None if wall is None else wall["lifetime_currency"],
        "points_gained": points,
        "multiplier_after_prestige": multiplier,
        "waiting_on": None if wall is None else wall["waiting_on"],
        "wait_seconds": None if wall is None else wall["wait_seconds"],
        "production_per_second_at_wall": None if wall is None else wall["production_per_second"],
        "owned_at_wall": None if wall is None else wall["owned"],
        "owned_final": owned,
        "unlock_order": agreement_report["primary_order"],
        "purchase_agreement": agreement_report["agreement"],
        "purchase_count": len(primary["purchases"]),
        "session_cadence": cadence,
        "retention": {
            "d1": retention["d1"],
            "d7": retention["d7"],
            "d30": retention["d30"],
            "session_cadence": retention["session_cadence"],
        },
        "retention_factors": {
            "wall_factor": retention["wall_factor"],
            "cadence_factor": retention["cadence_factor"],
            "reward_factor": retention["reward_factor"],
            "legibility_factor": retention["legibility_factor"],
        },
        "monetization_gate": gate,
        "free_path": economy.monetization.default_action,
        "constraints": constraints,
        "timeline": primary["samples"],
        "events": _events(primary, wall, points),
        "targets": {
            "wall_minutes_min": economy.retention.first_prestige_wall_minutes_min,
            "wall_minutes_max": economy.retention.first_prestige_wall_minutes_max,
            "wall_minutes_target": economy.retention.first_prestige_wall_minutes_target,
            "d1_min": economy.retention.d1_min,
            "d7_min": economy.retention.d7_min,
            "d30_min": economy.retention.d30_min,
            "session_cadence_min": economy.retention.session_cadence_min,
            "session_cadence_max": economy.retention.session_cadence_max,
        },
        "projection_note": (
            "D1/D7/D30 are a design-time projection from this run, not measured cohort retention."
        ),
    }
    return _round_number(report)  # type: ignore[return-value]


def _events(primary: dict[str, object], wall: dict[str, object] | None, points: int) -> list[dict[str, object]]:
    events: list[dict[str, object]] = [
        {"name": "session_begin", "session_index": 0, "seconds_since_last": 0, "prestige_count": 0}
    ]
    for purchase in primary["purchases"]:
        events.append(
            {
                "name": "upgrade_purchase",
                "generator_id": purchase["generator_id"],
                "owned_after": purchase["owned_after"],
                "cost": purchase["cost"],
                "active_seconds": purchase["active_seconds"],
            }
        )
    if wall is not None:
        events.append(
            {
                "name": "prestige_trigger",
                "prestige_count": 0,
                "points_gained": points,
                "lifetime_currency": round(float(wall["lifetime_currency"]), 6),
                "active_seconds": round(float(wall["t_seconds"]), 4),
            }
        )
    return events


def format_report(report: dict[str, object]) -> str:
    wall = report["prestige_wall_minutes"]
    retention = report["retention"]
    constraints = report["constraints"]
    assert isinstance(retention, dict)
    assert isinstance(constraints, dict)
    owned = report["owned_at_wall"] or {}
    assert isinstance(owned, dict)
    owned_text = ", ".join(f"{key}={value}" for key, value in owned.items())
    order = " → ".join(report["unlock_order"])  # type: ignore[arg-type]
    targets = report["targets"]
    assert isinstance(targets, dict)
    lines = [
        "Idle economy simulation (active play, monetization offer declined)",
        f"  policy: {report['policy']}",
        f"  prestige wall: {wall if wall is not None else 'not reached'} min "
        f"(window {float(targets['wall_minutes_min']):.0f}–{float(targets['wall_minutes_max']):.0f}, "
        f"target {float(targets['wall_minutes_target']):.0f})",
        f"  lifetime currency at wall: {report['lifetime_currency_at_wall']}",
        f"  prestige points gained: {report['points_gained']}",
        f"  multiplier after reset: {report['multiplier_after_prestige']}",
        f"  waiting on: {report['waiting_on']} for {report['wait_seconds']} seconds",
        f"  owned at wall: {owned_text}",
        f"  unlock order: {order}",
        f"  purchases: {report['purchase_count']}",
        f"  purchase-order agreement: {float(report['purchase_agreement']):.0%}",
        f"  session cadence: {float(report['session_cadence']):.2f} check-ins/day",
        "  retention projection (design-time, not measured): "
        f"D1 {float(retention['d1']):.1%}  D7 {float(retention['d7']):.1%}  "
        f"D30 {float(retention['d30']):.1%}",
        f"  constraints: {'PASS' if constraints['ok'] else 'FAIL'}",
    ]
    failures = constraints["failures"]
    assert isinstance(failures, list)
    if failures:
        lines.append("STOP: this change breaks a retention or economy constraint.")
        lines.extend(f"  - {failure}" for failure in failures)
    return "\n".join(lines)


def trace_run(economy: Economy) -> None:
    """Print the primary run's purchases whenever the tier or the minute changes."""
    result = simulate_run(economy, policy=economy.simulation.primary_policy, stop_at_wall=True)
    last_minute = -1
    last_id = None
    for purchase in result["purchases"]:
        minute = int(float(purchase["active_seconds"]) // 60)
        generator_id = purchase["generator_id"]
        if minute != last_minute or generator_id != last_id:
            print(
                f"t={float(purchase['active_seconds']) / 60:7.2f} min  "
                f"buy {generator_id} -> {purchase['owned_after']}  "
                f"cost={purchase['cost']}"
            )
            last_minute = minute
            last_id = generator_id
    wall = result["wall"]
    if wall is None:
        print(f"no wall ({result['stopped_on']}) at t={float(result['t_seconds']) / 60:.2f} min")
        print(f"lifetime={result['lifetime_currency']}")
        print(f"owned={result['owned']}")
        return
    print(
        f"WALL t={float(wall['t_minutes']):.2f} min  lifetime={wall['lifetime_currency']:.4f}  "
        f"points={wall['points_gained']}  wait={float(wall['wait_seconds']) / 60:.2f} min  "
        f"on={wall['waiting_on']}"
    )
    print(f"owned={wall['owned']}")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Simulate the idle economy and check retention constraints.")
    parser.add_argument("--data", type=Path, default=ROOT / "data", help="Directory of tunable JSON.")
    parser.add_argument("--save", type=Path, default=None, help="Write the report JSON to this path.")
    parser.add_argument("--json", action="store_true", help="Print the report as JSON.")
    parser.add_argument("--trace", action="store_true", help="Print a purchase trace instead of the summary.")
    parser.add_argument(
        "--no-fail",
        action="store_true",
        help="Exit 0 even when constraints fail. The STOP text is still printed.",
    )
    args = parser.parse_args(argv)
    economy = load_economy(args.data)
    if args.trace:
        trace_run(economy)
        return 0
    report = build_report(economy)
    if args.save is not None:
        args.save.parent.mkdir(parents=True, exist_ok=True)
        args.save.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    if args.json:
        print(json.dumps(report, indent=2))
    else:
        print(format_report(report))
    constraints = report["constraints"]
    assert isinstance(constraints, dict)
    if constraints["ok"] or args.no_fail:
        return 0
    return 1


if __name__ == "__main__":
    sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
    raise SystemExit(main())
