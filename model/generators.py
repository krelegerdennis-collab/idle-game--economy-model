"""Cost and production formulas.

Source of truth:

    cost_next = cost_base × growth_rate^owned
    production_total = production_base × owned × multipliers

Cost grows exponentially with copies owned. Production of a generator is
linear in copies owned, times finite milestone steps and the prestige
multiplier. That is the constraint that blocks an infinite-money glitch:
marginal output per currency spent must fall as owned increases.
"""

from __future__ import annotations

import math
from dataclasses import dataclass

import numpy as np


@dataclass(frozen=True)
class Milestone:
    owned_at_least: int
    production_multiplier: float


@dataclass(frozen=True)
class Generator:
    id: str
    tier: int
    cost_base: float
    growth_rate: float
    production_base: float


def cost_next(cost_base: float, growth_rate: float, owned: int) -> float:
    """Next copy costs cost_base × growth_rate^owned."""
    if owned < 0:
        raise ValueError("owned must be >= 0")
    if growth_rate <= 1:
        raise ValueError("growth_rate must be > 1 so cost scales exponentially")
    if cost_base <= 0:
        raise ValueError("cost_base must be > 0")
    return float(cost_base) * float(growth_rate) ** int(owned)


def cost_curve(cost_base: float, growth_rate: float, owned: np.ndarray) -> np.ndarray:
    """Vector form of cost_next, used for spreadsheet export."""
    owned_arr = np.asarray(owned)
    if np.any(owned_arr < 0):
        raise ValueError("owned must be >= 0")
    if growth_rate <= 1:
        raise ValueError("growth_rate must be > 1 so cost scales exponentially")
    if cost_base <= 0:
        raise ValueError("cost_base must be > 0")
    return float(cost_base) * np.power(float(growth_rate), owned_arr.astype(np.float64))


def upgrade_multiplier(owned: int, milestones: tuple[Milestone, ...]) -> float:
    """Product of milestone multipliers whose owned threshold is met."""
    multiplier = 1.0
    for milestone in milestones:
        if owned >= milestone.owned_at_least:
            multiplier *= milestone.production_multiplier
    return multiplier


def production_total(production_base: float, owned: int, multipliers: float) -> float:
    """production_total = production_base × owned × multipliers."""
    if owned < 0:
        raise ValueError("owned must be >= 0")
    return float(production_base) * int(owned) * float(multipliers)


def marginal_production(
    generator: Generator,
    owned: int,
    milestones: tuple[Milestone, ...],
    prestige_multiplier: float,
) -> float:
    """Production per second gained by buying one more copy.

    Milestone thresholds apply to the whole stack, so the copy that crosses
    a threshold can jump by more than production_base.
    """
    before_mult = upgrade_multiplier(owned, milestones) * prestige_multiplier
    after_mult = upgrade_multiplier(owned + 1, milestones) * prestige_multiplier
    before = production_total(generator.production_base, owned, before_mult)
    after = production_total(generator.production_base, owned + 1, after_mult)
    return after - before


def return_on_cost(
    generator: Generator,
    owned: int,
    milestones: tuple[Milestone, ...],
    prestige_multiplier: float,
) -> float:
    """Marginal production per currency spent on the next copy."""
    cost = cost_next(generator.cost_base, generator.growth_rate, owned)
    if not math.isfinite(cost) or cost <= 0:
        return 0.0
    return marginal_production(generator, owned, milestones, prestige_multiplier) / cost


def production_is_subexponential(
    generator: Generator,
    milestones: tuple[Milestone, ...],
    *,
    sample_owned: int = 200,
) -> bool:
    """True when later copies pay back worse than the first copy.

    Compared at `sample_owned`, which sits past the milestone table in the
    shipped data. Exponential cost then dominates linear production.
    """
    if generator.growth_rate <= 1 or generator.production_base <= 0:
        return False
    opening = return_on_cost(generator, 0, milestones, 1.0)
    if opening <= 0 or not math.isfinite(opening):
        return False
    later_cost = cost_next(generator.cost_base, generator.growth_rate, sample_owned)
    if not math.isfinite(later_cost):
        return True
    later = return_on_cost(generator, sample_owned, milestones, 1.0)
    return later < opening


def tiers_are_subexponential(generators: tuple[Generator, ...]) -> bool:
    """Each higher tier costs more, per unit of production, than the tier below.

    production_base ratio must stay strictly under the cost_base ratio.
    Otherwise a later tier is an exponential production jump and the
    purchase order stops being a curve and becomes a skip.
    """
    for lower, higher in zip(generators, generators[1:]):
        if lower.cost_base <= 0 or lower.production_base <= 0:
            return False
        cost_ratio = higher.cost_base / lower.cost_base
        production_ratio = higher.production_base / lower.production_base
        if production_ratio >= cost_ratio:
            return False
    return True
