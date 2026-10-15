"""Purchase policies used to test whether the curve is legible.

ROI buys the copy with the highest production change per currency over the
configured horizon (or until the next prestige, whichever comes first).
There is no second objective. A branch is ranked by that same ratio: the
integral of its production change over the window, divided by its cost.
The inner forecast ranks generators by instantaneous marginal so the
horizon search does not call itself.
"""

from __future__ import annotations

import math
import random
from dataclasses import dataclass

from model.generators import (
    cost_next,
    generator_branch_multiplier,
    marginal_production,
    offer_is_open,
    production_total,
    return_on_cost,
    upgrade_multiplier,
)
from model.load import Economy
from model.prestige import RunState, prestige_multiplier, prestige_points

_PURCHASE_TOLERANCE = 1e-8
_POLICIES = {"roi", "roi_noisy", "patient", "cheapest"}


@dataclass(frozen=True)
class Choice:
    generator_id: str
    cost: float
    marginal: float
    kind: str = "generator"

    @property
    def roi(self) -> float:
        if self.cost <= 0 or not math.isfinite(self.cost):
            return 0.0
        if not math.isfinite(self.marginal):
            return 0.0
        return self.marginal / self.cost


def _instant_generators(economy: Economy, state: RunState, allowed: set[str] | None) -> list[Choice]:
    """One more copy, scored by production per second. Not a horizon score."""
    prestige = prestige_multiplier(state.prestige_points, economy.prestige.point_value)
    choices: list[Choice] = []
    for generator in economy.generators:
        if allowed is not None and generator.id not in allowed:
            continue
        owned = state.owned[generator.id]
        cost = cost_next(generator.cost_base, generator.growth_rate, owned)
        if not math.isfinite(cost) or cost <= 0:
            continue
        marginal = marginal_production(generator, owned, economy.milestones, prestige)
        if marginal <= 0 or not math.isfinite(marginal):
            continue
        choices.append(Choice(generator.id, cost, marginal))
    return choices


def _patient_ids(economy: Economy, state: RunState) -> set[str]:
    """Tiers already opened, plus the single next tier. No skipping."""
    highest = -1
    for index, generator in enumerate(economy.generators):
        if state.owned[generator.id] > 0:
            highest = index
    last = min(highest + 1, len(economy.generators) - 1)
    return {economy.generators[index].id for index in range(last + 1)}


def generator_output(economy: Economy, state: RunState) -> float:
    """Generator production before the branch modifier. Clicks are not included."""
    prestige = prestige_multiplier(state.prestige_points, economy.prestige.point_value)
    total = 0.0
    for generator in economy.generators:
        owned = state.owned[generator.id]
        multipliers = upgrade_multiplier(owned, economy.milestones) * prestige
        total += production_total(generator.production_base, owned, multipliers)
    return total


def active_production(economy: Economy, state: RunState) -> float:
    """Same rate the run uses: clicks plus generators times the branch modifier."""
    prestige = prestige_multiplier(state.prestige_points, economy.prestige.point_value)
    clicks = economy.active_click_per_second * prestige
    total_owned = sum(state.owned.values())
    branch = generator_branch_multiplier(economy.branches, state.branch_levels, total_owned)
    return clicks + generator_output(economy, state) * branch


def _apply(state: RunState, choice: Choice) -> None:
    state.currency -= choice.cost
    if state.currency < 0 and abs(state.currency) < _PURCHASE_TOLERANCE * max(1.0, choice.cost):
        state.currency = 0.0
    if choice.kind == "branch":
        state.branch_levels[choice.generator_id] = state.branch_levels.get(choice.generator_id, 0) + 1
        return
    state.owned[choice.generator_id] = state.owned.get(choice.generator_id, 0) + 1


def _points_ready(economy: Economy, state: RunState) -> int:
    config = economy.prestige
    return prestige_points(
        state.lifetime_currency,
        coeff=config.coeff,
        divisor=config.divisor,
        exponent=config.exponent,
    )


def _best_instant(economy: Economy, state: RunState) -> Choice | None:
    options = _instant_generators(economy, state, None)
    if not options:
        return None
    return max(options, key=lambda option: (option.roi, -option.cost))


def play_horizon(
    economy: Economy,
    state: RunState,
    horizon_seconds: float,
    first: Choice | None,
) -> tuple[float, float, RunState]:
    """Currency produced until the horizon or the next prestige wall.

    `first` is bought before any later copy. Later copies are the best
    instantaneous generator. The forecast does not buy branches and does
    not call horizon ROI, so a branch decision cannot recurse.
    """
    if horizon_seconds < 0:
        raise ValueError("horizon_seconds must be >= 0")
    sim = state.copy()
    if horizon_seconds == 0:
        return 0.0, 0.0, sim
    produced = 0.0
    elapsed = 0.0
    pending = first
    steps = 0
    limit = economy.simulation.max_purchases
    stall = economy.prestige.stall_seconds
    points_needed = economy.prestige.min_points_to_prestige
    while elapsed < horizon_seconds - 1e-12 and steps < limit:
        steps += 1
        if pending is None:
            pending = _best_instant(economy, sim)
            if pending is None:
                rate = active_production(economy, sim)
                dt = horizon_seconds - elapsed
                produced += rate * dt
                sim.currency += rate * dt
                sim.lifetime_currency += rate * dt
                sim.t += dt
                elapsed = horizon_seconds
                break
        rate = active_production(economy, sim)
        if sim.currency + _PURCHASE_TOLERANCE >= pending.cost:
            if sim.currency < pending.cost:
                sim.currency = pending.cost
            _apply(sim, pending)
            pending = None
            continue
        if rate <= 0 or not math.isfinite(rate):
            break
        dt = (pending.cost - sim.currency) / rate
        if not math.isfinite(dt) or dt < 0:
            break
        if dt >= stall and _points_ready(economy, sim) >= points_needed:
            break
        remaining = horizon_seconds - elapsed
        step = dt if dt <= remaining else remaining
        produced += rate * step
        sim.lifetime_currency += rate * step
        sim.t += step
        elapsed += step
        if dt > remaining:
            sim.currency += rate * step
            break
        sim.currency = pending.cost
        _apply(sim, pending)
        pending = None
    return produced, elapsed, sim


def _available_branches(economy: Economy, state: RunState) -> list[Choice]:
    total_owned = sum(state.owned.values())
    available: list[Choice] = []
    for branch in economy.branches:
        level = state.branch_levels.get(branch.id, 0)
        if level >= branch.max_level or not offer_is_open(branch, total_owned):
            continue
        cost = cost_next(branch.cost_base, branch.growth_rate, level)
        if not math.isfinite(cost) or cost <= 0:
            continue
        available.append(Choice(branch.id, cost, 0.0, kind="branch"))
    return available


def _generator_horizon_choices(
    economy: Economy,
    state: RunState,
    allowed: set[str] | None,
    horizon_seconds: float,
) -> list[Choice]:
    prestige = prestige_multiplier(state.prestige_points, economy.prestige.point_value)
    choices: list[Choice] = []
    for generator in economy.generators:
        if allowed is not None and generator.id not in allowed:
            continue
        owned = state.owned[generator.id]
        cost = cost_next(generator.cost_base, generator.growth_rate, owned)
        if not math.isfinite(cost) or cost <= 0:
            continue
        ratio = return_on_cost(
            generator,
            owned,
            economy.milestones,
            prestige,
            horizon_seconds=horizon_seconds,
        )
        if ratio <= 0:
            continue
        choices.append(Choice(generator.id, cost, ratio * cost))
    return choices


def _branch_production_change(
    economy: Economy,
    state: RunState,
    branch: Choice,
    horizon_seconds: float,
    baseline_produced: float,
    baseline_elapsed: float,
) -> float:
    """Currency the branch adds over the shared window, versus not buying it."""
    produced, elapsed, _ = play_horizon(economy, state, horizon_seconds, branch)
    shared = min(baseline_elapsed, elapsed)
    if shared <= 0:
        return 0.0
    if baseline_elapsed > shared + 1e-6:
        base, _, _ = play_horizon(economy, state, shared, None)
    else:
        base = baseline_produced
    if elapsed > shared + 1e-6:
        gained, _, _ = play_horizon(economy, state, shared, branch)
    else:
        gained = produced
    return gained - base


def rank_choices(economy: Economy, state: RunState, policy: str) -> list[Choice]:
    """Highest horizon ROI first. Generators and branches share one ratio."""
    if policy not in _POLICIES:
        raise ValueError(f"unknown purchase policy: {policy}")
    allowed = _patient_ids(economy, state) if policy == "patient" else None
    horizon = economy.simulation.roi_horizon_minutes * 60.0
    branches = [] if policy == "cheapest" else _available_branches(economy, state)
    if not branches:
        ranked = _generator_horizon_choices(economy, state, allowed, horizon)
        if policy == "cheapest":
            return sorted(ranked, key=lambda option: (option.cost, option.generator_id))
        return sorted(ranked, key=lambda option: (option.roi, -option.cost), reverse=True)

    baseline_produced, baseline_elapsed, _ = play_horizon(economy, state, horizon, None)
    if baseline_elapsed <= 0:
        ranked = _generator_horizon_choices(economy, state, allowed, horizon)
        return sorted(ranked, key=lambda option: (option.roi, -option.cost), reverse=True)

    ranked = _generator_horizon_choices(economy, state, allowed, baseline_elapsed)
    for branch in branches:
        change = _branch_production_change(
            economy,
            state,
            branch,
            horizon,
            baseline_produced,
            baseline_elapsed,
        )
        ranked.append(Choice(branch.generator_id, branch.cost, change, kind="branch"))
    return sorted(ranked, key=lambda option: (option.roi, -option.cost), reverse=True)


def choose(economy: Economy, state: RunState, policy: str, rng: random.Random | None = None) -> Choice | None:
    if policy not in _POLICIES:
        raise ValueError(f"unknown purchase policy: {policy}")
    ranked = rank_choices(economy, state, policy)
    if not ranked:
        return None
    if policy == "cheapest":
        return ranked[0]
    if policy == "roi_noisy":
        if rng is None:
            raise ValueError("roi_noisy requires a seeded random generator")
        if len(ranked) > 1 and rng.random() < economy.simulation.noisy_second_best_probability:
            return ranked[1]
        return ranked[0]
    return ranked[0]
