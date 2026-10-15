"""Formula correctness and retention-constraint checks.

The shipped /data model has to clear the targets in AGENTS.md. A separate
case proves the checker fails closed when the curve is broken.
"""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
import pytest

from model.generators import (
    Generator,
    Milestone,
    cost_curve,
    cost_next,
    marginal_production,
    production_is_subexponential,
    production_total,
    return_on_cost,
    tiers_are_subexponential,
    upgrade_multiplier,
)
from model.load import ROOT, load_economy, parse_economy
from model.monetization import monetization_gate
from model.offline import offline_earnings
from model.prestige import apply_prestige, prestige_multiplier, prestige_points, prestige_reset
from model.retention import project_retention
from sim.compare_runs import load_other, verdict
from sim.export_unity import build_unity_payload, spreadsheet_frame, write_exports
from sim.policies import choose
from sim.run_simulation import build_report, initial_state, main as run_main, simulate_run

DATA = ROOT / "data"


def _copy_data(tmp_path: Path, **replacements: dict) -> Path:
    directory = tmp_path / "data"
    directory.mkdir()
    for path in DATA.glob("*.json"):
        payload = json.loads(path.read_text(encoding="utf-8"))
        if path.name in replacements:
            payload.update(replacements[path.name])
        (directory / path.name).write_text(json.dumps(payload), encoding="utf-8")
    return directory


def test_cost_formula_matches_spec():
    assert cost_next(10, 1.15, 0) == 10
    assert cost_next(10, 1.15, 1) == pytest.approx(11.5)
    assert cost_next(10, 1.15, 2) == pytest.approx(10 * 1.15**2)
    owned = np.array([0, 1, 2, 10])
    curve = cost_curve(10, 1.15, owned)
    for count, value in zip(owned, curve):
        assert value == pytest.approx(cost_next(10, 1.15, int(count)))


def test_return_on_cost_scales_with_the_active_horizon():
    generator = Generator("generator_1", 1, 10, 1.15, 2)
    short = return_on_cost(generator, 0, (), 1.0, horizon_seconds=1)
    long = return_on_cost(generator, 0, (), 1.0, horizon_seconds=600)
    assert short == pytest.approx(marginal_production(generator, 0, (), 1.0) / 10)
    assert long == pytest.approx(short * 600)
    with pytest.raises(ValueError, match="horizon_seconds"):
        return_on_cost(generator, 0, (), 1.0, horizon_seconds=0)


def test_production_formula_matches_spec():
    assert production_total(2, 3, 4) == 24
    milestones = (Milestone(10, 2),)
    assert upgrade_multiplier(9, milestones) == 1
    assert upgrade_multiplier(10, milestones) == 2
    generator = Generator("generator_1", 1, 10, 1.15, 2)
    # Crossing the milestone multiplies the whole stack, not just the new copy.
    assert marginal_production(generator, 9, milestones, 1) == pytest.approx(2 * 10 * 2 - 2 * 9 * 1)


def test_production_stays_subexponential_on_shipped_curve():
    economy = load_economy()
    assert tiers_are_subexponential(economy.generators)
    for generator in economy.generators:
        assert generator.growth_rate > 1
        assert production_is_subexponential(generator, economy.milestones)
        opening = marginal_production(generator, 0, economy.milestones, 1) / generator.cost_base
        later_cost = cost_next(generator.cost_base, generator.growth_rate, 200)
        later = marginal_production(generator, 200, economy.milestones, 1) / later_cost
        assert later < opening


def test_first_generator_uses_documented_growth_rate():
    economy = load_economy()
    assert economy.generators[0].id == "generator_1"
    assert economy.generators[0].growth_rate == pytest.approx(1.15)


def test_offline_earnings_respect_rate_and_cap():
    assert offline_earnings(10, 100, 0.5, 1000) == 500
    assert offline_earnings(10, 5000, 0.5, 1000) == 5000
    with pytest.raises(ValueError):
        offline_earnings(10, 100, 0.51, 1000)


def test_loader_rejects_offline_rate_above_half(tmp_path: Path):
    directory = _copy_data(tmp_path, **{"offline.json": {"offline_rate": 0.9}})
    with pytest.raises(ValueError, match="offline_rate"):
        load_economy(directory)


def test_session_cadence_is_inside_the_check_in_target():
    economy = load_economy()
    cadence = economy.offline.session_cadence
    assert cadence == pytest.approx(16.5 / 3)
    assert economy.retention.session_cadence_min <= cadence <= economy.retention.session_cadence_max


def test_prestige_reset_compounds_and_clears_the_run():
    gained = prestige_points(48_000_000, coeff=1, divisor=24_000_000, exponent=0.5)
    assert gained == 1
    assert prestige_multiplier(0, 0.2) == 1
    assert prestige_multiplier(1, 0.2) == pytest.approx(1.2)
    assert prestige_multiplier(2, 0.2) == pytest.approx(1.44)
    first = prestige_reset(
        48_000_000,
        1.0,
        coeff=1,
        divisor=24_000_000,
        exponent=0.5,
        point_value=0.2,
    )
    assert first["points_gained"] == 1
    assert first["multiplier"] == pytest.approx(1.2)
    assert first["currency"] == 0
    assert first["owned_reset"] is True
    second = prestige_reset(
        48_000_000,
        float(first["multiplier"]),
        coeff=1,
        divisor=24_000_000,
        exponent=0.5,
        point_value=0.2,
    )
    assert second["points_total"] == 2
    assert second["multiplier"] == pytest.approx(1.44)


def test_monetization_gates_at_the_first_wall_and_every_third_prestige():
    assert monetization_gate(0, True, 3)["reason"] == "first_prestige_wall"
    assert monetization_gate(0, False, 3)["active"] is False
    assert monetization_gate(1, False, 3)["active"] is False
    assert monetization_gate(2, False, 3)["reason"] == "every_nth_prestige"
    assert monetization_gate(3, False, 3)["active"] is False
    assert monetization_gate(5, False, 3)["reason"] == "every_nth_prestige"
    for prestige_count in range(9):
        gate = monetization_gate(prestige_count, prestige_count == 0, 3)
        upcoming = prestige_count + 1
        expect = prestige_count == 0 or (upcoming % 3 == 0 and upcoming != 1)
        assert gate["active"] is expect
        assert gate["default_action"] == "decline_offer"


def test_short_wall_fails_the_retention_projection():
    economy = load_economy()
    healthy = project_retention(
        wall_minutes=96,
        session_cadence=5.5,
        first_multiplier=1.2,
        purchase_agreement=0.98,
        targets=economy.retention,
    )
    assert healthy["d1"] >= economy.retention.d1_min
    assert healthy["d7"] >= economy.retention.d7_min
    assert healthy["d30"] >= economy.retention.d30_min
    short = project_retention(
        wall_minutes=60,
        session_cadence=5.5,
        first_multiplier=1.2,
        purchase_agreement=0.98,
        targets=economy.retention,
    )
    assert short["d1"] < economy.retention.d1_min


def test_tiny_curve_buys_on_the_cost_formula(tmp_path: Path):
    directory = _copy_data(tmp_path)
    generators = json.loads((directory / "generators.json").read_text(encoding="utf-8"))
    generators["starting_currency"] = 10
    generators["active_click_per_second"] = 0
    generators["generators"] = [
        {
            "id": "generator_1",
            "tier": 1,
            "cost_base": 10,
            "growth_rate": 1.5,
            "production_base": 1,
        }
    ]
    (directory / "generators.json").write_text(json.dumps(generators), encoding="utf-8")
    prestige = json.loads((directory / "prestige.json").read_text(encoding="utf-8"))
    prestige["min_points_to_prestige"] = 100
    prestige["stall_seconds"] = 10_000
    (directory / "prestige.json").write_text(json.dumps(prestige), encoding="utf-8")
    simulation = json.loads((directory / "simulation.json").read_text(encoding="utf-8"))
    simulation["max_purchases"] = 2
    simulation["max_active_seconds"] = 100
    (directory / "simulation.json").write_text(json.dumps(simulation), encoding="utf-8")

    economy = load_economy(directory)
    result = simulate_run(economy, policy="roi", stop_at_wall=True)
    assert result["owned"]["generator_1"] == 2
    assert result["t_seconds"] == pytest.approx(15)
    assert result["lifetime_currency"] == pytest.approx(15)
    assert result["wall"] is None


def test_shipped_model_meets_retention_constraints():
    report = build_report(load_economy())
    constraints = report["constraints"]
    assert isinstance(constraints, dict)
    assert constraints["ok"] is True, constraints["failures"]
    wall = float(report["prestige_wall_minutes"])
    assert 90 <= wall <= 120
    retention = report["retention"]
    assert isinstance(retention, dict)
    assert retention["d1"] >= 0.35
    assert retention["d7"] >= 0.15
    assert retention["d30"] >= 0.05
    assert 5 <= float(report["session_cadence"]) <= 6
    assert float(report["purchase_agreement"]) >= 0.8
    assert float(report["dominant_route_share"]) < 0.8
    assert report["dominant_route"] == "peak_contract"
    assert report["route_run_counts"] == {"peak_contract": 12, "linear": 9}
    assert float(report["route_shares"]["peak_contract"]) == pytest.approx(12 / 21)
    assert float(report["route_shares"]["linear"]) == pytest.approx(9 / 21)
    assert float(report["contract_take_rate"]) == pytest.approx(12 / 21)
    assert float(report["roi_horizon_minutes"]) == pytest.approx(10)
    bought = [choice for choice in report["contract_choices"] if choice["bought"]]
    assert bought
    assert {choice["total_owned"] for choice in bought} <= {52, 53, 54}
    for route_name, minutes in report["route_walls"].items():
        assert 90 <= float(minutes) <= 120, route_name
    progress = report["route_progress"]
    assert progress["peak_contract"]["resets_to_10x"] == 2
    assert progress["peak_contract"]["run_routes"] == ["peak_contract", "peak_contract"]
    assert progress["peak_contract"]["time_to_second_prestige_minutes"] == pytest.approx(302.433108, abs=1e-4)
    assert progress["linear"]["run_routes"] == ["linear", "peak_contract"]
    assert progress["linear"]["time_to_second_prestige_minutes"] == pytest.approx(304.154113, abs=1e-4)
    assert report["unlock_order"] == [
        "generator_1",
        "generator_2",
        "generator_3",
        "generator_4",
        "generator_5",
        "generator_6",
    ]
    assert report["points_gained"] == 1
    assert report["multiplier_after_prestige"] == pytest.approx(1.6)
    assert float(report["prestige_wall_minutes"]) == pytest.approx(97.192637, abs=1e-4)
    assert float(report["retention"]["d1"]) == pytest.approx(0.404895, abs=1e-6)
    assert float(report["retention"]["d7"]) == pytest.approx(0.192807, abs=1e-6)
    assert float(report["retention"]["d30"]) == pytest.approx(0.08)
    assert report["monetization_gate"]["active"] is True
    assert report["free_path"] == "decline_offer"
    assert report["constraints"]["ok"] is True
    names = [event["name"] for event in report["events"]]
    assert "session_begin" in names
    assert "upgrade_purchase" in names
    assert "prestige_trigger" in names


def test_roi_buys_the_contract_only_near_the_commit_cap():
    """The dip is immediate. Horizon ROI buys only once the peak fits in the window."""
    economy = load_economy()
    branch = economy.branches[0]
    assert branch.active_production_multiplier < 1
    assert branch.peak_net_multiplier > 1
    assert branch.commit_before_total_owned <= branch.peak_total_owned_at_least
    with pytest.raises(ValueError, match="peak"):
        choose(economy, initial_state(economy), "peak")
    result = simulate_run(economy, policy="roi")
    bought = [purchase for purchase in result["purchases"] if purchase["kind"] == "branch"]
    assert len(bought) == 1
    assert bought[0]["total_owned"] == branch.commit_before_total_owned - 1
    assert result["state"].branch_levels["peak_contract"] == 1
    assert 90 <= float(result["wall"]["t_minutes"]) <= 120


def test_second_run_reaches_the_same_lifetime_faster():
    economy = load_economy()
    first = simulate_run(economy, policy="roi")
    wall = first["wall"]
    assert wall is not None
    reset, result = apply_prestige(first["state"], [item.id for item in economy.generators], economy.prestige)
    assert result["points_gained"] >= 1
    assert reset.owned == {item.id: 0 for item in economy.generators}
    assert reset.currency == 0
    assert reset.prestige_points == first["state"].prestige_points + result["points_gained"]
    second = simulate_run(
        economy,
        policy="roi",
        state=reset,
        stop_at_wall=False,
        stop_at_lifetime=float(wall["lifetime_currency"]),
    )
    assert second["t_seconds"] < float(wall["t_seconds"])


def test_extreme_growth_breaks_constraints_and_stops():
    economy = load_economy()
    generators = tuple(
        Generator(item.id, item.tier, item.cost_base, 3.0, item.production_base)
        for item in economy.generators
    )
    broken = economy.__class__(**{**economy.__dict__, "generators": generators})
    report = build_report(broken, include_population=False)
    constraints = report["constraints"]
    assert isinstance(constraints, dict)
    assert constraints["ok"] is False
    assert constraints["failures"]


def test_run_simulation_cli_passes():
    assert run_main(["--json"]) == 0


def test_unity_export_matches_data(tmp_path: Path):
    economy = load_economy()
    payload = build_unity_payload(economy)
    assert payload["generators"][0]["growthRate"] == pytest.approx(1.15)
    assert payload["offline"]["offlineRate"] <= 0.5
    assert payload["prestige"]["exponent"] == pytest.approx(economy.prestige.exponent)
    assert payload["monetization"]["defaultAction"] == "decline_offer"
    assert [event["name"] for event in payload["analyticsEvents"]] == [
        "session_begin",
        "upgrade_purchase",
        "prestige_trigger",
    ]
    frame = spreadsheet_frame(economy)
    assert list(frame["id"]) == [item.id for item in economy.generators]
    assert frame.iloc[0]["growth_rate"] == pytest.approx(1.15)
    unity_path = tmp_path / "economy_config.json"
    csv_path = tmp_path / "generators.csv"
    write_exports(economy, unity_path, csv_path)
    assert json.loads(unity_path.read_text(encoding="utf-8")) == payload
    committed = json.loads((ROOT / "exports" / "unity" / "economy_config.json").read_text(encoding="utf-8"))
    assert committed == payload


def test_baseline_report_matches_current_model():
    report = build_report(load_economy())
    saved = json.loads((ROOT / "exports" / "runs" / "baseline.json").read_text(encoding="utf-8"))
    assert saved["data_fingerprint"] == report["data_fingerprint"]
    assert saved["prestige_wall_minutes"] == report["prestige_wall_minutes"]
    assert saved["retention"] == report["retention"]
    assert saved["constraints"]["ok"] is True


def test_compare_verdict_picks_the_closer_wall_and_better_d7():
    current = {
        "prestige_wall_minutes": 96,
        "retention": {"d7": 0.19},
    }
    other = {
        "prestige_wall_minutes": 40,
        "retention": {"d7": 0.22},
    }
    decision = verdict(current, other, 90)
    assert decision["closer_to_prestige_wall"] == "current"
    assert decision["better_d7"] == "other"


def test_compare_unknown_spec_fails():
    with pytest.raises(FileNotFoundError):
        load_other("not-a-real-ref")


def _write_branch_curve(directory: Path, *, peak_at: int, commit: int, peak_multiplier: float) -> None:
    generators = json.loads((directory / "generators.json").read_text(encoding="utf-8"))
    generators["starting_currency"] = 1000
    generators["active_click_per_second"] = 0
    generators["generators"] = [
        {
            "id": "generator_1",
            "tier": 1,
            "cost_base": 10,
            "growth_rate": 1.5,
            "production_base": 100,
        }
    ]
    (directory / "generators.json").write_text(json.dumps(generators), encoding="utf-8")
    upgrades = json.loads((directory / "upgrades.json").read_text(encoding="utf-8"))
    upgrades["branches"] = [
        {
            "id": "peak_contract",
            "cost_base": 10,
            "growth_rate": 1.5,
            "max_level": 1,
            "active_production_multiplier": 0.8,
            "peak_production_multiplier": peak_multiplier,
            "peak_total_owned_at_least": peak_at,
            "commit_before_total_owned": commit,
        }
    ]
    (directory / "upgrades.json").write_text(json.dumps(upgrades), encoding="utf-8")
    prestige = json.loads((directory / "prestige.json").read_text(encoding="utf-8"))
    prestige["min_points_to_prestige"] = 100
    prestige["stall_seconds"] = 100_000
    (directory / "prestige.json").write_text(json.dumps(prestige), encoding="utf-8")


def test_horizon_roi_buys_a_peak_inside_the_window(tmp_path: Path):
    directory = _copy_data(tmp_path)
    _write_branch_curve(directory, peak_at=3, commit=3, peak_multiplier=5)
    economy = load_economy(directory)
    choice = choose(economy, initial_state(economy), "roi")
    assert choice is not None
    assert choice.kind == "branch"
    assert choice.roi > 0


def test_horizon_roi_skips_a_peak_past_the_window(tmp_path: Path):
    directory = _copy_data(tmp_path)
    _write_branch_curve(directory, peak_at=5000, commit=4000, peak_multiplier=2)
    economy = load_economy(directory)
    choice = choose(economy, initial_state(economy), "roi")
    assert choice is not None
    assert choice.kind == "generator"


def test_loader_rejects_a_second_purchase_objective(tmp_path: Path):
    directory = _copy_data(tmp_path, **{"simulation.json": {"population_weights": {"roi": 1, "peak": 1}}})
    with pytest.raises(ValueError, match="ROI"):
        load_economy(directory)


def test_loader_rejects_a_nonpositive_roi_horizon(tmp_path: Path):
    directory = _copy_data(tmp_path, **{"simulation.json": {"roi_horizon_minutes": 0}})
    with pytest.raises(ValueError, match="roi_horizon_minutes"):
        load_economy(directory)


def test_parse_economy_rejects_out_of_order_tiers():
    files = {name: json.loads((DATA / name).read_text(encoding="utf-8")) for name in (
        "generators.json",
        "upgrades.json",
        "prestige.json",
        "offline.json",
        "monetization.json",
        "simulation.json",
        "retention_targets.json",
        "analytics.json",
    )}
    files["generators.json"]["generators"][0]["tier"] = 2
    with pytest.raises(ValueError, match="tiers"):
        parse_economy(files, DATA)
