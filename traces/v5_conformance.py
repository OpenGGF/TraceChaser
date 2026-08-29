#!/usr/bin/env python3
"""Build and verify the portable, synthetic trace-v5 conformance pack."""

from __future__ import annotations

import base64
import csv
import gzip
import hashlib
import io
import json
import shutil
import tempfile
from contextlib import contextmanager
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from traces.validate_trace_v5 import Validation, reject_duplicate_keys


MANIFEST_FORMAT = "tracechaser-v5-artifact-manifest-v1"
CONTRACT_VERSION = 1
CONSUMER_EXPECTATION_FORMAT = "openggf-trace-v5-consumer-expectation-v1"
PRODUCER_ENTRY = "traces.validate_trace_v5.Validation.run"
LEVEL_CONSUMER = "com.openggf.trace.TraceData.load"
S1_SPECIAL_CONSUMER = (
    "com.openggf.game.sonic1.specialstage.Sonic1SpecialStageTraceData.load"
)
S2_SPECIAL_CONSUMER = "com.openggf.trace.SpecialStageTraceData.load"
S3K_SPECIAL_CONSUMER = (
    "com.openggf.game.sonic3k.specialstage.S3kSpecialStageTraceData.load"
)
RUN_CONSUMER = "com.openggf.trace.TraceRunManifest.load+validate"
CONSUMER_ENTRIES = frozenset({
    LEVEL_CONSUMER, S1_SPECIAL_CONSUMER, S2_SPECIAL_CONSUMER,
    S3K_SPECIAL_CONSUMER, RUN_CONSUMER,
})
FINGERPRINT_A = "sha256:" + "a" * 64
FINGERPRINT_B = "sha256:" + "b" * 64
FINGERPRINT_C = "sha256:" + "c" * 64

LEVEL_HEADER = (
    "frame,input,camera_x,camera_y,rings,gameplay_frame_counter,"
    "vblank_counter,lag_counter,player_present,player_x,player_y,"
    "player_x_speed,player_y_speed,player_g_speed,player_angle,"
    "player_air,player_rolling,player_ground_mode,player_x_sub,"
    "player_y_sub,player_routine,player_status_byte,player_stand_on_obj,"
    "player_animation_id,player_mapping_frame,sidekick_present,"
    "sidekick_x,sidekick_y,sidekick_x_speed,sidekick_y_speed,"
    "sidekick_g_speed,sidekick_angle,sidekick_air,sidekick_rolling,"
    "sidekick_ground_mode,sidekick_x_sub,sidekick_y_sub,sidekick_routine,"
    "sidekick_status_byte,sidekick_stand_on_obj,sidekick_animation_id,"
    "sidekick_mapping_frame"
)
LEVEL_ROW = (
    "0000,0001,0010,0020,0003,0004,0005,0006,"
    "01,fff0,0030,fffe,0002,0003,04,1,0,2,0005,0006,07,08,09,0a,0b,"
    "01,0040,0050,0006,fff9,0008,09,0,1,3,000a,000b,0c,0d,0e,0f,10"
)
S1_SPECIAL_HEADER = (
    "frame,input,lag,x_pos,y_pos,vel_x,vel_y,inertia,status,ss_angle,"
    "ss_rotate,bg_anim,rings,emeralds"
)
S1_SPECIAL_ROW = (
    "10,2a,1,80000001,7ffffffe,fff0,0011,0022,33,44,55,66,0077,08"
)
S2_SPECIAL_HEADER = (
    "frame,input,input_p2,lag,speed_factor,track_anim,track_anim_frame,"
    "track_drawing_index,track_orientation,track_duration_timer,current_segment,"
    "player_anim_frame_timer,rings_togo_bcd,check_rings_flag,tails_control_counter,"
    "swap_positions_flag,sonic_present,sonic_ss_x,sonic_ss_x_sub,sonic_ss_y,"
    "sonic_ss_y_sub,sonic_ss_z,sonic_angle,sonic_routine,sonic_routine_secondary,"
    "sonic_status,sonic_anim,sonic_anim_frame,sonic_rings_bcd,sonic_hurt_timer,"
    "sonic_slide_timer,sonic_flip_timer,tails_present,tails_ss_x,tails_ss_x_sub,"
    "tails_ss_y,tails_ss_y_sub,tails_ss_z,tails_angle,tails_routine,"
    "tails_routine_secondary,tails_status,tails_anim,tails_anim_frame,"
    "tails_rings_bcd,tails_hurt_timer,tails_slide_timer,tails_flip_timer"
)
S2_SPECIAL_ROW = ",".join(
    ["10", "10", "11", "1"]
    + [format(value, "x") for value in range(0x12, 0x2E)]
    + ["0"]
    + [format(value, "x") for value in range(0x2F, 0x3E)]
)
S3K_SPECIAL_HEADER = (
    "frame,input,input_p2,lag,anim_frame,x_pos,y_pos,angle,velocity,turning,"
    "jumping,fade_timer,spheres_left,ring_count,rings_left,rate,rate_timer,"
    "clear_timer,clear_routine,started"
)
S3K_SPECIAL_ROW = ",".join(
    ["10", "10", "11", "0"]
    + [format(value, "x") for value in range(0x12, 0x21)]
    + ["2"]
)


@dataclass(frozen=True)
class Case:
    identifier: str
    rules: tuple[str, ...]
    consumer_entry: str
    files: dict[str, bytes]
    normalized_semantics: dict[str, Any] | None = None
    exact_diagnostic: str | None = None
    consumer_expectation: dict[str, Any] | None = None
    fault: dict[str, Any] | None = None
    materialization: dict[str, str] | None = None

    @property
    def expected_outcome(self) -> str:
        return "accept" if self.exact_diagnostic is None else "reject"


def build_pack(destination: Path) -> None:
    """Write the complete pack into a new or empty directory."""
    if destination.exists() and any(destination.iterdir()):
        raise ValueError(f"destination is not empty: {destination}")
    destination.mkdir(parents=True, exist_ok=True)

    members, cases = _pack_members()
    manifest = _manifest_document(members, cases)
    # The repository/history admission policy intentionally bounds the whole
    # artifact manifest to 64 KiB, so keep this generated ledger canonical and
    # compact while leaving human-facing JSON fixtures indented.
    members["manifest.json"] = _compact_json_bytes(manifest)

    for relative_path, content in sorted(members.items()):
        path = destination / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)


def validate_pack(root: Path) -> list[str]:
    """Verify identity, coverage, parser outcomes, and normalized semantics."""
    errors: list[str] = []
    manifest_path = root / "manifest.json"
    try:
        manifest = json.loads(
            manifest_path.read_text(encoding="utf-8"),
            object_pairs_hook=reject_duplicate_keys,
        )
    except (OSError, UnicodeDecodeError, json.JSONDecodeError, ValueError) as error:
        return [f"manifest.json: cannot load manifest: {error}"]
    errors.extend(_validate_manifest_shape(manifest))
    if errors:
        return errors
    expected_members, expected_cases = _pack_members()
    expected_manifest = _manifest_document(expected_members, expected_cases)
    if manifest != expected_manifest:
        errors.append("manifest.json: manifest does not match generated contract authority")

    listed = {entry["path"]: entry for entry in manifest["files"]}
    actual = {
        path.relative_to(root).as_posix(): path
        for path in root.rglob("*")
        if path.is_file() and path != manifest_path
    }
    for path in sorted(set(actual) - set(listed)):
        errors.append(f"unmanifested file: {path}")
    for path in sorted(set(listed) - set(actual)):
        errors.append(f"manifested file is missing: {path}")
    for relative_path in sorted(set(actual) & set(listed)):
        content = actual[relative_path].read_bytes()
        entry = listed[relative_path]
        if entry["stored_size"] != len(content):
            errors.append(f"{relative_path}: stored size mismatch")
        if entry["stored_sha256"] != _sha256(content):
            errors.append(f"{relative_path}: stored SHA-256 mismatch")
        if relative_path.endswith(".gz"):
            try:
                logical = gzip.decompress(content)
            except OSError as error:
                errors.append(f"{relative_path}: malformed gzip: {error}")
                continue
            if content[3] != 0 or content[4:8] != b"\x00\x00\x00\x00":
                errors.append(f"{relative_path}: gzip header is not deterministic")
            if entry.get("logical_size") != len(logical):
                errors.append(f"{relative_path}: logical size mismatch")
            if entry.get("logical_sha256") != _sha256(logical):
                errors.append(f"{relative_path}: logical SHA-256 mismatch")

    for case in manifest["cases"]:
        with _materialized_case_root(root, case) as case_root:
            actual_diagnostics = _canonical_diagnostics(case_root)
            if case["expected_outcome"] == "accept":
                if actual_diagnostics:
                    errors.append(
                        f"case {case['id']}: expected accept, got {actual_diagnostics!r}"
                    )
                    continue
                try:
                    normalized = _normalize_case(case_root, case["consumer_entry"])
                except (OSError, ValueError, KeyError, json.JSONDecodeError) as error:
                    errors.append(f"case {case['id']}: semantic normalization failed: {error}")
                    continue
                if normalized != case["normalized_semantics"]:
                    errors.append(
                        f"case {case['id']}: normalized semantics mismatch: "
                        f"expected {case['normalized_semantics']!r}, got {normalized!r}"
                    )
            elif actual_diagnostics != [case["exact_diagnostic"]]:
                errors.append(
                    f"case {case['id']}: expected diagnostic {case['exact_diagnostic']!r}, "
                    f"got {actual_diagnostics!r}"
                )
    return errors


def _pack_members() -> tuple[dict[str, bytes], list[Case]]:
    cases = _cases()
    members = {
        "readme.md": _readme_bytes(),
        "manifest.schema.json": _manifest_schema_bytes(),
    }
    for case in cases:
        root = f"fixtures/{case.identifier}"
        for relative_path, content in case.files.items():
            members[f"{root}/{relative_path}"] = content
    return members, cases


def _manifest_document(members: dict[str, bytes], cases: list[Case]) -> dict[str, Any]:
    coverage: dict[str, dict[str, list[str]]] = {}
    for case in cases:
        for rule in case.rules:
            group = coverage.setdefault(rule, {"accept_cases": [], "reject_cases": []})
            group[f"{case.expected_outcome}_cases"].append(case.identifier)
    return {
        "format": MANIFEST_FORMAT,
        "contract_version": CONTRACT_VERSION,
        "consumer_expectation_format": CONSUMER_EXPECTATION_FORMAT,
        "rule_coverage": {key: coverage[key] for key in sorted(coverage)},
        "cases": [_case_entry(case) for case in cases],
        "files": [
            _file_entry(path, content, *_case_for_path(path, cases))
            for path, content in sorted(members.items())
        ],
    }


def _cases() -> list[Case]:
    ordinary_metadata = _metadata("s2", "complete_run")
    ordinary_files = {
        "metadata.json": ordinary_metadata,
        "physics.csv": _csv(LEVEL_HEADER, LEVEL_ROW),
        "aux_state.jsonl": (
            b'{"frame":0,"event":"state_snapshot","tag":"synthetic","value":7}\n'
        ),
        "hardware_timing.jsonl": _timing_bytes(
            _timing(0, "vint_service", "kos_module_queue", 2, FINGERPRINT_A),
            _timing(0, "post_objects", "kos_module_queue", 3, FINGERPRINT_B),
            _timing(0, "pre_main_loop", "kos_decompression_queue", 4, FINGERPRINT_B),
            _timing(0, "pre_main_loop", "nemesis_plc_queue", 5, FINGERPRINT_C),
        ),
    }
    cases = [
        Case("accept-level", (
            "metadata-envelope", "ordinary-42-columns", "aux-jsonl",
            "removed-v5-field", "hardware-kind", "hardware-boundary",
            "hardware-order", "hardware-ordinal", "hardware-frame-bound",
            "hardware-fingerprint"), LEVEL_CONSUMER, ordinary_files,
            _ordinary_semantics(compressed=False)),
        Case("accept-gzip", ("deterministic-gzip",), LEVEL_CONSUMER, {
            "metadata.json": ordinary_metadata,
            "physics.csv.gz": _gzip(_csv(LEVEL_HEADER, LEVEL_ROW)),
            "aux_state.jsonl.gz": _gzip(
                b'{"frame":0,"event":"state_snapshot","tag":"gzip","value":8}\n'),
        }, _gzip_semantics()),
        Case("accept-s1-special", ("s1-special-14-columns",),
             S1_SPECIAL_CONSUMER,
             {"metadata.json": _metadata("s1", "s1_special_stage"),
              "physics.csv": _csv(S1_SPECIAL_HEADER, S1_SPECIAL_ROW)},
             _special_semantics("s1")),
        Case("accept-s2-special", ("s2-special-48-columns",),
             S2_SPECIAL_CONSUMER,
             {"metadata.json": _metadata("s2", "s2_special_stage"),
              "physics.csv": _csv(S2_SPECIAL_HEADER, S2_SPECIAL_ROW)},
             _special_semantics("s2")),
        Case("accept-s3k-special", ("s3k-special-20-columns",),
             S3K_SPECIAL_CONSUMER,
             {"metadata.json": _metadata("s3k", "s3k_special_stage"),
              "physics.csv": _csv(S3K_SPECIAL_HEADER, S3K_SPECIAL_ROW)},
             _special_semantics("s3k")),
        _accepted_run_case(),
    ]
    cases.extend([
        _reject_metadata_case("reject-schema", "trace_schema", 4,
                              "metadata-envelope",
                              "metadata.json: trace_schema must be integer 5",
                              "metadata.json: trace_schema must be integer 5"),
        _reject_width_case("reject-level-width", "ordinary-42-columns", "s2",
                           "complete_run", LEVEL_HEADER, 41, 42, LEVEL_CONSUMER),
        _reject_width_case("reject-s1-special-width", "s1-special-14-columns", "s1",
                           "s1_special_stage", S1_SPECIAL_HEADER, 13, 14,
                           S1_SPECIAL_CONSUMER),
        _reject_width_case("reject-s2-special-width", "s2-special-48-columns", "s2",
                           "s2_special_stage", S2_SPECIAL_HEADER, 47, 48,
                           S2_SPECIAL_CONSUMER),
        _reject_width_case("reject-s3k-special-width", "s3k-special-20-columns", "s3k",
                           "s3k_special_stage", S3K_SPECIAL_HEADER, 19, 20,
                           S3K_SPECIAL_CONSUMER),
        _reject_aux_case(),
        _reject_timing_case("reject-hardware-kind", "hardware-kind",
                            _timing(0, "vint_service", "unknown_queue", 0, FINGERPRINT_A),
                            "line 1 has invalid kind", "kind", "unknown_queue"),
        _reject_timing_case("reject-hardware-boundary", "hardware-boundary",
                            _timing(0, "unknown_boundary", "kos_module_queue", 0,
                                    FINGERPRINT_A),
                            "line 1 has invalid boundary", "boundary", "unknown_boundary"),
        _reject_timing_case("reject-direct-boundary", "hardware-boundary",
                            _timing(0, "post_objects", "kos_decompression_queue", 0,
                                    FINGERPRINT_A),
                            "line 1 direct completion kind requires pre_main_loop boundary",
                            "boundary", "post_objects"),
        _reject_timing_order_case(),
        _reject_timing_ordinal_case(),
        _reject_timing_case("reject-hardware-frame", "hardware-frame-bound",
                            _timing(1, "vint_service", "kos_module_queue", 0,
                                    FINGERPRINT_A),
                            "line 1 raw_frame 1 is outside [0, 1)", "raw_frame", 1,
                            consumer_diagnostic="raw_frame 1 is outside [0, 1)"),
        _reject_timing_case("reject-hardware-fingerprint", "hardware-fingerprint",
                            _timing(0, "vint_service", "kos_module_queue", 0,
                                    "SHA256:" + "a" * 64),
                            "line 1 has invalid submission_fingerprint",
                            "submission_fingerprint", "SHA256:" + "a" * 64),
        _reject_gzip_malformed_case(),
        _reject_gzip_nondeterministic_case(),
        _reject_run_segments_collection_case(),
        _reject_run_transitions_collection_case(),
        _reject_run_gap_collection_case(),
        _reject_run_segment_order_case(),
        _reject_run_transition_order_case(),
    ])
    for field in ("lua_script_version", "csv_version", "ss_csv_version",
                  "hardware_timing_schema", "run_schema"):
        cases.append(_reject_removed_field_case(field))
    return sorted(cases, key=lambda case: case.identifier)


def _accepted_run_case() -> Case:
    fingerprint = "sha256:" + "d" * 64
    segments = [
        {"dir": f"seg{index}", "kind": "level", "trace_profile": "complete_run",
         "bk2_frame_offset": 10 + index * 10, "trace_frame_count": 1,
         "zone_id": 3, "act": index + 1, "special_stage_index": None,
         "bonus_stage_type": None, "dynamic_art_initial_ledger_descriptors": [],
         "dynamic_art_initial_ledger_fingerprint": fingerprint}
        for index in range(3)
    ]
    transitions = [
        {"from_segment": 0, "to_segment": 1, "entry_kind": "level_advance",
         "mode_change_bk2_frame": 19, "special_bonus_entry_flag": 1,
         "saved_x_pos": 256, "saved_y_pos": 512, "last_star_post_hit": 2,
         "rings_before": 30, "rings_after": 4, "emeralds_before": 1,
         "emeralds_after": 2, "gap_admission_runs": [3, 1]},
        {"from_segment": 1, "to_segment": 2, "entry_kind": "death_restart",
         "mode_change_bk2_frame": 29, "special_bonus_entry_flag": 0,
         "saved_x_pos": 768, "saved_y_pos": 1024, "last_star_post_hit": 3,
         "rings_before": 4, "rings_after": 0, "emeralds_before": 2,
         "emeralds_after": 2, "gap_admission_runs": [2, 2]},
    ]
    manifest = {
        "trace_schema": 5, "game": "s2", "run_id": "synthetic-three-segment-run",
        "source_bk2": "synthetic-input.bk2", "rom_checksum": "synthetic-no-rom",
        "recorder": "native-bizhawk-headless", "recorder_version": "3.0",
        "expected_movie_end_mode": "title_screen", "segments": segments,
        "transitions": transitions, "dynamic_art_gap_transitions": [],
    }
    return Case(
        "accept-run-manifest",
        ("run-manifest-collections", "run-manifest-member-order"),
        RUN_CONSUMER, _run_files(manifest, ("seg0", "seg1", "seg2")),
        {"trace_schema": 5, "game": "s2",
         "run_id": "synthetic-three-segment-run", "source_bk2": "synthetic-input.bk2",
         "rom_checksum": "synthetic-no-rom", "expected_movie_end_mode": "title_screen",
         "segments": segments, "transitions": transitions,
         "dynamic_art_gap_transitions": [],
         "member_order": ["seg0", "seg1", "seg2"]},
    )


def _consumer_reject(exception_class: str, message: str,
                     message_match: str = "exact") -> dict[str, Any]:
    return {"outcome": "reject", "diagnostic": {
        "exception_class": exception_class,
        "message_match": message_match,
        "message": message,
    }}


def _reject_metadata_case(identifier: str, field: str, value: Any, rule: str,
                          producer: str, consumer: str) -> Case:
    metadata = json.loads(_metadata("s2", "complete_run"))
    metadata[field] = value
    kind = "removed-field" if rule == "removed-v5-field" else "metadata-value"
    return Case(identifier, (rule,), LEVEL_CONSUMER,
                {"metadata.json": _json_bytes(metadata),
                 "physics.csv": _csv(LEVEL_HEADER, LEVEL_ROW)},
                exact_diagnostic=producer,
                consumer_expectation=_consumer_reject(
                    "java.lang.IllegalArgumentException", consumer),
                fault={"rule": rule, "kind": kind, "field": field, "value": value})


def _reject_removed_field_case(field: str) -> Case:
    return _reject_metadata_case(
        f"reject-removed-{field.replace('_', '-')}", field, 1,
        "removed-v5-field", f"metadata.json: forbidden legacy key {field}",
        f"metadata.json: unsupported legacy field {field}")


def _reject_width_case(identifier: str, rule: str, game: str, profile: str,
                       header: str, bad_width: int, expected_width: int,
                       consumer_entry: str) -> Case:
    row = ",".join(["0"] * bad_width)
    producer = f"physics.csv: row 1 has {bad_width} columns; expected {expected_width}"
    consumer = (f"Trace schema 5 requires 42 or 43 CSV columns, got {bad_width}: {row}"
                if expected_width == 42 else
                f"Expected {expected_width} CSV columns, got {bad_width}: {row}")
    return Case(identifier, (rule,), consumer_entry,
                {"metadata.json": _metadata(game, profile),
                 "physics.csv": _csv(header, row)}, exact_diagnostic=producer,
                consumer_expectation=_consumer_reject(
                    "java.lang.IllegalArgumentException", consumer),
                fault={"rule": rule, "kind": "csv-width",
                       "actual_columns": bad_width, "expected_columns": expected_width})


def _reject_aux_case() -> Case:
    line = '{"event":"state_snapshot","tag":"missing-frame"}'
    return Case(
        "reject-aux-missing-frame", ("aux-jsonl",), LEVEL_CONSUMER,
        {"metadata.json": _metadata("s2", "complete_run"),
         "physics.csv": _csv(LEVEL_HEADER, LEVEL_ROW),
         "aux_state.jsonl": (line + "\n").encode()},
        exact_diagnostic="aux_state.jsonl: line 1 has invalid or missing frame",
        consumer_expectation=_consumer_reject(
            "java.lang.IllegalArgumentException", f"Failed to parse JSONL line: {line}"),
        fault={"rule": "aux-jsonl", "kind": "aux-missing-frame"})


def _reject_timing_case(identifier: str, rule: str, event: dict[str, Any],
                        diagnostic: str, field: str, value: Any, *,
                        consumer_diagnostic: str | None = None) -> Case:
    consumer = consumer_diagnostic or diagnostic
    return Case(
        identifier, (rule,), LEVEL_CONSUMER,
        {"metadata.json": _metadata("s2", "complete_run"),
         "physics.csv": _csv(LEVEL_HEADER, LEVEL_ROW),
         "hardware_timing.jsonl": _timing_bytes(event)},
        exact_diagnostic=f"hardware_timing.jsonl: {diagnostic}",
        consumer_expectation=_consumer_reject(
            "java.io.IOException", f"hardware_timing.jsonl: {consumer}"),
        fault={"rule": rule, "kind": "timing-value", "event_index": 0,
               "field": field, "value": value})


def _reject_timing_order_case() -> Case:
    return Case(
        "reject-hardware-order", ("hardware-order",), LEVEL_CONSUMER,
        {"metadata.json": _metadata("s2", "complete_run"),
         "physics.csv": _csv(LEVEL_HEADER, LEVEL_ROW),
         "hardware_timing.jsonl": _timing_bytes(
             _timing(0, "pre_main_loop", "kos_decompression_queue", 0, FINGERPRINT_A),
             _timing(0, "post_objects", "kos_module_queue", 0, FINGERPRINT_B))},
        exact_diagnostic="hardware_timing.jsonl: line 2 events must use canonical ordering",
        consumer_expectation=_consumer_reject(
            "java.io.IOException", "hardware_timing.jsonl: events must use canonical ordering"),
        fault={"rule": "hardware-order", "kind": "timing-order"})


def _reject_timing_ordinal_case() -> Case:
    return Case(
        "reject-hardware-ordinal", ("hardware-ordinal",), LEVEL_CONSUMER,
        {"metadata.json": _metadata("s2", "complete_run"),
         "physics.csv": _csv(LEVEL_HEADER, LEVEL_ROW),
         "hardware_timing.jsonl": _timing_bytes(
             _timing(0, "vint_service", "kos_module_queue", 2, FINGERPRINT_A),
             _timing(0, "post_objects", "kos_module_queue", 1, FINGERPRINT_B))},
        exact_diagnostic=(
            "hardware_timing.jsonl: line 2 ordinal must increase per kind kos_module_queue"),
        consumer_expectation=_consumer_reject(
            "java.io.IOException",
            "hardware_timing.jsonl: ordinal must increase per kind KOS_MODULE_QUEUE"),
        fault={"rule": "hardware-ordinal", "kind": "timing-ordinal"})


def _encoded_gzip_payload(content: bytes) -> bytes:
    return _json_bytes({"encoding": "base64", "base64": base64.b64encode(content).decode()})


def _reject_gzip_malformed_case() -> Case:
    materialization = {"source": "gzip_payload.json", "target": "physics.csv.gz",
                       "encoding": "base64"}
    return Case(
        "reject-gzip-malformed", ("deterministic-gzip",), LEVEL_CONSUMER,
        {"metadata.json": _metadata("s2", "complete_run"),
         "gzip_payload.json": _encoded_gzip_payload(b"not a gzip stream\n")},
        exact_diagnostic="physics.csv.gz: malformed gzip payload",
        consumer_expectation=_consumer_reject(
            "java.util.zip.ZipException", "Not in GZIP format"),
        fault={"rule": "deterministic-gzip", "kind": "gzip-malformed",
               "source": "gzip_payload.json", "target": "physics.csv.gz"},
        materialization=materialization)


def _reject_gzip_nondeterministic_case() -> Case:
    materialization = {"source": "gzip_payload.json", "target": "physics.csv.gz",
                       "encoding": "base64"}
    semantics = _ordinary_semantics(compressed=False, aux_events=[], timing_events=[])
    return Case(
        "reject-gzip-nondeterministic", ("deterministic-gzip",), LEVEL_CONSUMER,
        {"metadata.json": _metadata("s2", "complete_run"),
         "gzip_payload.json": _encoded_gzip_payload(
             _gzip(_csv(LEVEL_HEADER, LEVEL_ROW), mtime=1))},
        exact_diagnostic="physics.csv.gz: gzip header is not deterministic",
        consumer_expectation={"outcome": "accept", "normalized_semantics": semantics},
        fault={"rule": "deterministic-gzip", "kind": "gzip-nondeterministic",
               "source": "gzip_payload.json", "target": "physics.csv.gz"},
        materialization=materialization)


def _base_run_manifest(run_id: str) -> dict[str, Any]:
    return {"trace_schema": 5, "game": "s2", "run_id": run_id,
            "source_bk2": "synthetic-input.bk2", "rom_checksum": "synthetic-no-rom",
            "recorder": "native-bizhawk-headless", "recorder_version": "3.0",
            "segments": [], "transitions": [], "dynamic_art_gap_transitions": []}


def _segment(name: str, offset: int) -> dict[str, Any]:
    return {"dir": name, "kind": "level", "trace_profile": "complete_run",
            "bk2_frame_offset": offset, "trace_frame_count": 1}


def _reject_run_segments_collection_case() -> Case:
    manifest = _base_run_manifest("empty-segments")
    return Case(
        "reject-run-segments-collections", ("run-manifest-collections",), RUN_CONSUMER,
        _run_files(manifest, ("orphan",)),
        exact_diagnostic="run_manifest.json: segments must contain at least one member",
        consumer_expectation=_consumer_reject(
            "java.lang.IllegalStateException", "Manifest has no segments"),
        fault={"rule": "run-manifest-collections", "kind": "run-collection-empty",
               "field": "segments"})


def _reject_run_transitions_collection_case() -> Case:
    manifest = _base_run_manifest("bad-transitions-shape")
    manifest["segments"] = [_segment("seg0", 10)]
    manifest["transitions"] = {}
    return Case(
        "reject-run-transitions-collections", ("run-manifest-collections",), RUN_CONSUMER,
        _run_files(manifest, ("seg0",)),
        exact_diagnostic="run_manifest.json: transitions must be an array",
        consumer_expectation=_consumer_reject(
            "com.fasterxml.jackson.databind.exc.MismatchedInputException",
            "Cannot deserialize value of type `java.util.ArrayList<com.openggf.trace.TraceRunManifest$Transition>` from Object value",
            "contains"),
        fault={"rule": "run-manifest-collections", "kind": "run-collection-shape",
               "field": "transitions"})


def _reject_run_gap_collection_case() -> Case:
    manifest = _base_run_manifest("bad-gap-shape")
    manifest["segments"] = [_segment("seg0", 10)]
    manifest["dynamic_art_gap_transitions"] = {}
    return Case(
        "reject-run-gap-collections", ("run-manifest-collections",), RUN_CONSUMER,
        _run_files(manifest, ("seg0",)),
        exact_diagnostic="run_manifest.json: dynamic_art_gap_transitions must be an array",
        consumer_expectation=_consumer_reject(
            "java.io.IOException",
            "trace_schema 5 requires dynamic_art_gap_transitions array"),
        fault={"rule": "run-manifest-collections", "kind": "run-collection-shape",
               "field": "dynamic_art_gap_transitions"})


def _reject_run_segment_order_case() -> Case:
    manifest = _base_run_manifest("bad-segment-order")
    manifest["segments"] = [_segment("seg0", 20), _segment("seg1", 10)]
    return Case(
        "reject-run-segment-order", ("run-manifest-member-order",), RUN_CONSUMER,
        _run_files(manifest, ("seg0", "seg1")),
        exact_diagnostic=(
            "run_manifest.json: segment 1 bk2_frame_offset must be strictly increasing"),
        consumer_expectation=_consumer_reject(
            "java.lang.IllegalStateException",
            "Segment 1 bk2_frame_offset 10 is not strictly increasing"),
        fault={"rule": "run-manifest-member-order", "kind": "run-segment-order"})


def _reject_run_transition_order_case() -> Case:
    manifest = _base_run_manifest("bad-transition-order")
    manifest["segments"] = [
        _segment("seg0", 10), _segment("seg1", 20), _segment("seg2", 30)]
    manifest["transitions"] = [
        {"from_segment": 1, "to_segment": 2, "entry_kind": "level_advance",
         "mode_change_bk2_frame": 29},
        {"from_segment": 0, "to_segment": 1, "entry_kind": "level_advance",
         "mode_change_bk2_frame": 19},
    ]
    return Case(
        "reject-run-transition-order", ("run-manifest-member-order",), RUN_CONSUMER,
        _run_files(manifest, ("seg0", "seg1", "seg2")),
        exact_diagnostic=(
            "run_manifest.json: transition 1 from_segment must be strictly increasing"),
        consumer_expectation=_consumer_reject(
            "java.lang.IllegalStateException",
            "Transition 1 does not preserve unique segment adjacency"),
        fault={"rule": "run-manifest-member-order", "kind": "run-transition-order"})


def _run_files(manifest: dict[str, Any], segments: tuple[str, ...]) -> dict[str, bytes]:
    files = {"run_manifest.json": _json_bytes(manifest)}
    for segment in segments:
        files[f"{segment}/metadata.json"] = _metadata("s2", "complete_run")
        files[f"{segment}/physics.csv"] = _csv(LEVEL_HEADER, LEVEL_ROW)
    return files


def _metadata(game: str, profile: str, frame_count: int = 1) -> bytes:
    return _json_bytes({
        "game": game, "zone": "synthetic-zone", "zone_id": 3, "act": 2,
        "bk2_frame_offset": 7, "ring_floor_check_counter_phase": 6,
        "trace_frame_count": frame_count,
        "start_x": "0x8001", "start_y": "0x7ffe",
        "recording_date": "2000-01-02T03:04:05Z",
        "recorder": "native-bizhawk-headless", "recorder_version": "3.0",
        "trace_profile": profile, "trace_schema": 5,
        "bizhawk_version": "2.11", "genesis_core": "GPGX",
        "aux_schema_extras": ["synthetic_contract_extension"], "rom_zone_id": 131,
        "route": "synthetic-route", "source_bk2": "synthetic-input.bk2",
        "rom_checksum": "synthetic-no-rom", "notes": "synthetic contract only",
        "characters": ["sonic", "tails"], "main_character": "sonic",
        "sidekicks": ["tails"], "pre_trace_osc_frames": 9,
        "rng_seed": "0x80000001", "trace_type": "level",
        "input_source": "bk2", "credits_demo_index": 4,
        "credits_demo_slug": "synthetic-credits", "special_stage_index": 5,
        "run_id": "synthetic-run", "segment_index": 6,
        "bonus_stage_type": "slot_machine", "fresh_load": True,
        "v_int_run_count": 254,
    })


def _timing(raw_frame: int, boundary: str, kind: str, ordinal: int,
            fingerprint: str) -> dict[str, Any]:
    return {
        "event": "hardware_work_completed", "raw_frame": raw_frame,
        "boundary": boundary, "kind": kind, "ordinal": ordinal,
        "submission_fingerprint": fingerprint,
    }


def _timing_bytes(*events: dict[str, Any]) -> bytes:
    return b"".join(_compact_json_bytes(event) for event in events)


def _metadata_semantics(game: str, profile: str) -> dict[str, Any]:
    return {
        "game": game, "zone": "synthetic-zone", "zone_id": 3, "act": 2,
        "bk2_frame_offset": 7, "ring_floor_check_counter_phase": 6,
        "trace_frame_count": 1,
        "start_x_hex": "0x8001", "start_y_hex": "0x7ffe",
        "start_x": -32767, "start_y": 32766,
        "recording_date": "2000-01-02T03:04:05Z",
        "recorder": "native-bizhawk-headless", "recorder_version": "3.0",
        "trace_schema": 5, "trace_profile": profile,
        "bizhawk_version": "2.11", "genesis_core": "GPGX",
        "aux_schema_extras": ["synthetic_contract_extension"], "rom_zone_id": 131,
        "route": "synthetic-route", "source_bk2": "synthetic-input.bk2",
        "rom_checksum": "synthetic-no-rom", "notes": "synthetic contract only",
        "characters": ["sonic", "tails"], "main_character": "sonic",
        "sidekicks": ["tails"], "pre_trace_osc_frames": 9,
        "rng_seed_hex": "0x80000001", "initial_rng_seed": 2147483649,
        "trace_type": "level", "input_source": "bk2", "credits_demo_index": 4,
        "credits_demo_slug": "synthetic-credits", "special_stage_index": 5,
        "run_id": "synthetic-run", "segment_index": 6,
        "bonus_stage_type": "slot_machine", "fresh_load": True,
        "v_int_run_count": 254,
    }


def _ordinary_semantics(
        compressed: bool, *, aux_events: list[dict[str, Any]] | None = None,
        timing_events: list[dict[str, Any]] | None = None) -> dict[str, Any]:
    if aux_events is None:
        aux_events = ([{"frame": 0, "event": "state_snapshot",
                       "tag": "gzip", "value": 8}]
                      if compressed else
                      [{"frame": 0, "event": "state_snapshot",
                       "tag": "synthetic", "value": 7}])
    if timing_events is None:
        timing_events = ([] if compressed else [
            {"raw_frame": 0, "boundary": "vint_service",
             "kind": "kos_module_queue", "ordinal": 2,
             "submission_fingerprint": FINGERPRINT_A},
            {"raw_frame": 0, "boundary": "post_objects",
             "kind": "kos_module_queue", "ordinal": 3,
             "submission_fingerprint": FINGERPRINT_B},
            {"raw_frame": 0, "boundary": "pre_main_loop",
             "kind": "kos_decompression_queue", "ordinal": 4,
             "submission_fingerprint": FINGERPRINT_B},
            {"raw_frame": 0, "boundary": "pre_main_loop",
             "kind": "nemesis_plc_queue", "ordinal": 5,
             "submission_fingerprint": FINGERPRINT_C},
        ])
    return {
        "metadata": _metadata_semantics("s2", "complete_run"),
        "frames": [{
            "frame": 0, "input": 1, "camera_x": 16, "camera_y": 32, "rings": 3,
            "gameplay_frame_counter": 4, "vblank_counter": 5, "lag_counter": 6,
            "player": {"present": True, "x": -16, "y": 48, "x_speed": -2,
                       "y_speed": 2, "g_speed": 3, "angle": 4, "air": True,
                       "rolling": False, "ground_mode": 2, "x_sub": 5,
                       "y_sub": 6, "routine": 7, "status": 8,
                       "stand_on_obj": 9, "animation_id": 10, "mapping_frame": 11},
            "sidekick": {"present": True, "x": 64, "y": 80, "x_speed": 6,
                         "y_speed": -7, "g_speed": 8, "angle": 9, "air": False,
                         "rolling": True, "ground_mode": 3, "x_sub": 10,
                         "y_sub": 11, "routine": 12, "status": 13,
                         "stand_on_obj": 14, "animation_id": 15,
                         "mapping_frame": 16},
        }],
        "aux_events": aux_events,
        "hardware_work_completed": timing_events,
    }


def _gzip_semantics() -> dict[str, Any]:
    return _ordinary_semantics(compressed=True)


def _special_semantics(game: str) -> dict[str, Any]:
    if game == "s1":
        frame = {"frame": 10, "input": 42, "lag": True,
                 "x_pos": 2147483649, "y_pos": 2147483646,
                 "vel_x": 65520, "vel_y": 17, "inertia": 34,
                 "status": 51, "ss_angle": 68, "ss_rotate": 85,
                 "bg_anim": 102, "rings": 119, "emeralds": 8}
        profile = "s1_special_stage"
    elif game == "s2":
        character = lambda present, start: {
            "present": present, "ss_x": start, "ss_x_sub": start + 1,
            "ss_y": start + 2, "ss_y_sub": start + 3, "ss_z": start + 4,
            "angle": start + 5, "routine": start + 6,
            "routine_secondary": start + 7, "status": start + 8,
            "anim": start + 9, "anim_frame": start + 10,
            "rings_bcd": start + 11, "hurt_timer": start + 12,
            "slide_timer": start + 13, "flip_timer": start + 14,
        }
        frame = {"frame": 10, "input": 16, "input_p2": 17, "lag": True,
                 "speed_factor": 18, "track_anim": 19, "track_anim_frame": 20,
                 "track_drawing_index": 21, "track_orientation": 22,
                 "track_duration_timer": 23, "current_segment": 24,
                 "player_anim_frame_timer": 25, "rings_togo_bcd": 26,
                 "check_rings_flag": 27, "tails_control_counter": 28,
                 "swap_positions_flag": 29,
                 "sonic": character(True, 31), "tails": character(False, 47)}
        profile = "s2_special_stage"
    else:
        frame = {"frame": 10, "input": 16, "input_p2": 17, "lag": False,
                 "anim_frame": 18, "x_pos": 19, "y_pos": 20, "angle": 21,
                 "velocity": 22, "turning": 23, "jumping": 24,
                 "fade_timer": 25, "spheres_left": 26, "ring_count": 27,
                 "rings_left": 28, "rate": 29, "rate_timer": 30,
                 "clear_timer": 31, "clear_routine": 32, "started": True}
        profile = "s3k_special_stage"
    return {"metadata": {"game": game, "trace_profile": profile,
                         "trace_schema": 5, "trace_frame_count": 1},
            "frames": [frame], "aux_events": []}


def _normalize_case(root: Path, consumer_entry: str) -> dict[str, Any]:
    if consumer_entry == RUN_CONSUMER:
        manifest = _read_json(root / "run_manifest.json")
        return {
            "trace_schema": manifest["trace_schema"], "game": manifest["game"],
            "run_id": manifest["run_id"], "source_bk2": manifest["source_bk2"],
            "rom_checksum": manifest["rom_checksum"],
            "expected_movie_end_mode": manifest["expected_movie_end_mode"],
            "segments": manifest["segments"], "transitions": manifest["transitions"],
            "dynamic_art_gap_transitions": manifest["dynamic_art_gap_transitions"],
            "member_order": [segment["dir"] for segment in manifest["segments"]],
        }

    metadata = _read_json(root / "metadata.json")
    normalized_metadata = (
        _normalize_metadata(metadata)
        if consumer_entry == LEVEL_CONSUMER else
        {key: metadata[key] for key in (
            "game", "trace_profile", "trace_schema", "trace_frame_count")}
    )
    rows = _read_csv_rows(_single(root, "physics.csv"))
    auxiliary = _optional_single(root, "aux_state.jsonl")
    aux_events = _read_jsonl(auxiliary) if auxiliary is not None else []
    if consumer_entry == LEVEL_CONSUMER:
        timing = _optional_single(root, "hardware_timing.jsonl")
        events = _read_jsonl(timing) if timing is not None else []
        return {
            "metadata": normalized_metadata,
            "frames": [_normalize_level_row(row) for row in rows],
            "aux_events": aux_events,
            "hardware_work_completed": [
                {key: event[key] for key in (
                    "raw_frame", "boundary", "kind", "ordinal",
                    "submission_fingerprint")}
                for event in events
            ],
        }
    game = metadata["game"]
    return {"metadata": normalized_metadata,
            "frames": [_normalize_special_row(game, row) for row in rows],
            "aux_events": aux_events}


def _normalize_level_row(row: list[str]) -> dict[str, Any]:
    value = lambda index: int(row[index], 16)
    signed = lambda index: _signed_16(value(index))
    character = lambda start: {
        "present": value(start) != 0, "x": signed(start + 1), "y": signed(start + 2),
        "x_speed": signed(start + 3), "y_speed": signed(start + 4),
        "g_speed": signed(start + 5), "angle": value(start + 6),
        "air": value(start + 7) != 0, "rolling": value(start + 8) != 0,
        "ground_mode": value(start + 9), "x_sub": value(start + 10),
        "y_sub": value(start + 11), "routine": value(start + 12),
        "status": value(start + 13), "stand_on_obj": value(start + 14),
        "animation_id": value(start + 15), "mapping_frame": value(start + 16),
    }
    return {
        "frame": value(0), "input": value(1), "camera_x": value(2),
        "camera_y": value(3), "rings": value(4),
        "gameplay_frame_counter": value(5), "vblank_counter": value(6),
        "lag_counter": value(7), "player": character(8), "sidekick": character(25),
    }


def _normalize_special_row(game: str, row: list[str]) -> dict[str, Any]:
    decimal = int(row[0], 10)
    hex_value = lambda index: int(row[index], 16)
    if game == "s1":
        return {"frame": decimal, "input": hex_value(1), "lag": row[2] != "0",
                "x_pos": hex_value(3), "y_pos": hex_value(4),
                "vel_x": hex_value(5), "vel_y": hex_value(6),
                "inertia": hex_value(7), "status": hex_value(8),
                "ss_angle": hex_value(9), "ss_rotate": hex_value(10),
                "bg_anim": hex_value(11), "rings": hex_value(12),
                "emeralds": hex_value(13)}
    if game == "s2":
        character = lambda start: {
            "present": hex_value(start) != 0, "ss_x": hex_value(start + 1),
            "ss_x_sub": hex_value(start + 2), "ss_y": hex_value(start + 3),
            "ss_y_sub": hex_value(start + 4), "ss_z": hex_value(start + 5),
            "angle": hex_value(start + 6), "routine": hex_value(start + 7),
            "routine_secondary": hex_value(start + 8),
            "status": hex_value(start + 9), "anim": hex_value(start + 10),
            "anim_frame": hex_value(start + 11), "rings_bcd": hex_value(start + 12),
            "hurt_timer": hex_value(start + 13),
            "slide_timer": hex_value(start + 14), "flip_timer": hex_value(start + 15),
        }
        return {"frame": decimal, "input": hex_value(1), "input_p2": hex_value(2),
                "lag": row[3] != "0", "speed_factor": hex_value(4),
                "track_anim": hex_value(5), "track_anim_frame": hex_value(6),
                "track_drawing_index": hex_value(7),
                "track_orientation": hex_value(8),
                "track_duration_timer": hex_value(9),
                "current_segment": hex_value(10),
                "player_anim_frame_timer": hex_value(11),
                "rings_togo_bcd": hex_value(12), "check_rings_flag": hex_value(13),
                "tails_control_counter": hex_value(14),
                "swap_positions_flag": hex_value(15),
                "sonic": character(16), "tails": character(32)}
    return {"frame": decimal, "input": hex_value(1), "input_p2": hex_value(2),
            "lag": row[3] != "0", "anim_frame": hex_value(4),
            "x_pos": hex_value(5), "y_pos": hex_value(6), "angle": hex_value(7),
            "velocity": hex_value(8), "turning": hex_value(9),
            "jumping": hex_value(10), "fade_timer": hex_value(11),
            "spheres_left": hex_value(12), "ring_count": hex_value(13),
            "rings_left": hex_value(14), "rate": hex_value(15),
            "rate_timer": hex_value(16), "clear_timer": hex_value(17),
            "clear_routine": hex_value(18), "started": row[19] != "0"}


def _normalize_metadata(metadata: dict[str, Any]) -> dict[str, Any]:
    start_x = int(metadata["start_x"].removeprefix("0x"), 16)
    start_y = int(metadata["start_y"].removeprefix("0x"), 16)
    rng = int(metadata["rng_seed"].removeprefix("0x"), 16) & 0xFFFFFFFF
    return {
        "game": metadata["game"], "zone": metadata["zone"],
        "zone_id": metadata["zone_id"], "act": metadata["act"],
        "bk2_frame_offset": metadata["bk2_frame_offset"],
        "ring_floor_check_counter_phase": metadata["ring_floor_check_counter_phase"],
        "trace_frame_count": metadata["trace_frame_count"],
        "start_x_hex": metadata["start_x"], "start_y_hex": metadata["start_y"],
        "start_x": _signed_16(start_x), "start_y": _signed_16(start_y),
        "recording_date": metadata["recording_date"], "recorder": metadata["recorder"],
        "recorder_version": metadata["recorder_version"],
        "trace_schema": metadata["trace_schema"], "trace_profile": metadata["trace_profile"],
        "bizhawk_version": metadata["bizhawk_version"],
        "genesis_core": metadata["genesis_core"],
        "aux_schema_extras": metadata["aux_schema_extras"],
        "rom_zone_id": metadata["rom_zone_id"], "route": metadata["route"],
        "source_bk2": metadata["source_bk2"], "rom_checksum": metadata["rom_checksum"],
        "notes": metadata["notes"], "characters": metadata["characters"],
        "main_character": metadata["main_character"], "sidekicks": metadata["sidekicks"],
        "pre_trace_osc_frames": metadata["pre_trace_osc_frames"],
        "rng_seed_hex": metadata["rng_seed"], "initial_rng_seed": rng,
        "trace_type": metadata["trace_type"], "input_source": metadata["input_source"],
        "credits_demo_index": metadata["credits_demo_index"],
        "credits_demo_slug": metadata["credits_demo_slug"],
        "special_stage_index": metadata["special_stage_index"],
        "run_id": metadata["run_id"], "segment_index": metadata["segment_index"],
        "bonus_stage_type": metadata["bonus_stage_type"],
        "fresh_load": metadata["fresh_load"],
        "v_int_run_count": metadata["v_int_run_count"],
    }


def _canonical_diagnostics(case_root: Path) -> list[str]:
    prefix = str(case_root) + "/"
    root_label = str(case_root) + ": "
    return [
        error[len(prefix):] if error.startswith(prefix)
        else error.replace(root_label, "<root>: ", 1)
        for error in Validation(
            case_root, require_frame_keyed_auxiliary=True
        ).run()
    ]


def _validate_manifest_shape(manifest: Any) -> list[str]:
    if not isinstance(manifest, dict):
        return ["manifest.json: manifest must be a JSON object"]
    expected_top = {
        "format", "contract_version", "consumer_expectation_format",
        "rule_coverage", "cases", "files",
    }
    if set(manifest) != expected_top:
        return ["manifest.json: top-level fields do not match the complete schema"]
    if manifest.get("format") != MANIFEST_FORMAT:
        return ["manifest.json: invalid format"]
    if manifest.get("contract_version") != CONTRACT_VERSION:
        return ["manifest.json: invalid contract_version"]
    if manifest.get("consumer_expectation_format") != CONSUMER_EXPECTATION_FORMAT:
        return ["manifest.json: invalid consumer_expectation_format"]
    if not isinstance(manifest.get("rule_coverage"), dict):
        return ["manifest.json: rule_coverage must be an object"]
    if not isinstance(manifest.get("files"), list) or not isinstance(
            manifest.get("cases"), list):
        return ["manifest.json: files and cases must be arrays"]
    cases = manifest["cases"]
    case_ids = [case.get("id") for case in cases if isinstance(case, dict)]
    if len(case_ids) != len(cases) or any(not isinstance(value, str) for value in case_ids):
        return ["manifest.json: every case must be an object with a string id"]
    if case_ids != sorted(case_ids) or len(case_ids) != len(set(case_ids)):
        return ["manifest.json: case ids must be unique and sorted"]
    case_by_id = {case["id"]: case for case in cases}
    case_index_by_id = {case["id"]: index for index, case in enumerate(cases)}
    case_roots: set[str] = set()
    for case in cases:
        outcome = case.get("expected_outcome")
        required = {"id", "root", "rules", "expected_outcome", "producer_entry",
                    "consumer_entry", "consumer_expectation"}
        if outcome == "accept":
            required.add("normalized_semantics")
        elif outcome == "reject":
            required.update({"exact_diagnostic", "fault"})
        else:
            return [f"manifest.json: case {case['id']} has invalid expected_outcome"]
        allowed = set(required) | {"materialization"}
        if set(case) != required and set(case) != allowed:
            return [f"manifest.json: case {case['id']} fields do not match its outcome schema"]
        root = case.get("root")
        if root != f"fixtures/{case['id']}" or root in case_roots:
            return [f"manifest.json: case {case['id']} has invalid or duplicate root"]
        case_roots.add(root)
        rules = case.get("rules")
        if not isinstance(rules, list) or not rules or any(
                not isinstance(rule, str) or not rule for rule in rules):
            return [f"manifest.json: case {case['id']} rules must be nonempty strings"]
        if case.get("producer_entry") != PRODUCER_ENTRY:
            return [f"manifest.json: case {case['id']} has invalid producer_entry"]
        if case.get("consumer_entry") not in CONSUMER_ENTRIES:
            return [f"manifest.json: case {case['id']} has invalid consumer_entry"]
        if not _valid_consumer_expectation(case["consumer_expectation"]):
            return [f"manifest.json: case {case['id']} has invalid consumer_expectation"]
        if outcome == "accept" and (
                not isinstance(case.get("normalized_semantics"), dict)
                or case["consumer_expectation"] != {
                    "outcome": "accept", "semantics_ref": "#/normalized_semantics"}):
            return [f"manifest.json: case {case['id']} accept semantics disagree"]
        if outcome == "reject" and (
                not isinstance(case.get("exact_diagnostic"), str)
                or not case["exact_diagnostic"]
                or not isinstance(case.get("fault"), dict)):
            return [f"manifest.json: case {case['id']} reject authority is incomplete"]
        materialization = case.get("materialization")
        if materialization is not None and (
                not isinstance(materialization, dict)
                or set(materialization) != {"source", "target", "encoding"}
                or materialization.get("encoding") != "base64"):
            return [f"manifest.json: case {case['id']} has invalid materialization"]

    paths = [entry.get("path") for entry in manifest["files"]
             if isinstance(entry, dict)]
    if len(paths) != len(manifest["files"]) or paths != sorted(paths) \
            or len(paths) != len(set(paths)) or any(
                not isinstance(path, str) for path in paths):
        return ["manifest.json: file paths must be unique and sorted"]
    owned_case_ids: set[str] = set()
    for entry in manifest["files"]:
        base = {"path", "stored_size", "stored_sha256", "case_id",
                "expected_outcome", "parser_ref", "expectation_ref"}
        expected_fields = base | ({"logical_size", "logical_sha256"}
                                  if entry["path"].endswith(".gz") else set())
        if set(entry) != expected_fields:
            return [f"manifest.json: file {entry['path']} fields do not match schema"]
        if not _valid_identity(entry.get("stored_size"), entry.get("stored_sha256")):
            return [f"manifest.json: file {entry['path']} has invalid stored identity"]
        if entry["path"].endswith(".gz") and not _valid_identity(
                entry.get("logical_size"), entry.get("logical_sha256")):
            return [f"manifest.json: file {entry['path']} has invalid logical identity"]
        case_id = entry.get("case_id")
        if case_id == "pack-self-description":
            if entry["path"] not in {"readme.md", "manifest.schema.json"} or (
                    entry["expectation_ref"] != "pack-self-description") or (
                    entry["parser_ref"] != "pack-self-description"):
                return [f"manifest.json: file {entry['path']} has invalid pack ownership"]
            continue
        case = case_by_id.get(case_id)
        if case is None or not entry["path"].startswith(case["root"] + "/"):
            return [f"manifest.json: file {entry['path']} has invalid case linkage"]
        owned_case_ids.add(case_id)
        if entry.get("expected_outcome") != case["expected_outcome"]:
            return [f"manifest.json: file {entry['path']} disagrees on expected_outcome"]
        case_ref = f"#/cases/{case_index_by_id[case_id]}"
        if entry.get("parser_ref") != case_ref:
            return [f"manifest.json: file {entry['path']} has invalid parser_ref"]
        if entry.get("expectation_ref") != case_ref:
            return [f"manifest.json: file {entry['path']} has invalid expectation_ref"]
    if owned_case_ids != set(case_by_id):
        return ["manifest.json: every case must own at least one file"]
    for case in cases:
        materialization = case.get("materialization")
        if materialization is None:
            continue
        source = f"{case['root']}/{materialization['source']}"
        if source not in paths or "/" in materialization["target"] or not (
                materialization["target"].endswith((".csv.gz", ".jsonl.gz", ".json.gz"))):
            return [f"manifest.json: case {case['id']} materialization linkage is invalid"]
    if not _valid_rule_coverage(manifest["rule_coverage"], cases):
        return ["manifest.json: rule_coverage does not match case rules and faults"]
    return []


def _valid_identity(size: Any, digest: Any) -> bool:
    return (isinstance(size, int) and not isinstance(size, bool) and size >= 0
            and isinstance(digest, str) and len(digest) == 64
            and all(character in "0123456789abcdef" for character in digest))


def _valid_consumer_expectation(expectation: Any) -> bool:
    if not isinstance(expectation, dict):
        return False
    if expectation.get("outcome") == "accept":
        return (
            (set(expectation) == {"outcome", "semantics_ref"}
             and expectation["semantics_ref"] == "#/normalized_semantics")
            or (set(expectation) == {"outcome", "normalized_semantics"}
                and isinstance(expectation["normalized_semantics"], dict))
        )
    if expectation.get("outcome") != "reject" or set(expectation) != {
            "outcome", "diagnostic"}:
        return False
    diagnostic = expectation["diagnostic"]
    return (isinstance(diagnostic, dict)
            and set(diagnostic) == {
                "exception_class", "message_match", "message"}
            and isinstance(diagnostic["exception_class"], str)
            and diagnostic["exception_class"].startswith(("java.", "com.fasterxml."))
            and diagnostic["message_match"] in {"exact", "contains"}
            and isinstance(diagnostic["message"], str) and bool(diagnostic["message"]))


def _valid_rule_coverage(coverage: dict[str, Any], cases: list[dict[str, Any]]) -> bool:
    expected: dict[str, dict[str, list[str]]] = {}
    for case in cases:
        for rule in case["rules"]:
            group = expected.setdefault(rule, {"accept_cases": [], "reject_cases": []})
            group[f"{case['expected_outcome']}_cases"].append(case["id"])
        if case["expected_outcome"] == "reject" and case["fault"].get("rule") \
                not in case["rules"]:
            return False
    return coverage == {key: expected[key] for key in sorted(expected)} and all(
        value["accept_cases"] and value["reject_cases"] for value in expected.values())


def _case_entry(case: Case) -> dict[str, Any]:
    entry = {
        "id": case.identifier,
        "root": f"fixtures/{case.identifier}",
        "rules": list(case.rules),
        "expected_outcome": case.expected_outcome,
        "producer_entry": PRODUCER_ENTRY,
        "consumer_entry": case.consumer_entry,
        "consumer_expectation": (
            case.consumer_expectation
            if case.consumer_expectation is not None else
            {"outcome": "accept", "semantics_ref": "#/normalized_semantics"}
        ),
    }
    if case.expected_outcome == "accept":
        entry["normalized_semantics"] = case.normalized_semantics
    else:
        entry["exact_diagnostic"] = case.exact_diagnostic
        entry["fault"] = case.fault
    if case.materialization is not None:
        entry["materialization"] = case.materialization
    return entry


def _file_entry(path: str, content: bytes, case: Case | None,
                case_index: int | None) -> dict[str, Any]:
    if case is None:
        entry: dict[str, Any] = {
            "path": path, "stored_size": len(content), "stored_sha256": _sha256(content),
            "case_id": "pack-self-description", "expected_outcome": "accept",
            "parser_ref": "pack-self-description",
            "expectation_ref": "pack-self-description",
        }
    else:
        assert case_index is not None
        entry = {
            "path": path, "stored_size": len(content), "stored_sha256": _sha256(content),
            "case_id": case.identifier, "expected_outcome": case.expected_outcome,
            "parser_ref": f"#/cases/{case_index}",
            "expectation_ref": f"#/cases/{case_index}",
        }
    if path.endswith(".gz"):
        logical = gzip.decompress(content)
        entry["logical_size"] = len(logical)
        entry["logical_sha256"] = _sha256(logical)
    return entry


@contextmanager
def _materialized_case_root(root: Path, case: dict[str, Any]):
    original = root / case["root"]
    materialization = case.get("materialization")
    if materialization is None:
        yield original
        return
    with tempfile.TemporaryDirectory() as temporary:
        generated = Path(temporary) / case["id"]
        shutil.copytree(original, generated)
        payload = _read_json(generated / materialization["source"])
        if payload.get("encoding") != "base64":
            raise ValueError("materialized payload must declare base64 encoding")
        target = generated / materialization["target"]
        target.write_bytes(base64.b64decode(payload["base64"], validate=True))
        yield generated


def _case_for_path(path: str, cases: list[Case]) -> tuple[Case | None, int | None]:
    for index, case in enumerate(cases):
        if path.startswith(f"fixtures/{case.identifier}/"):
            return case, index
    return None, None


def _read_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(), object_pairs_hook=reject_duplicate_keys)


def _read_jsonl(path: Path) -> list[dict[str, Any]]:
    text = _read_text(path)
    return [json.loads(line, object_pairs_hook=reject_duplicate_keys)
            for line in text.splitlines() if line.strip()]


def _read_csv_rows(path: Path) -> list[list[str]]:
    rows = list(csv.reader(_read_text(path).splitlines()))
    return rows[1:] if rows and rows[0] and rows[0][0].strip().lower() == "frame" else rows


def _read_text(path: Path) -> str:
    if path.name.endswith(".gz"):
        return gzip.decompress(path.read_bytes()).decode("utf-8")
    return path.read_text(encoding="utf-8")


def _single(root: Path, name: str) -> Path:
    result = _optional_single(root, name)
    if result is None:
        raise FileNotFoundError(name)
    return result


def _optional_single(root: Path, name: str) -> Path | None:
    plain, compressed = root / name, root / f"{name}.gz"
    present = [path for path in (plain, compressed) if path.is_file()]
    if len(present) > 1:
        raise ValueError(f"both plain and gzip {name}")
    return present[0] if present else None


def _signed_16(value: int) -> int:
    return value - 0x10000 if value > 0x7FFF else value


def _csv(header: str, row: str) -> bytes:
    return f"{header}\n{row}\n".encode()


def _gzip(content: bytes, *, mtime: int = 0) -> bytes:
    target = io.BytesIO()
    with gzip.GzipFile(fileobj=target, mode="wb", filename="", mtime=mtime) as output:
        output.write(content)
    return target.getvalue()


def _json_bytes(value: Any) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True) + "\n").encode()


def _compact_json_bytes(value: Any) -> bytes:
    return (json.dumps(value, sort_keys=True, separators=(",", ":")) + "\n").encode()


def _sha256(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()


def _manifest_schema_bytes() -> bytes:
    return _json_bytes({
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "$id": "https://github.com/OpenGGF/TraceChaser/contracts/v5/manifest.schema.json",
        "title": "TraceChaser trace-v5 semantic conformance manifest",
        "type": "object",
        "additionalProperties": False,
        "required": ["format", "contract_version", "consumer_expectation_format",
                     "rule_coverage", "cases", "files"],
        "properties": {
            "format": {"const": MANIFEST_FORMAT},
            "contract_version": {"const": CONTRACT_VERSION},
            "consumer_expectation_format": {
                "const": CONSUMER_EXPECTATION_FORMAT},
            "rule_coverage": {"type": "object", "additionalProperties": {
                "$ref": "#/$defs/ruleCoverage"}},
            "cases": {"type": "array", "items": {"$ref": "#/$defs/case"}},
            "files": {"type": "array", "items": {"$ref": "#/$defs/file"}},
        },
        "$defs": {
            "outcome": {"enum": ["accept", "reject"]},
            "ruleCoverage": {"type": "object", "additionalProperties": False,
                "required": ["accept_cases", "reject_cases"], "properties": {
                    "accept_cases": {"type": "array", "minItems": 1,
                                     "items": {"type": "string"}},
                    "reject_cases": {"type": "array", "minItems": 1,
                                     "items": {"type": "string"}},
                }},
            "diagnostic": {"type": "object", "additionalProperties": False,
                "required": ["exception_class", "message_match", "message"],
                "properties": {
                    "exception_class": {"type": "string"},
                    "message_match": {"enum": ["exact", "contains"]},
                    "message": {"type": "string", "minLength": 1},
                }},
            "consumerExpectation": {"oneOf": [
                {"type": "object", "additionalProperties": False,
                 "required": ["outcome", "semantics_ref"], "properties": {
                     "outcome": {"const": "accept"},
                     "semantics_ref": {"const": "#/normalized_semantics"}}},
                {"type": "object", "additionalProperties": False,
                 "required": ["outcome", "normalized_semantics"], "properties": {
                     "outcome": {"const": "accept"},
                     "normalized_semantics": {"type": "object"}}},
                {"type": "object", "additionalProperties": False,
                 "required": ["outcome", "diagnostic"], "properties": {
                     "outcome": {"const": "reject"},
                     "diagnostic": {"$ref": "#/$defs/diagnostic"}}},
            ]},
            "fault": {"type": "object", "minProperties": 2,
                      "required": ["rule", "kind"]},
            "materialization": {"type": "object", "additionalProperties": False,
                "required": ["source", "target", "encoding"], "properties": {
                    "source": {"type": "string"}, "target": {"type": "string"},
                    "encoding": {"const": "base64"},
                }},
            "case": {"type": "object", "required": ["id", "root", "rules",
                "expected_outcome", "producer_entry", "consumer_entry",
                "consumer_expectation"],
                "additionalProperties": False,
                "properties": {
                    "id": {"type": "string"}, "root": {"type": "string"},
                    "rules": {"type": "array", "minItems": 1,
                              "items": {"type": "string"}},
                    "expected_outcome": {"$ref": "#/$defs/outcome"},
                    "producer_entry": {"const": PRODUCER_ENTRY},
                    "consumer_entry": {"enum": sorted(CONSUMER_ENTRIES)},
                    "consumer_expectation": {"$ref": "#/$defs/consumerExpectation"},
                    "normalized_semantics": {"type": "object"},
                    "exact_diagnostic": {"type": "string"},
                    "fault": {"$ref": "#/$defs/fault"},
                    "materialization": {"$ref": "#/$defs/materialization"},
                },
                "allOf": [
                    {"if": {"properties": {"expected_outcome": {"const": "accept"}}},
                     "then": {"required": ["normalized_semantics"]}},
                    {"if": {"properties": {"expected_outcome": {"const": "reject"}}},
                     "then": {"required": ["exact_diagnostic", "fault"]}},
                ]},
            "file": {"type": "object", "required": ["path", "stored_size",
                "stored_sha256", "case_id", "expected_outcome", "parser_ref",
                "expectation_ref"], "additionalProperties": False,
                "properties": {
                    "path": {"type": "string"},
                    "stored_size": {"type": "integer", "minimum": 0},
                    "stored_sha256": {"type": "string", "pattern": "^[0-9a-f]{64}$"},
                    "logical_size": {"type": "integer", "minimum": 0},
                    "logical_sha256": {"type": "string", "pattern": "^[0-9a-f]{64}$"},
                    "case_id": {"type": "string"},
                    "expected_outcome": {"$ref": "#/$defs/outcome"},
                    "parser_ref": {"type": "string"},
                    "expectation_ref": {"type": "string"},
                }, "allOf": [{
                    "if": {"properties": {"path": {"pattern": "\\.gz$"}}},
                    "then": {"required": ["logical_size", "logical_sha256"]},
                    "else": {"not": {"anyOf": [
                        {"required": ["logical_size"]},
                        {"required": ["logical_sha256"]},
                    ]}},
                }]},
        },
    })


def _readme_bytes() -> bytes:
    return b"""# Trace-v5 semantic conformance pack

This bounded pack contains synthetic trace-schema-5 documents only. It contains
no ROM bytes, BK2 movie data, or copied canonical OpenGGF traces.

`manifest.json` pins every member's stored SHA-256 and byte length. Deterministic
gzip members additionally pin logical SHA-256 and length. Every case names the
TraceChaser producer entry, the real OpenGGF Java consumer entry for the Task 10
copy, and normalized accepted semantics or structured, source-pinned consumer
diagnostics. Array order in normalized semantics is significant. Neutral JSON
containers carry deliberately malformed or nondeterministic gzip bytes; each
case declares the exact logical parser target that Task 10 must materialize.

Run `python3 traces/validate_v5_conformance.py` from the repository root. The
validator checks identities, rejects unmanifested members, runs every case
through `traces.validate_trace_v5.Validation`, and compares accepted values
instead of treating hashes as semantic proof.

Regenerate into an empty directory with
`python3 traces/generate_v5_conformance.py <directory>` and compare the result
byte-for-byte. Do not hand-edit generated members.
"""
