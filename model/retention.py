"""Design-time retention projection.

The percentages are not measured cohorts. They map simulation signals onto
the targets in data/retention_targets.json so a change can fail closed:

- prestige-wall time
- session cadence implied by the offline cap
- first-reset multiplier
- purchase-order agreement

Coefficients live in data. This module is only the shape of the projection.
"""

from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class RetentionTargets:
    d1_min: float
    d7_min: float
    d30_min: float
    session_cadence_min: float
    session_cadence_max: float
    first_prestige_wall_minutes_min: float
    first_prestige_wall_minutes_target: float
    first_prestige_wall_minutes_max: float
    purchase_order_agreement_min: float
    min_tiers_unlocked: int
    projection: dict[str, float]


def _clamp_factor(value: float, floor: float) -> float:
    return max(floor, min(1.0, value))


def wall_factor(minutes: float | None, targets: RetentionTargets) -> float:
    """1.0 at the early edge of the window, slightly lower toward the late edge."""
    projection = targets.projection
    floor = projection["factor_floor"]
    if minutes is None:
        return floor
    lo = targets.first_prestige_wall_minutes_min
    hi = targets.first_prestige_wall_minutes_max
    if lo <= minutes <= hi:
        span = hi - lo
        penalty = projection["wall_in_band_penalty_at_max"] * ((minutes - lo) / span)
        return _clamp_factor(1.0 - penalty, floor)
    if minutes < lo:
        scale = projection["short_wall_scale_minutes"]
        return _clamp_factor(1.0 - (lo - minutes) / scale, floor)
    overshoot_base = 1.0 - projection["wall_in_band_penalty_at_max"]
    scale = projection["long_wall_scale_minutes"]
    return _clamp_factor(overshoot_base - (minutes - hi) / scale, floor)


def cadence_factor(cadence: float, targets: RetentionTargets) -> float:
    projection = targets.projection
    lo = targets.session_cadence_min
    hi = targets.session_cadence_max
    if lo <= cadence <= hi:
        return 1.0
    distance = (lo - cadence) if cadence < lo else (cadence - hi)
    return _clamp_factor(1.0 - projection["cadence_penalty_per_checkin"] * distance, projection["factor_floor"])


def reward_factor(first_multiplier: float, targets: RetentionTargets) -> float:
    """Full credit when the first reset is felt, and not when it deletes the next run."""
    projection = targets.projection
    lo = projection["healthy_first_multiplier_min"]
    hi = projection["healthy_first_multiplier_max"]
    floor = projection["factor_floor"]
    if lo <= first_multiplier <= hi:
        return 1.0
    if first_multiplier < lo:
        width = projection["reward_low_penalty_width"]
        return _clamp_factor(1.0 - (lo - first_multiplier) / width, floor)
    width = projection["reward_high_penalty_width"]
    return _clamp_factor(1.0 - (first_multiplier - hi) / width, floor)


def legibility_factor(agreement: float, targets: RetentionTargets) -> float:
    minimum = targets.purchase_order_agreement_min
    if agreement >= minimum:
        return 1.0
    if minimum <= 0:
        return targets.projection["factor_floor"]
    return _clamp_factor(agreement / minimum, targets.projection["factor_floor"])


def project_retention(
    *,
    wall_minutes: float | None,
    session_cadence: float,
    first_multiplier: float,
    purchase_agreement: float,
    targets: RetentionTargets,
) -> dict[str, float]:
    """Project D1/D7/D30 from one simulation report."""
    projection = targets.projection
    wall = wall_factor(wall_minutes, targets)
    cadence = cadence_factor(session_cadence, targets)
    reward = reward_factor(first_multiplier, targets)
    legibility = legibility_factor(purchase_agreement, targets)
    return {
        "d1": projection["base_d1"] * wall * cadence * legibility,
        "d7": projection["base_d7"] * wall * cadence * reward * legibility,
        "d30": projection["base_d30"] * cadence * reward * legibility,
        "session_cadence": session_cadence,
        "wall_factor": wall,
        "cadence_factor": cadence,
        "reward_factor": reward,
        "legibility_factor": legibility,
    }


def evaluate_constraints(
    *,
    wall_minutes: float | None,
    retention: dict[str, float],
    session_cadence: float,
    offline_rate: float,
    purchase_agreement: float,
    unlock_order: list[str],
    authored_order: list[str],
    tiers_unlocked: int,
    subexponential: bool,
    free_path_reached_wall: bool,
    targets: RetentionTargets,
) -> dict[str, object]:
    """Fail closed. Each failure is a sentence the agent can stop on."""
    failures: list[str] = []
    lo = targets.first_prestige_wall_minutes_min
    hi = targets.first_prestige_wall_minutes_max
    if wall_minutes is None:
        failures.append("simulation ended without a first prestige wall, so the 90 minute target cannot be checked")
    elif wall_minutes < lo:
        failures.append(
            f"first prestige wall is {wall_minutes:.2f} min, before the {lo:.0f} minute minimum"
        )
    elif wall_minutes > hi:
        failures.append(
            f"first prestige wall is {wall_minutes:.2f} min, after the {hi:.0f} minute maximum"
        )
    if retention["d1"] < targets.d1_min:
        failures.append(
            f"D1 projection is {retention['d1']:.1%}, below the {targets.d1_min:.0%} minimum"
        )
    if retention["d7"] < targets.d7_min:
        failures.append(
            f"D7 projection is {retention['d7']:.1%}, below the {targets.d7_min:.0%} minimum"
        )
    if retention["d30"] < targets.d30_min:
        failures.append(
            f"D30 projection is {retention['d30']:.1%}, below the {targets.d30_min:.0%} minimum"
        )
    if not (targets.session_cadence_min <= session_cadence <= targets.session_cadence_max):
        failures.append(
            f"session cadence is {session_cadence:.2f} check-ins/day, outside "
            f"{targets.session_cadence_min:.0f}–{targets.session_cadence_max:.0f}"
        )
    if offline_rate > 0.5:
        failures.append(f"offline_rate is {offline_rate}, above the 0.5 cap")
    if purchase_agreement < targets.purchase_order_agreement_min:
        failures.append(
            f"purchase-order agreement is {purchase_agreement:.0%}, below "
            f"{targets.purchase_order_agreement_min:.0%}; the generator sequence is illegible"
        )
    if unlock_order != authored_order[: len(unlock_order)]:
        failures.append(
            "primary purchase order "
            f"{' → '.join(unlock_order) or '(empty)'} is not the authored tier sequence"
        )
    if tiers_unlocked < targets.min_tiers_unlocked:
        failures.append(
            f"primary run unlocks {tiers_unlocked} tiers, fewer than {targets.min_tiers_unlocked}"
        )
    if not subexponential:
        failures.append(
            "production scaling is not sub-exponential relative to cost; the economy can run away"
        )
    if not free_path_reached_wall:
        failures.append("the free path does not reach the first prestige wall; a monetization gate would be required")
    return {"ok": not failures, "failures": failures}
