"""Load tunable values from /data. Formulas stay in /model."""

from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path

from model.generators import Generator, Milestone
from model.monetization import MonetizationConfig
from model.offline import OfflineConfig
from model.prestige import PrestigeConfig
from model.retention import RetentionTargets

ROOT = Path(__file__).resolve().parents[1]
DATA_FILES = (
    "generators.json",
    "upgrades.json",
    "prestige.json",
    "offline.json",
    "monetization.json",
    "simulation.json",
    "retention_targets.json",
    "analytics.json",
)


@dataclass(frozen=True)
class SimulationConfig:
    max_active_seconds: float
    max_purchases: int
    sample_every_seconds: float
    primary_policy: str
    noisy_second_best_probability: float
    noisy_runs: int
    noisy_seed: int
    population_weights: dict[str, float]


@dataclass(frozen=True)
class Economy:
    currency_id: str
    starting_currency: float
    active_click_per_second: float
    generators: tuple[Generator, ...]
    milestones: tuple[Milestone, ...]
    prestige: PrestigeConfig
    offline: OfflineConfig
    monetization: MonetizationConfig
    simulation: SimulationConfig
    retention: RetentionTargets
    analytics_events: tuple[dict[str, object], ...]
    data_dir: Path


def _read_json(data_dir: Path, name: str) -> dict:
    path = data_dir / name
    if not path.is_file():
        raise FileNotFoundError(f"missing economy data file: {path}")
    with path.open(encoding="utf-8") as handle:
        payload = json.load(handle)
    if not isinstance(payload, dict):
        raise ValueError(f"{path} must contain a JSON object")
    return payload


def load_files(data_dir: Path) -> dict[str, dict]:
    return {name: _read_json(data_dir, name) for name in DATA_FILES}


def parse_economy(files: dict[str, dict], data_dir: Path) -> Economy:
    generators_file = files["generators.json"]
    upgrades_file = files["upgrades.json"]
    prestige_file = files["prestige.json"]
    offline_file = files["offline.json"]
    monetization_file = files["monetization.json"]
    simulation_file = files["simulation.json"]
    retention_file = files["retention_targets.json"]
    analytics_file = files["analytics.json"]

    generators: list[Generator] = []
    for index, raw in enumerate(generators_file["generators"], start=1):
        tier = int(raw["tier"])
        if tier != index:
            raise ValueError(f"generator tiers must be 1..n in list order, found {tier} at position {index}")
        generator = Generator(
            id=str(raw["id"]),
            tier=tier,
            cost_base=float(raw["cost_base"]),
            growth_rate=float(raw["growth_rate"]),
            production_base=float(raw["production_base"]),
        )
        if generator.growth_rate <= 1:
            raise ValueError(f"{generator.id} growth_rate must be > 1")
        if generator.cost_base <= 0 or generator.production_base <= 0:
            raise ValueError(f"{generator.id} cost_base and production_base must be > 0")
        generators.append(generator)
    if len({item.id for item in generators}) != len(generators):
        raise ValueError("generator ids must be unique")

    milestones = tuple(
        Milestone(
            owned_at_least=int(item["owned_at_least"]),
            production_multiplier=float(item["production_multiplier"]),
        )
        for item in upgrades_file["milestones"]
    )
    for milestone in milestones:
        if milestone.owned_at_least < 1:
            raise ValueError("upgrade milestones must require at least 1 owned")
        if milestone.production_multiplier <= 0:
            raise ValueError("upgrade production_multiplier must be > 0")

    exponent = float(prestige_file["exponent"])
    if not 0 < exponent < 1:
        raise ValueError("prestige exponent must satisfy 0 < exponent < 1")
    point_value = float(prestige_file["point_value"])
    if point_value < 0:
        raise ValueError("point_value must be >= 0")
    prestige = PrestigeConfig(
        coeff=float(prestige_file["coeff"]),
        divisor=float(prestige_file["divisor"]),
        exponent=exponent,
        point_value=point_value,
        min_points_to_prestige=int(prestige_file["min_points_to_prestige"]),
        stall_seconds=float(prestige_file["stall_seconds"]),
    )
    if prestige.divisor <= 0 or prestige.coeff <= 0:
        raise ValueError("prestige coeff and divisor must be > 0")
    if prestige.min_points_to_prestige < 0:
        raise ValueError("min_points_to_prestige must be >= 0")
    if prestige.stall_seconds <= 0:
        raise ValueError("stall_seconds must be > 0")

    offline_rate = float(offline_file["offline_rate"])
    if offline_rate < 0 or offline_rate > 0.5:
        raise ValueError("offline_rate must be between 0 and 0.5 inclusive")
    offline = OfflineConfig(
        offline_rate=offline_rate,
        cap_hours=float(offline_file["cap_hours"]),
        waking_hours=float(offline_file["waking_hours"]),
    )
    if offline.cap_hours <= 0 or offline.waking_hours <= 0:
        raise ValueError("offline cap_hours and waking_hours must be > 0")

    default_action = str(monetization_file["default_action"])
    if default_action != "decline_offer":
        raise ValueError("monetization default_action must be decline_offer; the free path is the model")
    monetization = MonetizationConfig(
        every_nth_prestige=int(monetization_file["every_nth_prestige"]),
        optional_boost_multiplier=float(monetization_file["optional_boost_multiplier"]),
        optional_boost_seconds=float(monetization_file["optional_boost_seconds"]),
        default_action=default_action,
    )
    if monetization.every_nth_prestige < 1:
        raise ValueError("every_nth_prestige must be >= 1")

    weights = {str(key): float(value) for key, value in simulation_file["population_weights"].items()}
    if any(value < 0 for value in weights.values()) or sum(weights.values()) <= 0:
        raise ValueError("population_weights must be non-negative and sum to more than 0")
    simulation = SimulationConfig(
        max_active_seconds=float(simulation_file["max_active_seconds"]),
        max_purchases=int(simulation_file["max_purchases"]),
        sample_every_seconds=float(simulation_file["sample_every_seconds"]),
        primary_policy=str(simulation_file["primary_policy"]),
        noisy_second_best_probability=float(simulation_file["noisy_second_best_probability"]),
        noisy_runs=int(simulation_file["noisy_runs"]),
        noisy_seed=int(simulation_file["noisy_seed"]),
        population_weights=weights,
    )

    projection = {str(key): float(value) for key, value in retention_file["projection"].items()}
    retention = RetentionTargets(
        d1_min=float(retention_file["d1_min"]),
        d7_min=float(retention_file["d7_min"]),
        d30_min=float(retention_file["d30_min"]),
        session_cadence_min=float(retention_file["session_cadence_min"]),
        session_cadence_max=float(retention_file["session_cadence_max"]),
        first_prestige_wall_minutes_min=float(retention_file["first_prestige_wall_minutes_min"]),
        first_prestige_wall_minutes_target=float(retention_file["first_prestige_wall_minutes_target"]),
        first_prestige_wall_minutes_max=float(retention_file["first_prestige_wall_minutes_max"]),
        purchase_order_agreement_min=float(retention_file["purchase_order_agreement_min"]),
        min_tiers_unlocked=int(retention_file["min_tiers_unlocked"]),
        projection=projection,
    )

    events = tuple(analytics_file["events"])
    names = [str(event["name"]) for event in events]
    required = {"session_begin", "upgrade_purchase", "prestige_trigger"}
    missing = required.difference(names)
    if missing:
        raise ValueError(f"analytics events missing: {sorted(missing)}")

    return Economy(
        currency_id=str(generators_file["currency_id"]),
        starting_currency=float(generators_file["starting_currency"]),
        active_click_per_second=float(generators_file["active_click_per_second"]),
        generators=tuple(generators),
        milestones=milestones,
        prestige=prestige,
        offline=offline,
        monetization=monetization,
        simulation=simulation,
        retention=retention,
        analytics_events=events,
        data_dir=data_dir,
    )


def load_economy(data_dir: Path | None = None) -> Economy:
    directory = Path(data_dir) if data_dir is not None else ROOT / "data"
    return parse_economy(load_files(directory), directory)
