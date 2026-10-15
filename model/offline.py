"""Offline earning.

    earnings = production × time_away × offline_rate

`time_away` is capped. `offline_rate` cannot exceed 0.5: past that, being
away beats playing, and the check-in loop that retention depends on dies.
"""

from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class OfflineConfig:
    offline_rate: float
    cap_hours: float
    waking_hours: float

    @property
    def cap_seconds(self) -> float:
        return self.cap_hours * 3600.0

    @property
    def session_cadence(self) -> float:
        """Check-ins per waking day if the player returns when the cap fills."""
        if self.cap_hours <= 0:
            raise ValueError("cap_hours must be > 0")
        return self.waking_hours / self.cap_hours


def offline_earnings(
    production: float,
    time_away_seconds: float,
    offline_rate: float,
    cap_seconds: float,
) -> float:
    """Currency earned while away, at a fraction of the live production rate."""
    if offline_rate > 0.5:
        raise ValueError("offline_rate must be <= 0.5")
    if offline_rate < 0:
        raise ValueError("offline_rate must be >= 0")
    if production < 0:
        raise ValueError("production must be >= 0")
    if time_away_seconds < 0 or cap_seconds < 0:
        raise ValueError("time values must be >= 0")
    effective_time = min(time_away_seconds, cap_seconds)
    return float(production) * effective_time * float(offline_rate)
