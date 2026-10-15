"""Read playtest JSON-lines logs and report what players did.

One JSONL file is one install when events have no player_id. A directory of
files is a cohort. Rates that the logs are too short to support are null.
This script does not invent a cohort and does not read the economy model.

    python -m sim.analyze_playtest path/to/playtest_log.jsonl
"""

from __future__ import annotations

import argparse
import json
import statistics
import sys
from collections import defaultdict
from datetime import datetime, timedelta, timezone
from pathlib import Path


def parse_timestamp(value: object) -> datetime:
    """UTC timestamp. Naive values are treated as UTC."""
    text = str(value).replace("Z", "+00:00")
    parsed = datetime.fromisoformat(text)
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=timezone.utc)
    return parsed.astimezone(timezone.utc)


def load_jsonl(path: Path) -> tuple[list[dict], int]:
    """Events from one file, plus how many lines were not JSON objects."""
    events: list[dict] = []
    skipped = 0
    text = path.read_text(encoding="utf-8")
    for line in text.splitlines():
        if not line.strip():
            continue
        try:
            payload = json.loads(line)
        except json.JSONDecodeError:
            skipped += 1
            continue
        if not isinstance(payload, dict) or "event" not in payload:
            skipped += 1
            continue
        events.append(payload)
    return events, skipped


def log_files(path: Path) -> list[Path]:
    if path.is_file():
        return [path]
    if path.is_dir():
        return sorted(item for item in path.rglob("*.jsonl") if item.is_file())
    return []


def _player_key(event: dict, source: str) -> str:
    player_id = event.get("player_id")
    if player_id is None or player_id == "":
        return f"file:{source}"
    return str(player_id)


def _seconds(event: dict, key: str) -> float | None:
    value = event.get(key)
    if value is None or isinstance(value, bool):
        return None
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


def _minutes_from_seconds(seconds: float | None) -> float | None:
    if seconds is None:
        return None
    return seconds / 60.0


def _median(values: list[float]) -> float | None:
    if not values:
        return None
    return float(statistics.median(values))


def _width(values: list[float]) -> float | None:
    if len(values) < 2:
        return None
    return max(values) - min(values)


def _retention(begins: dict[str, list[datetime]], observation_end: datetime | None) -> dict[str, object]:
    """Share of eligible players with a session_begin at least N calendar days later.

    A player is eligible for day N only when the cohort was observed for N days
    after their first session. Otherwise the rate is null, not zero.
    """
    report: dict[str, object] = {}
    for day in (1, 7, 30):
        if observation_end is None:
            report[f"d{day}"] = None
            report[f"d{day}_eligible_players"] = 0
            report[f"d{day}_returned_players"] = 0
            continue
        eligible = 0
        returned = 0
        horizon = timedelta(days=day)
        for stamps in begins.values():
            first = min(stamps)
            if observation_end - first < horizon:
                continue
            eligible += 1
            first_date = first.date()
            if any((stamp.date() - first_date).days >= day for stamp in stamps):
                returned += 1
        report[f"d{day}"] = None if eligible == 0 else returned / eligible
        report[f"d{day}_eligible_players"] = eligible
        report[f"d{day}_returned_players"] = returned
    return report


def analyze_events(grouped: dict[str, list[dict]], *, skipped_lines: int = 0) -> dict[str, object]:
    """Aggregate one cohort. `grouped` maps player id to that player's events."""
    purchase_moments: list[dict[str, object]] = []
    seen_not_bought: list[dict[str, object]] = []
    not_shown: list[str] = []
    prestige_counts: dict[str, int] = {}
    relic_players: dict[str, list[str]] = defaultdict(list)
    relic_counts: dict[str, int] = defaultdict(int)
    orders: dict[str, list[str]] = {}
    session_begins: dict[str, list[datetime]] = {}
    purchase_times: list[float] = []
    stamped: list[datetime] = []
    session_players = 0

    for player_id, events in grouped.items():
        ordered = sorted(events, key=lambda item: str(item.get("timestamp", "")))
        names = [str(item.get("event")) for item in ordered]
        if "session_begin" in names:
            session_players += 1
        shown = [item for item in ordered if item.get("event") == "contract_offer_shown"]
        bought = [item for item in ordered if item.get("event") == "contract_purchase"]
        declined = [item for item in ordered if item.get("event") == "contract_declined"]
        if bought:
            for item in bought:
                active = _minutes_from_seconds(_seconds(item, "active_playtime"))
                since_offer = _minutes_from_seconds(_seconds(item, "time_since_offer_shown"))
                if active is not None:
                    purchase_times.append(active)
                purchase_moments.append(
                    {
                        "player_id": player_id,
                        "timestamp": item.get("timestamp"),
                        "active_minutes": active,
                        "minutes_since_offer": since_offer,
                        "owned_at_purchase": item.get("owned_at_purchase"),
                    }
                )
        elif shown:
            offer = shown[0]
            decline = declined[-1] if declined else None
            seen_not_bought.append(
                {
                    "player_id": player_id,
                    "offer_timestamp": offer.get("timestamp"),
                    "owned_at_offer": offer.get("owned_at_offer"),
                    "active_minutes_at_offer": _minutes_from_seconds(_seconds(offer, "active_playtime")),
                    "decline_timestamp": None if decline is None else decline.get("timestamp"),
                    "reason_if_known": None if decline is None else decline.get("reason_if_known"),
                    "owned_at_decline": None if decline is None else decline.get("owned_at_decline"),
                }
            )
        elif "session_begin" in names:
            not_shown.append(player_id)

        prestige = [item for item in ordered if item.get("event") == "prestige_trigger"]
        if prestige:
            prestige_counts[player_id] = len(prestige)
        for item in ordered:
            if item.get("event") != "relic_purchase":
                continue
            relic_id = str(item.get("relic_id"))
            relic_counts[relic_id] += 1
            if relic_id not in relic_players[player_id]:
                relic_players[player_id].append(relic_id)

        unlock: list[str] = []
        for item in ordered:
            if item.get("event") != "upgrade_purchase":
                continue
            generator = str(item.get("generator_type"))
            if generator not in unlock:
                unlock.append(generator)
        if unlock:
            orders[player_id] = unlock

        begins: list[datetime] = []
        for item in ordered:
            if "timestamp" not in item:
                continue
            try:
                stamp = parse_timestamp(item["timestamp"])
            except (TypeError, ValueError):
                continue
            stamped.append(stamp)
            if item.get("event") == "session_begin":
                begins.append(stamp)
        if begins:
            session_begins[player_id] = begins

    order_counts: dict[tuple[str, ...], int] = defaultdict(int)
    for unlock in orders.values():
        order_counts[tuple(unlock)] += 1
    distribution = [
        {"order": list(order), "players": count}
        for order, count in sorted(order_counts.items(), key=lambda item: (-item[1], item[0]))
    ]
    observation_end = max(stamped) if stamped else None
    buyers = len({moment["player_id"] for moment in purchase_moments})
    take_rate = None if session_players == 0 else buyers / session_players
    return {
        "player_count": len(grouped),
        "session_players": session_players,
        "skipped_lines": skipped_lines,
        "contract_purchased": {
            "players": buyers,
            "events": len(purchase_moments),
            "moments": purchase_moments,
        },
        "contract_seen_not_purchased": {
            "players": len(seen_not_bought),
            "moments": seen_not_bought,
        },
        "contract_not_shown": {
            "players": len(not_shown),
            "player_ids": not_shown,
        },
        "take_rate": take_rate,
        "contract_purchase_time_median_minutes": _median(purchase_times),
        "contract_purchase_time_width_minutes": _width(purchase_times),
        "prestige": {
            "players": len(prestige_counts),
            "events": sum(prestige_counts.values()),
            "per_player": [
                {"player_id": player_id, "count": count}
                for player_id, count in sorted(prestige_counts.items())
            ],
        },
        "relics": {
            "players": len(relic_players),
            "by_id": dict(sorted(relic_counts.items())),
            "per_player": [
                {"player_id": player_id, "relic_ids": relic_ids}
                for player_id, relic_ids in sorted(relic_players.items())
            ],
        },
        "purchase_orders": distribution,
        "retention": _retention(session_begins, observation_end),
        "observation_end": None if observation_end is None else observation_end.isoformat(),
    }


def analyze_path(path: Path) -> dict[str, object]:
    """Load every JSONL log under `path` and aggregate by player id."""
    grouped: dict[str, list[dict]] = defaultdict(list)
    skipped = 0
    files = log_files(path)
    for item in files:
        events, bad = load_jsonl(item)
        skipped += bad
        for event in events:
            grouped[_player_key(event, str(item))].append(event)
    report = analyze_events(grouped, skipped_lines=skipped)
    report["log_files"] = [str(item) for item in files]
    return report


def format_report(report: dict[str, object]) -> str:
    """Dutch text for the six playtest questions. Numbers only, no verdict."""
    purchased = report["contract_purchased"]
    seen = report["contract_seen_not_purchased"]
    hidden = report["contract_not_shown"]
    prestige = report["prestige"]
    relics = report["relics"]
    retention = report["retention"]
    assert isinstance(purchased, dict)
    assert isinstance(seen, dict)
    assert isinstance(hidden, dict)
    assert isinstance(prestige, dict)
    assert isinstance(relics, dict)
    assert isinstance(retention, dict)
    lines = [
        f"spelers: {report['player_count']}",
        f"contract gekocht: {purchased['players']} spelers, {purchased['events']} events",
        f"contract gezien, niet gekocht: {seen['players']} spelers",
        f"contract niet getoond: {hidden['players']} spelers",
        f"take rate: {_plain(report['take_rate'])}",
        "koopmomenten (actieve minuten): "
        + ", ".join(_plain(moment.get("active_minutes")) for moment in purchased["moments"])
        if purchased["moments"]
        else "koopmomenten (actieve minuten): —",
        f"mediaan koopmoment: {_plain(report['contract_purchase_time_median_minutes'])} min",
        f"prestige: {prestige['players']} spelers, {prestige['events']} events",
        "relics: "
        + (
            ", ".join(f"{relic_id}={count}" for relic_id, count in relics["by_id"].items())
            if relics["by_id"]
            else "—"
        ),
        "purchase-order: "
        + (
            "; ".join(
                f"{' > '.join(item['order'])} ({item['players']})" for item in report["purchase_orders"]
            )
            if report["purchase_orders"]
            else "—"
        ),
        f"D1: {_plain(retention['d1'])} (eligible {retention['d1_eligible_players']})",
        f"D7: {_plain(retention['d7'])} (eligible {retention['d7_eligible_players']})",
        f"D30: {_plain(retention['d30'])} (eligible {retention['d30_eligible_players']})",
    ]
    return "\n".join(lines)


def _plain(value: object) -> str:
    if value is None:
        return "—"
    if isinstance(value, float):
        return f"{value:.6f}".rstrip("0").rstrip(".")
    return str(value)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Aggregate playtest JSONL logs.")
    parser.add_argument("path", type=Path, help="A JSONL file or a directory of them.")
    parser.add_argument("--json", action="store_true", help="Print the machine-readable report.")
    args = parser.parse_args(argv)
    if not args.path.exists():
        print(f"geen logs op {args.path}")
        print(format_report(analyze_events({})))
        return 0
    report = analyze_path(args.path)
    if args.json:
        print(json.dumps(report, indent=2, ensure_ascii=False))
    else:
        print(format_report(report))
    return 0


if __name__ == "__main__":
    sys.exit(main())
