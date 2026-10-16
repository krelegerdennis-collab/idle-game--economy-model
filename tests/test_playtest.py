"""Playtest logs against the frozen baseline. No cohort is invented."""

from __future__ import annotations

import json
from pathlib import Path

import pytest

from model.load import ROOT
from sim.analyze_playtest import analyze_events, analyze_path, format_report, load_jsonl
from sim.compare_playtest_to_sim import (
    compare_reports,
    empty_playtest,
    format_comparison,
    main as compare_main,
    simulation_reference,
)

BASELINE_PATH = ROOT / "exports" / "runs" / "baseline.json"
EXPORT = ROOT / "exports" / "unity" / "economy_config.json"
BUNDLED = ROOT / "unity" / "Playtest" / "Assets" / "StreamingAssets" / "economy_config.json"
CS_ROOT = ROOT / "unity" / "Playtest" / "Assets"

FORBIDDEN_LITERALS = (
    "0.0976",
    "25000",
    "24000000",
    "1229.28",
    "15680",
    "13500000",
    "0.117",
    "300000",
    "1.074",
    "70.24",
    "163.92",
    "1.15",
    "0.571429",
)

EVENTS = (
    "session_begin",
    "session_end",
    "upgrade_purchase",
    "contract_offer_shown",
    "contract_purchase",
    "contract_declined",
    "prestige_trigger",
    "relic_purchase",
    "offline_earning_collected",
)


def _event(name: str, **fields: object) -> dict:
    payload = {"event": name, "player_id": "player"}
    payload.update(fields)
    return payload


def test_streaming_config_matches_the_export():
    assert BUNDLED.read_bytes() == EXPORT.read_bytes()


def test_client_has_no_hardcoded_balance_literals():
    sources = list(CS_ROOT.rglob("*.cs"))
    assert sources
    blob = "\n".join(path.read_text(encoding="utf-8") for path in sources)
    for literal in FORBIDDEN_LITERALS:
        assert literal not in blob
    bridge = (CS_ROOT / "AnalyticsBridge.cs").read_text(encoding="utf-8")
    for name in EVENTS:
        assert name in bridge
    export = (CS_ROOT / "ExportLogs.cs").read_text(encoding="utf-8")
    assert "/sdcard/Android/data/" in export
    assert "Application.persistentDataPath" in export
    assert "Application.identifier" in export
    assert "WebGLPlayer" in export
    download = (CS_ROOT / "WebGLDownload.cs").read_text(encoding="utf-8")
    assert "PlaytestDownload" in download
    assert "ExternalEval" not in (CS_ROOT / "ExportLogs.cs").read_text(encoding="utf-8")


def test_player_settings_enable_both_input_backends():
    settings = (ROOT / "unity" / "Playtest" / "ProjectSettings" / "ProjectSettings.asset").read_text(encoding="utf-8")
    assert "activeInputHandler: 2" in settings


def test_simulation_reference_reads_the_frozen_baseline():
    baseline = json.loads(BASELINE_PATH.read_text(encoding="utf-8"))
    metrics = simulation_reference(baseline)
    times = [choice["active_minutes"] for choice in baseline["contract_choices"] if choice.get("bought")]
    assert metrics["take_rate"] == pytest.approx(baseline["contract_take_rate"])
    assert metrics["contract_window_minutes"] == pytest.approx(max(times) - min(times))
    assert metrics["contract_window_minutes"] == pytest.approx(3.560634)
    assert metrics["d1"] == pytest.approx(baseline["retention"]["d1"])
    assert metrics["d30"] == pytest.approx(baseline["retention"]["d30"])


def test_playtest_cohort_answers_the_six_questions(tmp_path: Path):
    lines = [
        _event("session_begin", timestamp="2026-01-01T10:00:00+00:00", cumulative_playtime=0, player_id="a"),
        _event(
            "contract_offer_shown",
            timestamp="2026-01-01T10:00:01+00:00",
            owned_at_offer=0,
            active_playtime=0,
            player_id="a",
        ),
        _event(
            "upgrade_purchase",
            timestamp="2026-01-01T10:05:00+00:00",
            generator_type="generator_1",
            player_id="a",
        ),
        _event(
            "upgrade_purchase",
            timestamp="2026-01-01T10:10:00+00:00",
            generator_type="generator_2",
            player_id="a",
        ),
        _event(
            "contract_purchase",
            timestamp="2026-01-01T10:20:00+00:00",
            owned_at_purchase=54,
            time_since_offer_shown=1200,
            active_playtime=1200,
            player_id="a",
        ),
        _event(
            "prestige_trigger",
            timestamp="2026-01-01T12:00:00+00:00",
            lifetime_currency=24000000,
            points_granted=1,
            multiplier_after=1.6,
            player_id="a",
        ),
        _event(
            "relic_purchase",
            timestamp="2026-01-01T12:01:00+00:00",
            relic_id="offline_ledger",
            insight_cost=1,
            insight_after=4,
            player_id="a",
        ),
        _event("session_begin", timestamp="2026-01-01T11:00:00+00:00", cumulative_playtime=0, player_id="b"),
        _event(
            "contract_offer_shown",
            timestamp="2026-01-01T11:00:01+00:00",
            owned_at_offer=0,
            active_playtime=30,
            player_id="b",
        ),
        _event(
            "upgrade_purchase",
            timestamp="2026-01-01T11:02:00+00:00",
            generator_type="generator_2",
            player_id="b",
        ),
        _event(
            "upgrade_purchase",
            timestamp="2026-01-01T11:03:00+00:00",
            generator_type="generator_1",
            player_id="b",
        ),
        _event(
            "contract_declined",
            timestamp="2026-01-01T11:30:00+00:00",
            owned_at_decline=55,
            reason_if_known="offer_closed",
            player_id="b",
        ),
        _event("session_begin", timestamp="2026-01-02T11:00:00+00:00", cumulative_playtime=1800, player_id="b"),
        "not json",
    ]
    path = tmp_path / "playtest_log.jsonl"
    path.write_text("\n".join(json.dumps(line) if isinstance(line, dict) else line for line in lines) + "\n", encoding="utf-8")
    loaded, skipped = load_jsonl(path)
    assert skipped == 1
    assert len(loaded) == 13
    report = analyze_path(path)
    assert report["session_players"] == 2
    assert report["medium"] == "device"
    assert report["take_rate"] == pytest.approx(0.5)
    assert report["contract_declined_ratio"] == pytest.approx(0.5)
    assert report["contract_purchased"]["players"] == 1
    assert report["contract_purchased"]["moments"][0]["active_minutes"] == pytest.approx(20)
    assert report["contract_seen_not_purchased"]["players"] == 1
    assert report["contract_seen_not_purchased"]["moments"][0]["reason_if_known"] == "offer_closed"
    assert report["prestige"]["players"] == 1
    assert report["prestige"]["events"] == 1
    assert report["relics"]["by_id"] == {"offline_ledger": 1}
    orders = {tuple(item["order"]): item["players"] for item in report["purchase_orders"]}
    assert orders[("generator_1", "generator_2")] == 1
    assert orders[("generator_2", "generator_1")] == 1
    assert report["retention"]["d1"] == pytest.approx(0.5)
    assert report["retention"]["d1_eligible_players"] == 2
    assert report["retention"]["d7"] is None
    assert report["retention"]["d30"] is None
    assert "contract gekocht: 1" in format_report(report)

    baseline = json.loads(BASELINE_PATH.read_text(encoding="utf-8"))
    compared = compare_reports(report, baseline)
    assert compared["take_rate"]["difference"] == pytest.approx(0.5 - baseline["contract_take_rate"])
    assert compared["contract_window_minutes"]["playtest"] == pytest.approx(20)
    assert compared["contract_window_minutes"]["difference"] == pytest.approx(
        20 - simulation_reference(baseline)["contract_window_minutes"]
    )
    assert compared["d1"]["difference"] == pytest.approx(0.5 - baseline["retention"]["d1"])
    assert compared["d30"]["playtest"] is None
    assert compared["d30"]["difference"] is None


def test_empty_cohort_does_not_invent_rates():
    report = empty_playtest()
    assert report["take_rate"] is None
    assert report["retention"]["d1"] is None
    assert report["retention"]["d30"] is None
    baseline = json.loads(BASELINE_PATH.read_text(encoding="utf-8"))
    compared = compare_reports(report, baseline)
    assert compared["take_rate"]["simulation"] == pytest.approx(baseline["contract_take_rate"])
    assert compared["take_rate"]["playtest"] is None
    assert compared["take_rate"]["difference"] is None
    text = format_comparison(compared)
    assert "playtest —" in text
    assert "D30:" in text


def test_webgl_logs_skip_retention(tmp_path: Path, capsys: pytest.CaptureFixture[str]):
    path = tmp_path / "web.jsonl"
    lines = [
        _event(
            "session_begin",
            timestamp="2026-01-01T00:00:00+00:00",
            player_id="w",
            medium="webgl",
            cumulative_playtime=0,
        ),
        _event(
            "contract_offer_shown",
            timestamp="2026-01-01T00:00:01+00:00",
            player_id="w",
            owned_at_offer=0,
        ),
        _event(
            "contract_declined",
            timestamp="2026-01-01T00:10:00+00:00",
            player_id="w",
            reason_if_known="session_end",
        ),
    ]
    path.write_text("\n".join(json.dumps(line) for line in lines) + "\n", encoding="utf-8")
    assert compare_main(["--logs", str(path)]) == 0
    text = capsys.readouterr().out
    assert "niet valide voor WebGL" in text
    assert "contract_declined-ratio: 1" in text
    assert "D1: simulatie" not in text
    assert "D30: simulatie" not in text


def test_compare_cli_with_no_logs_exits_clean(capsys: pytest.CaptureFixture[str]):
    assert compare_main(["--logs", "missing-playtest-logs"]) == 0
    captured = capsys.readouterr().out
    assert "Take rate:" in captured
    assert "playtest —" in captured


def test_directory_of_logs_merges_one_player(tmp_path: Path):
    first = tmp_path / "a.jsonl"
    second = tmp_path / "b.jsonl"
    first.write_text(
        json.dumps(_event("session_begin", timestamp="2026-03-01T00:00:00+00:00", player_id="same")) + "\n",
        encoding="utf-8",
    )
    second.write_text(
        json.dumps(_event("session_begin", timestamp="2026-03-08T00:00:00+00:00", player_id="same")) + "\n",
        encoding="utf-8",
    )
    report = analyze_path(tmp_path)
    assert report["player_count"] == 1
    assert report["retention"]["d7"] == pytest.approx(1.0)
    assert report["retention"]["d1"] == pytest.approx(1.0)


def test_analyze_events_without_files_has_no_players():
    report = analyze_events({})
    assert report["player_count"] == 0
    assert report["contract_not_shown"]["players"] == 0
