"""Insight and relics: a reward axis beside the prestige multiplier.

Relics persist across resets. Nothing in this module adds prestige points.
The only way insight becomes prestige points is the exchange, and that
exchange waits until the next reset.
"""

from __future__ import annotations

import hashlib
from dataclasses import dataclass

from model.generators import Milestone
from model.offline import OFFLINE_RATE_CAP
from model.prestige import RunState


@dataclass(frozen=True)
class InsightSource:
    id: str
    kind: str
    threshold: int
    insight: int


@dataclass(frozen=True)
class PrestigeExchange:
    """Spend insight on prestige points that land on the next reset."""

    id: str
    insight_cost: int
    bonus_points: int
    applies: str


@dataclass(frozen=True)
class Relic:
    id: str
    insight_cost: int
    max_level: int
    offline_rate_add: float = 0.0
    session_start_free_generator_chance: float = 0.0
    free_generator_id: str | None = None
    extra_milestone: Milestone | None = None
    session_open_boost_multiplier: float = 1.0
    session_open_boost_seconds: float = 0.0


@dataclass(frozen=True)
class RelicConfig:
    currency_id: str
    sources: tuple[InsightSource, ...]
    prestige_exchange: PrestigeExchange | None
    relics: tuple[Relic, ...]


def session_roll(seed: int, prestige_count: int) -> float:
    """Unit interval fixed by the run index and the configured seed."""
    digest = hashlib.sha256(f"{seed}:{prestige_count}".encode()).hexdigest()
    return int(digest[:8], 16) / 0xFFFFFFFF


def effective_milestones(
    base: tuple[Milestone, ...],
    relics: tuple[Relic, ...],
    levels: dict[str, int],
) -> tuple[Milestone, ...]:
    """Baseline milestones plus slots owned relics have opened."""
    extras: list[Milestone] = []
    for relic in relics:
        if levels.get(relic.id, 0) <= 0 or relic.extra_milestone is None:
            continue
        extras.append(relic.extra_milestone)
    if not extras:
        return base
    return base + tuple(extras)


def effective_offline_rate(
    base_rate: float,
    relics: tuple[Relic, ...],
    levels: dict[str, int],
    *,
    cap: float = OFFLINE_RATE_CAP,
) -> float:
    """Offline rate after relic bonuses. The 0.5 cap still binds."""
    rate = float(base_rate)
    for relic in relics:
        level = levels.get(relic.id, 0)
        if level <= 0 or relic.offline_rate_add == 0:
            continue
        rate += relic.offline_rate_add * level
    if rate < 0:
        return 0.0
    return min(cap, rate)


def opening_boost(state: RunState) -> float:
    """Session-open multiplier. 1 once the boost window has elapsed."""
    if state.t < state.session_boost_until and state.session_boost_multiplier > 0:
        return state.session_boost_multiplier
    return 1.0


def begin_session(config: RelicConfig, state: RunState, roll: float) -> None:
    """Apply session-start relic effects once, at the beginning of a run."""
    if state.session_started:
        return
    state.session_started = True
    if state.t != 0:
        return
    multiplier = 1.0
    until = 0.0
    for relic in config.relics:
        level = state.relic_levels.get(relic.id, 0)
        if level <= 0:
            continue
        if relic.session_open_boost_seconds > 0 and relic.session_open_boost_multiplier > 0:
            multiplier *= relic.session_open_boost_multiplier ** level
            until = max(until, relic.session_open_boost_seconds)
        chance = relic.session_start_free_generator_chance
        if chance > 0 and relic.free_generator_id and roll < chance:
            generator_id = relic.free_generator_id
            state.owned[generator_id] = state.owned.get(generator_id, 0) + level
    state.session_boost_multiplier = multiplier
    state.session_boost_until = until


def claim_insight(
    config: RelicConfig,
    state: RunState,
    *,
    total_owned: int,
    prestige_points_ready: int,
) -> list[str]:
    """Grant each matching source once per run. Returns the source ids paid."""
    paid: list[str] = []
    for source in config.sources:
        if source.id in state.claimed_insight_sources:
            continue
        if source.kind == "total_owned_at_least" and total_owned < source.threshold:
            continue
        if source.kind == "prestige_points_at_least" and prestige_points_ready < source.threshold:
            continue
        if source.kind not in {"total_owned_at_least", "prestige_points_at_least"}:
            continue
        state.insight += source.insight
        state.claimed_insight_sources.add(source.id)
        paid.append(source.id)
    return paid
