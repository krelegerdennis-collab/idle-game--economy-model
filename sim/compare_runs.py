"""Compare two economy runs.

By default the current /data model is compared with exports/runs/baseline.json.
--other may be a report JSON, a data directory, or a git ref whose data/
files should be simulated.

    python -m sim.compare_runs
    python -m sim.compare_runs --other experiment/higher-cost-exponent
    python -m sim.compare_runs --other path/to/other/data
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import tempfile
from pathlib import Path

import pandas as pd

from model.load import ROOT, load_economy
from sim.run_simulation import build_report


def _closeness(minutes: float | None, target: float) -> float:
    if minutes is None:
        return float("inf")
    return abs(minutes - target)


def verdict(current: dict[str, object], other: dict[str, object], target_minutes: float = 90.0) -> dict[str, str]:
    """Say which run is closer to the prestige-wall target and which has better D7."""
    current_wall = current.get("prestige_wall_minutes")
    other_wall = other.get("prestige_wall_minutes")
    current_distance = _closeness(None if current_wall is None else float(current_wall), target_minutes)
    other_distance = _closeness(None if other_wall is None else float(other_wall), target_minutes)
    if current_distance < other_distance:
        closer = "current"
    elif other_distance < current_distance:
        closer = "other"
    else:
        closer = "tie"

    current_d7 = float(current["retention"]["d7"])  # type: ignore[index]
    other_d7 = float(other["retention"]["d7"])  # type: ignore[index]
    if current_d7 > other_d7:
        better_d7 = "current"
    elif other_d7 > current_d7:
        better_d7 = "other"
    else:
        better_d7 = "tie"
    return {"closer_to_prestige_wall": closer, "better_d7": better_d7}


def _is_report(path: Path) -> bool:
    if not path.is_file() or path.suffix != ".json":
        return False
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return False
    return isinstance(payload, dict) and "prestige_wall_minutes" in payload and "retention" in payload


def _load_report(path: Path) -> dict[str, object]:
    payload = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(payload, dict):
        raise ValueError(f"{path} is not a simulation report")
    return payload


def _data_dir_from_git(ref: str, root: Path) -> Path:
    """Materialize data/*.json from a git ref into a temp directory."""
    probe = subprocess.run(
        ["git", "rev-parse", "--verify", "--quiet", ref],
        cwd=root,
        capture_output=True,
        text=True,
        check=False,
    )
    if probe.returncode != 0:
        raise FileNotFoundError(f"not a data directory, report JSON, or git ref: {ref}")
    listing = subprocess.run(
        ["git", "ls-tree", "-r", "--name-only", ref, "data"],
        cwd=root,
        capture_output=True,
        text=True,
        check=True,
    )
    files = [line for line in listing.stdout.splitlines() if line.endswith(".json")]
    if not files:
        raise FileNotFoundError(f"git ref {ref} has no data/*.json files")
    directory = Path(tempfile.mkdtemp(prefix="economy-compare-"))
    for relative in files:
        show = subprocess.run(
            ["git", "show", f"{ref}:{relative}"],
            cwd=root,
            capture_output=True,
            text=True,
            check=True,
        )
        target = directory / Path(relative).name
        target.write_text(show.stdout, encoding="utf-8")
    return directory


def load_other(spec: str, root: Path = ROOT) -> dict[str, object]:
    """Load a report, simulate a data directory, or simulate data from a git ref."""
    path = Path(spec)
    if not path.is_absolute():
        path = root / path
    if _is_report(path):
        return _load_report(path)
    if path.is_dir():
        data_dir = path if (path / "generators.json").is_file() else path / "data"
        if not (data_dir / "generators.json").is_file():
            raise FileNotFoundError(f"no generators.json under {spec}")
        return build_report(load_economy(data_dir))
    return build_report(load_economy(_data_dir_from_git(spec, root)))


def comparison_frame(current: dict[str, object], other: dict[str, object]) -> pd.DataFrame:
    rows = []
    for label, report in (("current", current), ("other", other)):
        retention = report["retention"]
        constraints = report["constraints"]
        assert isinstance(retention, dict)
        assert isinstance(constraints, dict)
        rows.append(
            {
                "run": label,
                "prestige_wall_minutes": report.get("prestige_wall_minutes"),
                "d1": retention.get("d1"),
                "d7": retention.get("d7"),
                "d30": retention.get("d30"),
                "session_cadence": report.get("session_cadence"),
                "purchase_agreement": report.get("purchase_agreement"),
                "multiplier_after_prestige": report.get("multiplier_after_prestige"),
                "constraints_ok": constraints.get("ok"),
            }
        )
    return pd.DataFrame(rows)


def format_comparison(current: dict[str, object], other: dict[str, object], target_minutes: float = 90.0) -> str:
    frame = comparison_frame(current, other)
    decision = verdict(current, other, target_minutes)
    closer = decision["closer_to_prestige_wall"]
    better = decision["better_d7"]
    closer_text = {
        "current": f"current is closer to the {target_minutes:.0f} minute prestige wall",
        "other": f"other is closer to the {target_minutes:.0f} minute prestige wall",
        "tie": f"both runs are equally close to the {target_minutes:.0f} minute prestige wall",
    }[closer]
    d7_text = {
        "current": "current has the higher D7 projection",
        "other": "other has the higher D7 projection",
        "tie": "both runs have the same D7 projection",
    }[better]
    return frame.to_string(index=False) + "\n\n" + closer_text + "\n" + d7_text


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Compare the current economy with another run.")
    parser.add_argument(
        "--other",
        default=str(ROOT / "exports" / "runs" / "baseline.json"),
        help="Report JSON, data directory, or git ref. Default: exports/runs/baseline.json",
    )
    parser.add_argument(
        "--target-minutes",
        type=float,
        default=None,
        help="Prestige-wall target. Defaults to data/retention_targets.json.",
    )
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args(argv)
    economy = load_economy(ROOT / "data")
    target = (
        economy.retention.first_prestige_wall_minutes_target
        if args.target_minutes is None
        else args.target_minutes
    )
    current = build_report(economy)
    other = load_other(args.other)
    decision = verdict(current, other, target)
    if args.json:
        print(json.dumps({"verdict": decision, "table": comparison_frame(current, other).to_dict(orient="records")}, indent=2))
    else:
        print(format_comparison(current, other, target))
    return 0


if __name__ == "__main__":
    sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
    raise SystemExit(main())
