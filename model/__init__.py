"""Mathematical economy model for an idle game. Not a game."""

from model.generators import cost_next, production_total
from model.monetization import monetization_gate
from model.offline import offline_earnings
from model.prestige import prestige_multiplier, prestige_reset

__all__ = [
    "cost_next",
    "monetization_gate",
    "offline_earnings",
    "prestige_multiplier",
    "prestige_reset",
    "production_total",
]
