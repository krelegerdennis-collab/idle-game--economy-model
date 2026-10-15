"""Monetization gates.

Gates sit at the first prestige wall and at every 3rd prestige
(the 3rd, 6th, 9th, ... reset). A gate is an optional offer. The default
action is to decline it. The free path has to reach the prestige wall on
its own. Nothing here should be tuned to increase spending.
"""

from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class MonetizationConfig:
    every_nth_prestige: int
    optional_boost_multiplier: float
    optional_boost_seconds: float
    default_action: str


def monetization_gate(
    prestige_count: int,
    at_first_wall: bool,
    every_nth_prestige: int,
) -> dict[str, object]:
    """Whether a gate is available.

    `prestige_count` is how many resets the player has already completed.
    The upcoming reset is `prestige_count + 1`.
    """
    if prestige_count < 0:
        raise ValueError("prestige_count must be >= 0")
    if every_nth_prestige < 1:
        raise ValueError("every_nth_prestige must be >= 1")

    upcoming = prestige_count + 1
    first_wall = prestige_count == 0 and at_first_wall
    # The 3rd, 6th, 9th... reset. The first reset is the wall gate, not this one.
    every_nth = upcoming % every_nth_prestige == 0 and upcoming != 1
    if first_wall:
        reason = "first_prestige_wall"
    elif every_nth:
        reason = "every_nth_prestige"
    else:
        reason = None
    return {
        "active": reason is not None,
        "reason": reason,
        "upcoming_prestige_number": upcoming,
        "optional": True,
        "default_action": "decline_offer",
    }
