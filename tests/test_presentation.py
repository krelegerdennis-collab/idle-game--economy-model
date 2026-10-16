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
    layout = bundle["presentation"]["layout"]
    assert layout["referenceWidth"] == 1080
    assert layout["referenceHeight"] == 1920
    assert layout["matchWidthOrHeight"] == 0.5
    assert layout["topAnchor"] == 0.9
    assert layout["middleAnchor"] == 0.4
    assert layout["bottomAnchor"] == 0.0
    assert layout["actorSplit"] == 0.5
    assert layout["heroScale"] == 0.85
    assert layout["generatorRowHeight"] == 72
    assert 60 <= layout["generatorRowHeight"] <= 80
    assert bundle["presentation"]["copy"]["shop"] == "Shop"
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


def test_class_sprites_are_transparent_pngs():
    bundle = load_bundle()
    classes = bundle["presentation"]["art"]["classes"]
    assert [item["id"] for item in classes] == ["swordsman", "mage", "summoner", "archer", "rogue"]
    assert bundle["presentation"]["art"]["defaultClass"] == "swordsman"
    paths = [item["sprite_path"] for item in classes]
    paths.extend(
        enemy["sprite_path"] for enemy in bundle["presentation"]["actors"]["enemies"]
    )
    paths.append(bundle["presentation"]["art"]["stageBackground"])
    for sprite_path in paths:
        art = ROOT / "unity" / "Playtest" / "Assets" / sprite_path
        streaming = STREAMING / sprite_path
        assert art.read_bytes() == streaming.read_bytes()
        payload = art.read_bytes()
        assert payload.startswith(b"\x89PNG\r\n\x1a\n")
        if "/Backgrounds/" not in sprite_path:
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
    stride = width * 4
    previous = bytearray(stride)
    cursor = 0
    corner = None
    for _ in range(height):
        filter_type = rows[cursor]
        data = bytearray(rows[cursor + 1:cursor + 1 + stride])
        for index in range(stride):
            left = data[index - 4] if index >= 4 else 0
            up = previous[index]
            up_left = previous[index - 4] if index >= 4 else 0
            if filter_type == 1:
                data[index] = (data[index] + left) & 255
            elif filter_type == 2:
                data[index] = (data[index] + up) & 255
            elif filter_type == 3:
                data[index] = (data[index] + ((left + up) // 2)) & 255
            elif filter_type == 4:
                data[index] = (data[index] + _paeth(left, up, up_left)) & 255
            elif filter_type != 0:
                raise AssertionError("png filter " + str(filter_type))
        if corner is None:
            corner = bytes(data[:4])
        previous = data
        cursor += 1 + stride
    return corner == b"\x00\x00\x00\x00"


def _paeth(left: int, up: int, up_left: int) -> int:
    estimate = left + up - up_left
    distance_left = abs(estimate - left)
    distance_up = abs(estimate - up)
    distance_up_left = abs(estimate - up_left)
    if distance_left <= distance_up and distance_left <= distance_up_left:
        return left
    if distance_up <= distance_up_left:
        return up
    return up_left


def test_playtest_is_silent_without_clips_and_draws_actors_on_the_canvas():
    assets = ROOT / "unity" / "Playtest" / "Assets"
    sources = {
        path.relative_to(assets).as_posix(): path.read_text(encoding="utf-8")
        for path in assets.rglob("*.cs")
    }
    blob = "\n".join(sources.values())
    assert "AudioClip.Create" not in blob
    assert "OnAudioFilterRead" not in blob
    assert "SpriteRenderer" not in blob
    audio = sources["Scripts/AudioManager.cs"]
    assert "musicClips" in audio
    assert "sfxClips" in audio
    assert "AudioSource" in audio
    assert "Mathf.Sin" not in audio
    character = sources["Scripts/CharacterController.cs"]
    assert "UnityEngine.UI" in character
    assert "FilterMode.Point" in character
    assert "LoadImage" in character
    controller = sources["PlaytestController.cs"]
    assert "audioManager" in controller
    assert "preserveAspect = false" in controller
    assert "row.transform" in controller
    enemy = sources["Scripts/EnemyController.cs"]
    assert "anchorMin" in enemy
    sprites = list((assets / "Sprites").rglob("*.png"))
    assert sprites
    for png in sprites:
        meta = Path(str(png) + ".meta")
        text = meta.read_text(encoding="utf-8")
        assert "TextureImporter:" in text
        assert "filterMode: 0" in text
        assert "textureCompression: 0" in text
    streaming = assets / "StreamingAssets"
    for meta in streaming.rglob("*.png.meta"):
        text = meta.read_text(encoding="utf-8")
        assert "filterMode: 0" in text
        assert "textureCompression: 0" in text


def test_portrait_bands_come_from_presentation_not_literals():
    assets = ROOT / "unity" / "Playtest" / "Assets"
    blob = "\n".join(path.read_text(encoding="utf-8") for path in assets.rglob("*.cs"))
    config = (assets / "Scripts" / "PresentationConfig.cs").read_text(encoding="utf-8")
    controller = (assets / "PlaytestController.cs").read_text(encoding="utf-8")
    for key in (
        "topAnchor",
        "middleAnchor",
        "bottomAnchor",
        "generatorRowHeight",
        "actorSplit",
        "heroScale",
    ):
        assert key in config
    assert "TopAnchor" in controller
    assert "MiddleAnchor" in controller
    assert "BottomAnchor" in controller
    assert "GeneratorRowHeight" in controller
    assert "ScrollRect" in controller
    for literal in ("0.9f", "0.4f", "0.0f", "1080f", "1920f"):
        assert literal not in blob
    settings = (ROOT / "unity" / "Playtest" / "ProjectSettings" / "ProjectSettings.asset").read_text(
        encoding="utf-8"
    )
    assert "defaultScreenOrientation: 0" in settings
    assert "allowedAutorotateToPortrait: 1" in settings
    assert "allowedAutorotateToPortraitUpsideDown: 0" in settings
    assert "allowedAutorotateToLandscapeRight: 0" in settings
    assert "allowedAutorotateToLandscapeLeft: 0" in settings
    assert "activeInputHandler: 2" in settings


def test_layout_anchors_and_row_height_are_required(tmp_path: Path):
    directory = tmp_path / "data"
    directory.mkdir()
    for name in ("presentation.json", "cosmetics.json", "season_pass.json"):
        (directory / name).write_text((DATA / name).read_text(encoding="utf-8"), encoding="utf-8")
    missing = json.loads((directory / "presentation.json").read_text(encoding="utf-8"))
    del missing["layout"]["topAnchor"]
    (directory / "presentation.json").write_text(json.dumps(missing), encoding="utf-8")
    with pytest.raises(ValueError, match="topAnchor"):
        load_bundle(directory)
    tall = json.loads((DATA / "presentation.json").read_text(encoding="utf-8"))
    tall["layout"]["generatorRowHeight"] = 88
    (directory / "presentation.json").write_text(json.dumps(tall), encoding="utf-8")
    with pytest.raises(ValueError, match="generatorRowHeight"):
        load_bundle(directory)


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
