"""Export data/ into ScriptableObject-shaped JSON and a spreadsheet CSV.

The export is a projection of the tunable files. If it disagrees with
data/, the export is stale and the model files win.

    python -m sim.export_unity
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np
import pandas as pd

from model.generators import cost_curve, production_total, upgrade_multiplier
from model.load import ROOT, Economy, load_economy


def build_unity_payload(economy: Economy) -> dict[str, object]:
    """Camel-case payload a Unity ScriptableObject importer can read."""
    return {
        "schemaVersion": 1,
        "currencyId": economy.currency_id,
        "startingCurrency": economy.starting_currency,
        "activeClickPerSecond": economy.active_click_per_second,
        "formulas": {
            "costNext": "cost_base * growth_rate^owned",
            "productionTotal": "production_base * owned * multipliers",
            "prestigeReset": "floor(coeff * (lifetime_currency / divisor)^exponent)",
            "prestigeMultiplier": "(1 + point_value)^total_points",
            "offlineEarnings": "production * min(time_away, cap_seconds) * offline_rate",
        },
        "generators": [
            {
                "id": generator.id,
                "tier": generator.tier,
                "costBase": generator.cost_base,
                "growthRate": generator.growth_rate,
                "productionBase": generator.production_base,
            }
            for generator in economy.generators
        ],
        "upgrades": [
            {
                "ownedAtLeast": milestone.owned_at_least,
                "productionMultiplier": milestone.production_multiplier,
            }
            for milestone in economy.milestones
        ],
        "prestige": {
            "coeff": economy.prestige.coeff,
            "divisor": economy.prestige.divisor,
            "exponent": economy.prestige.exponent,
            "pointValue": economy.prestige.point_value,
            "minPointsToPrestige": economy.prestige.min_points_to_prestige,
            "stallSeconds": economy.prestige.stall_seconds,
        },
        "offline": {
            "offlineRate": economy.offline.offline_rate,
            "capHours": economy.offline.cap_hours,
            "wakingHours": economy.offline.waking_hours,
        },
        "monetization": {
            "everyNthPrestige": economy.monetization.every_nth_prestige,
            "optionalBoostMultiplier": economy.monetization.optional_boost_multiplier,
            "optionalBoostSeconds": economy.monetization.optional_boost_seconds,
            "defaultAction": economy.monetization.default_action,
        },
        "analyticsEvents": [
            {"name": event["name"], "properties": list(event["properties"])}
            for event in economy.analytics_events
        ],
    }


def spreadsheet_frame(economy: Economy) -> pd.DataFrame:
    """One row per generator, with cost and production at owned milestones."""
    owned_marks = np.array([0, 10, 25, 50, 100], dtype=np.int64)
    rows: list[dict[str, object]] = []
    for generator in economy.generators:
        costs = cost_curve(generator.cost_base, generator.growth_rate, owned_marks)
        row: dict[str, object] = {
            "tier": generator.tier,
            "id": generator.id,
            "cost_base": generator.cost_base,
            "growth_rate": generator.growth_rate,
            "production_base": generator.production_base,
        }
        for mark, cost in zip(owned_marks, costs):
            multipliers = upgrade_multiplier(int(mark), economy.milestones)
            row[f"cost_at_{int(mark)}"] = round(float(cost), 6)
            row[f"production_at_{int(mark)}"] = round(
                production_total(generator.production_base, int(mark), multipliers),
                6,
            )
        rows.append(row)
    return pd.DataFrame(rows)


def write_exports(economy: Economy, unity_path: Path, spreadsheet_path: Path) -> None:
    unity_path.parent.mkdir(parents=True, exist_ok=True)
    spreadsheet_path.parent.mkdir(parents=True, exist_ok=True)
    payload = build_unity_payload(economy)
    unity_path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    frame = spreadsheet_frame(economy)
    frame.to_csv(spreadsheet_path, index=False)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Export the economy model for Unity and spreadsheets.")
    parser.add_argument("--data", type=Path, default=ROOT / "data")
    parser.add_argument("--unity", type=Path, default=ROOT / "exports" / "unity" / "economy_config.json")
    parser.add_argument("--spreadsheet", type=Path, default=ROOT / "exports" / "spreadsheet" / "generators.csv")
    args = parser.parse_args(argv)
    economy = load_economy(args.data)
    write_exports(economy, args.unity, args.spreadsheet)
    print(f"wrote {args.unity}")
    print(f"wrote {args.spreadsheet}")
    return 0


if __name__ == "__main__":
    sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
    raise SystemExit(main())
