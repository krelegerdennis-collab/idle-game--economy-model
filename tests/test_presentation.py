"""Presentation files stay out of the frozen economy curve."""

from __future__ import annotations

import json
from pathlib import Path

import pytest

from model.load import DATA_FILES, ROOT
from sim.presentation import load_bundle, presentation_is_outside_the_economy
from sim.run_simulation import data_fingerprint

DATA = ROOT / "data"
STREAMING = ROOT / "unity" / "Playtest" / "Assets" / "StreamingAssets"
FROZEN_FINGERPRINT = "d424c23490c0"


def test_presentation_bundle_matches_the_schema():
    bundle = load_bundle()
    theme = bundle["presentation"]["theme"]
    assert theme["background"] == "#0A0A1A"
    assert theme["text"] == "#00FFFF"
    assert theme["currency"] == "#FFD700"
    assert theme["critical"] == "#FF69B4"
    assert bundle["presentation"]["type"]["currencySize"] >= 48
    assert bundle["presentation"]["type"]["generatorSize"] >= 24
    slots = [item["slot"] for item in bundle["cosmetics"]["items"]]
    assert slots == ["hat", "cape", "weapon"]
    assert any(item["is_premium"] for item in bundle["cosmetics"]["items"])
    assert bundle["season_pass"]["price_tier"] == "cosmetic"
    assert bundle["season_pass"]["rewards"] == ["hat_placeholder", "cape_placeholder"]


def test_presentation_files_do_not_enter_the_economy():
    assert presentation_is_outside_the_economy()
    assert "cosmetics.json" not in DATA_FILES
    assert "season_pass.json" not in DATA_FILES
    assert "presentation.json" not in DATA_FILES
    assert data_fingerprint(DATA) == FROZEN_FINGERPRINT


def test_streaming_presentation_matches_data():
    for name in ("presentation.json", "cosmetics.json", "season_pass.json"):
        assert (STREAMING / name).read_bytes() == (DATA / name).read_bytes()


def test_client_reads_theme_from_data_not_from_literals():
    sources = list((ROOT / "unity" / "Playtest" / "Assets").rglob("*.cs"))
    blob = "\n".join(path.read_text(encoding="utf-8") for path in sources)
    for literal in ("#0A0A1A", "#00FFFF", "#FFD700", "#FF69B4"):
        assert literal not in blob
    assert "ParticleSystem" in blob
    assert "TextMeshPro" in blob
    assert "Audio/Music" not in blob
    assert "hat_placeholder" not in blob


def test_hero_sprite_is_a_transparent_png():
    bundle = load_bundle()
    sprite_path = bundle["presentation"]["actors"]["hero"]["sprite_path"]
    assert sprite_path == "Sprites/Characters/hero.png"
    art = ROOT / "unity" / "Playtest" / "Assets" / sprite_path
    streaming = STREAMING / sprite_path
    assert art.read_bytes() == streaming.read_bytes()
    payload = art.read_bytes()
    assert payload.startswith(b"\x89PNG\r\n\x1a\n")
    assert _png_corner_is_clear(payload)


def _png_corner_is_clear(payload: bytes) -> bool:
    import struct
    import zlib

    pos = 8
    width = height = None
    raw = b""
    while pos < len(payload):
        length = struct.unpack(">I", payload[pos:pos + 4])[0]
        kind = payload[pos + 4:pos + 8]
        chunk = payload[pos + 8:pos + 8 + length]
        pos += 12 + length
        if kind == b"IHDR":
            width, height, bit_depth, color_type = struct.unpack(">IIBB", chunk[:10])
            assert bit_depth == 8 and color_type == 6
        elif kind == b"IDAT":
            raw += chunk
        elif kind == b"IEND":
            break
    rows = zlib.decompress(raw)
    assert rows[0] == 0
    pixel = rows[1:5]
    return pixel == b"\x00\x00\x00\x00"


def test_bad_cosmetic_slot_is_rejected(tmp_path: Path):
    directory = tmp_path / "data"
    directory.mkdir()
    for name in ("presentation.json", "cosmetics.json", "season_pass.json"):
        (directory / name).write_text((DATA / name).read_text(encoding="utf-8"), encoding="utf-8")
    cosmetics = json.loads((directory / "cosmetics.json").read_text(encoding="utf-8"))
    cosmetics["items"][0]["slot"] = "mount"
    (directory / "cosmetics.json").write_text(json.dumps(cosmetics), encoding="utf-8")
    with pytest.raises(ValueError, match="slot"):
        load_bundle(directory)
