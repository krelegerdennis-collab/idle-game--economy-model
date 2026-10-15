"""Prestige reset.

    prestige_reset = f(lifetime_currency, prestige_multiplier)

Points granted by a run are a sublinear function of that run's lifetime
currency. The prestige multiplier compounds across runs:

    points = floor(coeff × (lifetime_currency / divisor) ^ exponent)
    multiplier = (1 + point_value) ^ total_points

A reset clears the run: currency and owned generators go back to empty.
Banked points, and therefore the multiplier, stay.
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field


@dataclass
class RunState:
    """Mutable state of one simulated player."""

    t: float
    currency: float
    lifetime_currency: float
    owned: dict[str, int]
    prestige_points: int = 0
    prestige_count: int = 0
    branch_levels: dict[str, int] = field(default_factory=dict)
    insight: int = 0
    relic_levels: dict[str, int] = field(default_factory=dict)
    claimed_insight_sources: set[str] = field(default_factory=set)
    pending_prestige_points: int = 0
    session_boost_until: float = 0.0
    session_boost_multiplier: float = 1.0
    session_started: bool = False

    def copy(self) -> RunState:
        return RunState(
            t=self.t,
            currency=self.currency,
            lifetime_currency=self.lifetime_currency,
            owned=dict(self.owned),
            prestige_points=self.prestige_points,
            prestige_count=self.prestige_count,
            branch_levels=dict(self.branch_levels),
            insight=self.insight,
            relic_levels=dict(self.relic_levels),
            claimed_insight_sources=set(self.claimed_insight_sources),
            pending_prestige_points=self.pending_prestige_points,
            session_boost_until=self.session_boost_until,
            session_boost_multiplier=self.session_boost_multiplier,
            session_started=self.session_started,
        )


@dataclass(frozen=True)
class PrestigeConfig:
    coeff: float
    divisor: float
    exponent: float
    point_value: float
    min_points_to_prestige: int
    stall_seconds: float


def prestige_points(
    lifetime_currency: float,
    *,
    coeff: float,
    divisor: float,
    exponent: float,
) -> int:
    """Points this run is worth, before they are banked."""
    if lifetime_currency <= 0 or divisor <= 0 or coeff <= 0:
        return 0
    if exponent <= 0 or exponent >= 1:
        raise ValueError("prestige exponent must be sublinear: 0 < exponent < 1")
    raw = coeff * (lifetime_currency / divisor) ** exponent
    if not math.isfinite(raw) or raw < 1:
        return 0
    return int(math.floor(raw))


def prestige_multiplier(total_points: int, point_value: float) -> float:
    """Compound multiplier from banked points. 1.0 before the first reset."""
    if total_points < 0:
        raise ValueError("total_points must be >= 0")
    if point_value < 0:
        raise ValueError("point_value must be >= 0")
    return (1.0 + point_value) ** int(total_points)


def points_from_multiplier(multiplier: float, point_value: float) -> int:
    """Recover banked points from a compounded multiplier."""
    if multiplier < 1:
        raise ValueError("prestige multiplier cannot be below 1")
    if multiplier == 1 or point_value == 0:
        return 0
    recovered = math.log(multiplier) / math.log(1.0 + point_value)
    return int(round(recovered))


def prestige_reset(
    lifetime_currency: float,
    current_multiplier: float,
    *,
    coeff: float,
    divisor: float,
    exponent: float,
    point_value: float,
) -> dict[str, float | int | bool]:
    """f(lifetime_currency, prestige_multiplier).

    Returns the post-reset point total and multiplier. The run itself is
    cleared by the caller (currency 0, generators 0). `current_multiplier`
    is the value entering the reset, so the new multiplier compounds on it.
    """
    gained = prestige_points(
        lifetime_currency,
        coeff=coeff,
        divisor=divisor,
        exponent=exponent,
    )
    banked = points_from_multiplier(current_multiplier, point_value)
    total = banked + gained
    return {
        "points_gained": gained,
        "points_total": total,
        "multiplier": prestige_multiplier(total, point_value),
        "currency": 0,
        "owned_reset": True,
    }


def apply_prestige(
    state: RunState,
    generator_ids: list[str],
    config: PrestigeConfig,
) -> tuple[RunState, dict[str, float | int | bool]]:
    """Apply prestige_reset to a run and return the cleared state."""
    current = prestige_multiplier(state.prestige_points, config.point_value)
    result = prestige_reset(
        state.lifetime_currency,
        current,
        coeff=config.coeff,
        divisor=config.divisor,
        exponent=config.exponent,
        point_value=config.point_value,
    )
    bonus = state.pending_prestige_points
    if bonus:
        result = dict(result)
        result["points_gained"] = int(result["points_gained"]) + bonus
        result["points_total"] = int(result["points_total"]) + bonus
        result["multiplier"] = prestige_multiplier(int(result["points_total"]), config.point_value)
    reset = RunState(
        t=0.0,
        currency=0.0,
        lifetime_currency=0.0,
        owned={generator_id: 0 for generator_id in generator_ids},
        prestige_points=int(result["points_total"]),
        prestige_count=state.prestige_count + 1,
        branch_levels={},
        insight=state.insight,
        relic_levels=dict(state.relic_levels),
        claimed_insight_sources=set(),
        pending_prestige_points=0,
        session_started=False,
    )
    return reset, result
