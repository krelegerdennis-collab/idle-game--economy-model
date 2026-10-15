"""Place playtest logs next to the frozen baseline and print the differences.

The simulation side is read from exports/runs/baseline.json. Nothing here
recomputes the economy. A missing cohort leaves the playtest side null.

    python -m sim.compare_playtest_to_sim
    python -m sim.compare_playtest_to_sim --logs path/to/playtest_log.jsonl
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from sim.analyze_playtest import analyze_events, analyze_path

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_BASELINE = ROOT / "exports" / "runs" / "baseline.json"


def simulation_reference(baseline: dict[str, object]) -> dict[str, float | None]:
    """Take rate, purchase-time width, D1, and D30 from a frozen run report."""
    choices = baseline.get("contract_choices")
    times: list[float] = []
    if isinstance(choices, list):
        for choice in choices:
            if not isinstance(choice, dict) or not choice.get("bought"):
                continue
            if "active_minutes" not in choice:
                continue
            times.append(float(choice["active_minutes"]))
    window = None if len(times) < 2 else max(times) - min(times)
    retention = baseline.get("retention")
    d1 = None
    d30 = None
    if isinstance(retention, dict):
        if "d1" in retention:
            d1 = float(retention["d1"])
        if "d30" in retention:
            d30 = float(retention["d30"])
    take = baseline.get("contract_take_rate")
    return {
        "take_rate": None if take is None else float(take),
        "contract_window_minutes": window,
        "d1": d1,
        "d30": d30,
    }


def _difference(playtest: object, simulation: float | None) -> float | None:
    if playtest is None or simulation is None:
        return None
    return float(playtest) - float(simulation)


def compare_reports(playtest: dict[str, object], baseline: dict[str, object]) -> dict[str, object]:
    """Numeric difference per metric. No verdict."""
    simulation = simulation_reference(baseline)
    retention = playtest.get("retention")
    playtest_d1 = None
    playtest_d30 = None
    if isinstance(retention, dict):
        playtest_d1 = retention.get("d1")
        playtest_d30 = retention.get("d30")
    pairs = {
        "take_rate": (simulation["take_rate"], playtest.get("take_rate")),
        "contract_window_minutes": (
            simulation["contract_window_minutes"],
            playtest.get("contract_purchase_time_median_minutes"),
        ),
        "d1": (simulation["d1"], playtest_d1),
        "d30": (simulation["d30"], playtest_d30),
    }
    report: dict[str, object] = {}
    for name, (simulated, observed) in pairs.items():
        report[name] = {
            "simulation": simulated,
            "playtest": observed,
            "difference": _difference(observed, simulated),
        }
    report["contract_window_minutes"]["playtest_width_minutes"] = playtest.get(
        "contract_purchase_time_width_minutes"
    )
    report["playtest_players"] = playtest.get("session_players", 0)
    return report


def format_comparison(report: dict[str, object]) -> str:
    """The four lines the playtest phase compares. A missing cohort stays blank."""
    take = report["take_rate"]
    window = report["contract_window_minutes"]
    d1 = report["d1"]
    d30 = report["d30"]
    assert isinstance(take, dict)
    assert isinstance(window, dict)
    assert isinstance(d1, dict)
    assert isinstance(d30, dict)
    return "\n".join(
        [
            f"Take rate: simulatie {_pct(take['simulation'])} vs. playtest {_pct(take['playtest'])}",
            (
                "Contract-venster: simulatie "
                f"{_minutes(window['simulation'])} min vs. playtest mediaan {_minutes(window['playtest'])} min"
            ),
            f"D1: simulatie {_pct(d1['simulation'])} vs. playtest {_pct(d1['playtest'])}",
            f"D30: simulatie {_pct(d30['simulation'])} vs. playtest {_pct(d30['playtest'])}",
            f"verschil take rate: {_num(take['difference'])}",
            f"verschil contract-venster (mediaan - simulatie): {_num(window['difference'])}",
            f"verschil D1: {_num(d1['difference'])}",
            f"verschil D30: {_num(d30['difference'])}",
        ]
    )


def _num(value: object) -> str:
    if value is None:
        return "—"
    text = f"{float(value):.6f}".rstrip("0").rstrip(".")
    return text.replace(".", ",")


def _pct(value: object) -> str:
    if value is None:
        return "—"
    return _num(float(value) * 100.0) + "%"


def _minutes(value: object) -> str:
    if value is None:
        return "—"
    return _num(value)


def empty_playtest() -> dict[str, object]:
    return analyze_events({})


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Diff playtest logs against the frozen baseline.")
    parser.add_argument("--baseline", type=Path, default=DEFAULT_BASELINE)
    parser.add_argument("--logs", type=Path, default=None)
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args(argv)
    baseline = json.loads(args.baseline.read_text(encoding="utf-8"))
    if args.logs is None or not args.logs.exists():
        playtest = empty_playtest()
    else:
        playtest = analyze_path(args.logs)
    report = compare_reports(playtest, baseline)
    if args.json:
        print(json.dumps(report, indent=2, ensure_ascii=False))
    else:
        print(format_comparison(report))
    return 0


if __name__ == "__main__":
    sys.exit(main())
