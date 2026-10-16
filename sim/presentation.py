"""Load the playtest presentation layer. It does not touch production."""

from __future__ import annotations

import json
import re
from datetime import date
from pathlib import Path

from model.load import DATA_FILES, ROOT

PRESENTATION_FILES = (
    "presentation.json",
    "cosmetics.json",
    "season_pass.json",
)
SLOTS = frozenset({"hat", "cape", "weapon"})
ACTOR_STATES = ("Idle", "Attack", "Celebrate", "Hurt")
_COLOR = re.compile(r"^#[0-9A-Fa-f]{6}$")
_DATE = re.compile(r"^\d{4}-\d{2}-\d{2}$")


def load_bundle(data_dir: Path | None = None) -> dict[str, object]:
    directory = ROOT / "data" if data_dir is None else data_dir
    presentation = _read(directory / "presentation.json")
    cosmetics = _read(directory / "cosmetics.json")
    season = _read(directory / "season_pass.json")
    _validate_presentation(presentation)
    _validate_cosmetics(cosmetics)
    _validate_season(season, cosmetics)
    return {
        "presentation": presentation,
        "cosmetics": cosmetics,
        "season_pass": season,
    }


def presentation_is_outside_the_economy() -> bool:
    return not any(name in DATA_FILES for name in PRESENTATION_FILES)


def _read(path: Path) -> dict:
    if not path.is_file():
        raise FileNotFoundError(f"missing presentation file: {path}")
    with path.open(encoding="utf-8") as handle:
        payload = json.load(handle)
    if not isinstance(payload, dict):
        raise ValueError(f"{path.name} must contain a JSON object")
    if payload.get("schemaVersion") != 1:
        raise ValueError(f"{path.name} schemaVersion must be 1")
    return payload


def _validate_presentation(payload: dict) -> None:
    theme = _object(payload, "theme")
    for key in (
        "background",
        "text",
        "currency",
        "critical",
        "bar",
        "barTrack",
        "button",
        "buttonText",
        "glow",
    ):
        _color(theme, key)
    palette = theme.get("iconPalette")
    if not isinstance(palette, list) or not palette:
        raise ValueError("presentation.json theme.iconPalette must list colors")
    for color in palette:
        if not isinstance(color, str) or _COLOR.fullmatch(color) is None:
            raise ValueError("presentation.json icon color is invalid")
    _positive(theme, "glowDistance")
    glow_alpha = _number(theme, "glowAlpha")
    if not 0 < glow_alpha <= 1:
        raise ValueError("presentation.json glowAlpha must be in (0, 1]")

    type_scale = _object(payload, "type")
    if _number(type_scale, "currencySize") < 48:
        raise ValueError("presentation.json currencySize must be at least 48")
    if _number(type_scale, "generatorSize") < 24:
        raise ValueError("presentation.json generatorSize must be at least 24")
    for key in ("bodySize", "currencyMinHeight", "bodyMinHeight", "buttonMinHeight", "classButton", "barHeight"):
        _positive(type_scale, key)

    layout = _object(payload, "layout")
    for key in ("referenceWidth", "referenceHeight", "padding", "spacing"):
        _positive(layout, key)
    stage = _number(layout, "stageFraction")
    if not 0 < stage < 1:
        raise ValueError("presentation.json stageFraction must be between 0 and 1")
    match = _number(layout, "matchWidthOrHeight")
    if not 0 <= match <= 1:
        raise ValueError("presentation.json matchWidthOrHeight must be from 0 to 1")
    bottom = _number(layout, "bottomAnchor")
    middle = _number(layout, "middleAnchor")
    top = _number(layout, "topAnchor")
    if bottom != 0 or not bottom < middle < top < 1:
        raise ValueError(
            "presentation.json layout anchors must be bottom 0, then middle, then top, below 1"
        )
    row_height = _number(layout, "generatorRowHeight")
    if not 60 <= row_height <= 80:
        raise ValueError("presentation.json generatorRowHeight must sit between 60 and 80")
    split = _number(layout, "actorSplit")
    if not 0 < split < 1:
        raise ValueError("presentation.json actorSplit must be between 0 and 1")
    hero_scale = _number(layout, "heroScale")
    if not 0 < hero_scale < 1:
        raise ValueError("presentation.json heroScale must be between 0 and 1")

    feedback = _object(payload, "feedback")
    for key in (
        "floatSeconds",
        "floatRise",
        "shakeSeconds",
        "shakePixels",
        "shakeFrequency",
        "cameraUnitsPerPixel",
        "confettiSeconds",
        "confettiSpeed",
        "confettiSize",
        "confettiRadius",
    ):
        _positive(feedback, key)
    count = _number(feedback, "confettiCount")
    if count < 1 or count != int(count):
        raise ValueError("presentation.json confettiCount must be a positive integer")
    share = _number(feedback, "largePurchaseCostShare")
    if not 0 < share <= 1:
        raise ValueError("presentation.json largePurchaseCostShare must be in (0, 1]")

    audio = _object(payload, "audio")
    for key in ("idleTrack", "bossTrack", "clickSfx", "upgradeSfx", "critSfx"):
        _audio_id(audio, key)
    extensions = audio.get("extensions")
    if extensions != [".wav", ".mp3"]:
        raise ValueError("presentation.json audio.extensions must be .wav and .mp3")
    ratio = _number(audio, "bossApproachRatio")
    if not 0 < ratio <= 1:
        raise ValueError("presentation.json bossApproachRatio must be in (0, 1]")
    placeholder = _object(audio, "placeholder")
    for key in (
        "idleHz",
        "bossHz",
        "clickHz",
        "upgradeHz",
        "critHz",
        "loopSeconds",
        "sfxSeconds",
        "sampleRate",
        "amplitude",
    ):
        _positive(placeholder, key)

    actors = _object(payload, "actors")
    resource = actors.get("animatorResource")
    if resource != "Animators/PixelActor":
        raise ValueError("presentation.json animatorResource must be Animators/PixelActor")
    _positive(actors, "pixelsPerUnit")
    _positive(actors, "cueSeconds")
    if actors.get("idleState") != "Idle":
        raise ValueError("presentation.json idleState must be Idle")
    if tuple(actors.get("states") or ()) != ACTOR_STATES:
        raise ValueError("presentation.json states must be Idle, Attack, Celebrate, Hurt")
    for key in (
        "purchaseHero",
        "purchaseEnemy",
        "prestigeHero",
        "prestigeEnemy",
        "milestoneHero",
        "milestoneEnemy",
    ):
        if actors.get(key) not in ACTOR_STATES:
            raise ValueError(f"presentation.json actors.{key} is not a known state")
    _actor(actors.get("hero"), "hero")
    enemies = actors.get("enemies")
    if not isinstance(enemies, list) or not enemies:
        raise ValueError("presentation.json actors.enemies must list enemies")
    seen: set[str] = set()
    for enemy in enemies:
        _actor(enemy, "enemy")
        if enemy["id"] in seen:
            raise ValueError("presentation.json duplicate enemy id " + enemy["id"])
        seen.add(enemy["id"])
    poses = actors.get("poses")
    if not isinstance(poses, dict) or set(poses) != set(ACTOR_STATES):
        raise ValueError("presentation.json poses must cover every actor state")
    for state, scale in poses.items():
        if isinstance(scale, bool) or not isinstance(scale, (int, float)) or scale <= 0:
            raise ValueError(f"presentation.json pose {state} scale must be positive")

    _validate_art(payload.get("art"), actors)

    copy = _object(payload, "copy")
    for key in ("time", "contract", "prestige", "relic", "download", "dip", "peak", "premium", "season", "shop"):
        text = copy.get(key)
        if not isinstance(text, str) or not text:
            raise ValueError(f"presentation.json copy.{key} must be text")


def _validate_cosmetics(payload: dict) -> None:
    slots = payload.get("slots")
    if slots != ["hat", "cape", "weapon"]:
        raise ValueError("cosmetics.json slots must be hat, cape, weapon")
    items = payload.get("items")
    if not isinstance(items, list) or not items:
        raise ValueError("cosmetics.json items must be a non-empty list")
    seen: set[str] = set()
    for item in items:
        if not isinstance(item, dict):
            raise ValueError("cosmetics.json item must be an object")
        item_id = item.get("id")
        if not isinstance(item_id, str) or not item_id or item_id in seen:
            raise ValueError("cosmetics.json item id must be unique")
        seen.add(item_id)
        name = item.get("name")
        if not isinstance(name, str) or not name:
            raise ValueError("cosmetics.json item name is missing")
        if item.get("slot") not in SLOTS:
            raise ValueError("cosmetics.json slot must be hat, cape, or weapon")
        sprite = item.get("sprite_path")
        if not isinstance(sprite, str) or not sprite.startswith("Sprites/") or not sprite.endswith(".png"):
            raise ValueError("cosmetics.json sprite_path must be a Sprites PNG")
        condition = item.get("unlock_condition")
        if not isinstance(condition, str) or not condition:
            raise ValueError("cosmetics.json unlock_condition must be text")
        if not isinstance(item.get("is_premium"), bool):
            raise ValueError("cosmetics.json is_premium must be a boolean")


def _validate_season(payload: dict, cosmetics: dict) -> None:
    season_id = payload.get("season_id")
    if not isinstance(season_id, str) or not season_id:
        raise ValueError("season_pass.json season_id is missing")
    start = _date(payload.get("start_date"), "start_date")
    end = _date(payload.get("end_date"), "end_date")
    if end < start:
        raise ValueError("season_pass.json end_date is before start_date")
    rewards = payload.get("rewards")
    if not isinstance(rewards, list) or not rewards:
        raise ValueError("season_pass.json rewards must list cosmetic ids")
    known = {item["id"] for item in cosmetics["items"]}
    for reward in rewards:
        if reward not in known:
            raise ValueError("season_pass.json reward is not a cosmetic id")
    tier = payload.get("price_tier")
    if not isinstance(tier, str) or not tier:
        raise ValueError("season_pass.json price_tier must be text")


def _validate_art(art: object, actors: dict) -> None:
    if not isinstance(art, dict):
        raise ValueError("presentation.json art must be an object")
    for key in ("stageBackground", "currencyIcon", "insightIcon", "prestigeIcon"):
        _sprite_path(art.get(key), f"art.{key}")
    pref = art.get("classPrefKey")
    if not isinstance(pref, str) or not pref:
        raise ValueError("presentation.json art.classPrefKey must be text")
    classes = art.get("classes")
    if not isinstance(classes, list) or not classes:
        raise ValueError("presentation.json art.classes must list the playable classes")
    ids: set[str] = set()
    for item in classes:
        if not isinstance(item, dict):
            raise ValueError("presentation.json class must be an object")
        class_id = item.get("id")
        name = item.get("name")
        if not isinstance(class_id, str) or not class_id or class_id in ids:
            raise ValueError("presentation.json class id must be unique")
        ids.add(class_id)
        if not isinstance(name, str) or not name:
            raise ValueError("presentation.json class name is missing")
        _sprite_path(item.get("sprite_path"), "class.sprite_path")
    default = art.get("defaultClass")
    if default not in ids:
        raise ValueError("presentation.json art.defaultClass is not a class id")
    hero_path = actors.get("hero", {}).get("sprite_path")
    if hero_path != next(item["sprite_path"] for item in classes if item["id"] == default):
        raise ValueError("presentation.json hero sprite_path must match the default class")


def _sprite_path(value: object, label: str) -> None:
    if not isinstance(value, str) or not value.startswith("Sprites/") or not value.endswith(".png"):
        raise ValueError(f"presentation.json {label} must be a Sprites PNG")


def _actor(value: object, label: str) -> None:
    if not isinstance(value, dict):
        raise ValueError(f"presentation.json {label} must be an object")
    if not isinstance(value.get("id"), str) or not value["id"]:
        raise ValueError(f"presentation.json {label} id is missing")
    _color(value, "color")
    for key in ("width", "height"):
        size = value.get(key)
        if isinstance(size, bool) or not isinstance(size, int) or size < 1:
            raise ValueError(f"presentation.json {label}.{key} must be a positive integer")
    for key in ("x", "y"):
        coordinate = value.get(key)
        if isinstance(coordinate, bool) or not isinstance(coordinate, (int, float)):
            raise ValueError(f"presentation.json {label}.{key} must be a number")
    sprite = value.get("sprite_path")
    if sprite is None:
        return
    if not isinstance(sprite, str) or not sprite.startswith("Sprites/") or not sprite.endswith(".png"):
        raise ValueError(f"presentation.json {label}.sprite_path must be a Sprites PNG")


def _object(payload: dict, key: str) -> dict:
    value = payload.get(key)
    if not isinstance(value, dict):
        raise ValueError(f"presentation.json {key} must be an object")
    return value


def _color(payload: dict, key: str) -> None:
    value = payload.get(key)
    if not isinstance(value, str) or _COLOR.fullmatch(value) is None:
        raise ValueError(f"presentation.json {key} must be a #RRGGBB color")


def _audio_id(payload: dict, key: str) -> None:
    value = payload.get(key)
    if not isinstance(value, str) or not value.startswith("Audio/"):
        raise ValueError(f"presentation.json {key} must be an Audio/ path")


def _positive(payload: dict, key: str) -> None:
    if _number(payload, key) <= 0:
        raise ValueError(f"presentation.json {key} must be positive")


def _number(payload: dict, key: str) -> float:
    value = payload.get(key)
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ValueError(f"presentation.json {key} must be a number")
    return float(value)


def _date(value: object, key: str) -> date:
    if not isinstance(value, str) or _DATE.fullmatch(value) is None:
        raise ValueError(f"season_pass.json {key} must be YYYY-MM-DD")
    year, month, day = (int(part) for part in value.split("-"))
    try:
        return date(year, month, day)
    except ValueError as error:
        raise ValueError(f"season_pass.json {key} is not a calendar date") from error
