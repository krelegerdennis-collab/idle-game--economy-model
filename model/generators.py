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
class CommitThresholdFormula:
    """Scaling close for a branch offer.

    commit_threshold = base + floor(total_owned / owned_divisor)

    The cap moves with progress. It is not a fixed owned count.
    """

    base: int
    owned_divisor: int


@dataclass(frozen=True)
class Branch:
    """A purchased modifier that competes with buying the next generator.

    `structure` is `single` or `multi_stage`. A single purchase applies
    `active_production_multiplier` immediately and `peak_production_multiplier`
    once total generator copies reach `peak_total_owned_at_least`.

    A multi-stage contract is a ladder of purchases. Level n multiplies
    production by `active_production_multiplier` ** n until the peak. At the
    peak the multiplier is that dip times
    `peak_production_multiplier + peak_multiplier_per_stage * n`. Once n
    reaches `max_level`, the result is capped by `peak_net_cap`.

    The offer stays open while total owned is still under `commit_threshold`
    and under the peak, so a new stage is only available before the peak.
    """

    id: str
    cost_base: float
    growth_rate: float
    max_level: int
    active_production_multiplier: float
    peak_production_multiplier: float
    peak_total_owned_at_least: int
    commit_threshold_formula: CommitThresholdFormula
    structure: str = "single"
    peak_multiplier_per_stage: float = 0.0
    peak_net_cap: float | None = None

    @property
    def peak_net_multiplier(self) -> float:
        if self.structure == "multi_stage" and self.peak_net_cap is not None:
            return self.peak_net_cap
        return self.active_production_multiplier * self.peak_production_multiplier

    def applied_multiplier(self, level: int, total_owned: int) -> float:
        """Production factor for `level` owned stages at this copy count."""
        if level <= 0:
            return 1.0
        if level > self.max_level:
            raise ValueError(f"{self.id} level {level} is above max_level {self.max_level}")
        if self.structure == "multi_stage":
            dip = self.active_production_multiplier ** level
            if total_owned < self.peak_total_owned_at_least:
                return dip
            peak = self.peak_production_multiplier + self.peak_multiplier_per_stage * level
            raw = dip * peak
            if self.peak_net_cap is not None and level >= self.max_level:
                return min(raw, self.peak_net_cap)
            return raw
        multiplier = self.active_production_multiplier ** level
        if total_owned >= self.peak_total_owned_at_least:
            multiplier *= self.peak_production_multiplier ** level
        return multiplier


@dataclass(frozen=True)
class Generator:
    id: str
    tier: int
    cost_base: float
    growth_rate: float
    production_base: float


def commit_threshold(formula: CommitThresholdFormula, total_owned: int) -> int:
    """base + floor(total_owned / owned_divisor)."""
    if total_owned < 0:
        raise ValueError("total_owned must be >= 0")
    if formula.base < 1:
        raise ValueError("commit threshold base must be >= 1")
    if formula.owned_divisor < 1:
        raise ValueError("owned_divisor must be >= 1")
    return formula.base + math.floor(total_owned / formula.owned_divisor)


def offer_is_open(branch: Branch, total_owned: int) -> bool:
    """True while the contract can still be bought as a production dip.

    The scaling cap and the peak both have to sit strictly above total owned.
    Once total owned has reached the peak, the modifier would raise production
    immediately, so the offer is no longer a commit.
    """
    if total_owned < 0:
        raise ValueError("total_owned must be >= 0")
    if total_owned >= branch.peak_total_owned_at_least:
        return False
    return total_owned < commit_threshold(branch.commit_threshold_formula, total_owned)


def cost_next(cost_base: float, growth_rate: float, owned: int) -> float:
    """Next copy costs cost_base × growth_rate^owned.

    A growth rate of 1 is a flat price. Generators reject that in the loader.
    A multi-stage contract may use it because max_level bounds the spend.
    """
    if owned < 0:
        raise ValueError("owned must be >= 0")
    if growth_rate < 1:
        raise ValueError("growth_rate must be >= 1")
    if cost_base <= 0:
        raise ValueError("cost_base must be > 0")
    if growth_rate == 1:
        return float(cost_base)
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


def generator_branch_multiplier(
    branches: tuple[Branch, ...],
    levels: dict[str, int],
    total_owned: int,
) -> float:
    """Product of owned branch modifiers. 1 when the player has bought none."""
    multiplier = 1.0
    for branch in branches:
        level = levels.get(branch.id, 0)
        if level <= 0:
            continue
        multiplier *= branch.applied_multiplier(level, total_owned)
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
    horizon_seconds: float,
) -> float:
    """Production gained over the horizon, per currency spent on the next copy.

    The gain is marginal production times `horizon_seconds` of active play.
    Callers pass the configured ROI horizon, shortened to the time left until
    the next prestige when that moment comes first. A longer window scales
    every generator by the same factor, so generator order is unchanged.

    A branch whose multiplier changes inside the window is not scored here.
    Its production change is the integral of the two play paths over that
    same horizon, divided by its cost, and ranked against this ratio.
    """
    if horizon_seconds <= 0:
        raise ValueError("horizon_seconds must be > 0")
    cost = cost_next(generator.cost_base, generator.growth_rate, owned)
    if not math.isfinite(cost) or cost <= 0:
        return 0.0
    marginal = marginal_production(generator, owned, milestones, prestige_multiplier)
    if not math.isfinite(marginal):
        return 0.0
    return marginal * float(horizon_seconds) / cost


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
    # Any shared positive window preserves later < opening. One second is the
    # unit interval for that comparison, not a balance value.
    opening = return_on_cost(generator, 0, milestones, 1.0, horizon_seconds=1)
    if opening <= 0 or not math.isfinite(opening):
        return False
    later_cost = cost_next(generator.cost_base, generator.growth_rate, sample_owned)
    if not math.isfinite(later_cost):
        return True
    later = return_on_cost(generator, sample_owned, milestones, 1.0, horizon_seconds=1)
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
