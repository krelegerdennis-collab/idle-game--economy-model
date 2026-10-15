"""Purchase policies used to test whether the curve is legible.

The primary policy buys the copy with the highest marginal production per
currency. The others are a small population around that behavior. None of
them spend money; monetization offers are not a policy.
"""

from __future__ import annotations

import math
import random
from dataclasses import dataclass

from model.generators import cost_next, marginal_production
from model.load import Economy
from model.prestige import RunState, prestige_multiplier


@dataclass(frozen=True)
class Choice:
    generator_id: str
    cost: float
    marginal: float

    @property
    def roi(self) -> float:
        if self.cost <= 0 or not math.isfinite(self.cost):
            return 0.0
        return self.marginal / self.cost


def _choices(economy: Economy, state: RunState, allowed: set[str] | None) -> list[Choice]:
    prestige = prestige_multiplier(state.prestige_points, economy.prestige.point_value)
    choices: list[Choice] = []
    for generator in economy.generators:
        if allowed is not None and generator.id not in allowed:
            continue
        cost = cost_next(generator.cost_base, generator.growth_rate, state.owned[generator.id])
        if not math.isfinite(cost):
            continue
        marginal = marginal_production(
            generator,
            state.owned[generator.id],
            economy.milestones,
            prestige,
        )
        if marginal <= 0:
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


def choose(economy: Economy, state: RunState, policy: str, rng: random.Random | None = None) -> Choice | None:
    if policy == "patient":
        options = _choices(economy, state, _patient_ids(economy, state))
    else:
        options = _choices(economy, state, None)
    if not options:
        return None
    if policy == "cheapest":
        return min(options, key=lambda option: (option.cost, option.generator_id))
    ranked = sorted(options, key=lambda option: (option.roi, -option.cost), reverse=True)
    if policy == "roi_noisy":
        if rng is None:
            raise ValueError("roi_noisy requires a seeded random generator")
        if len(ranked) > 1 and rng.random() < economy.simulation.noisy_second_best_probability:
            return ranked[1]
        return ranked[0]
    if policy not in {"roi", "patient"}:
        raise ValueError(f"unknown purchase policy: {policy}")
    return ranked[0]
