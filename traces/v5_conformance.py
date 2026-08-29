#!/usr/bin/env python3
"""Build and verify the portable, synthetic trace-v5 conformance pack."""

from __future__ import annotations

import csv
import gzip
import hashlib
import io
import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from traces.validate_trace_v5 import Validation, reject_duplicate_keys


MANIFEST_FORMAT = "tracechaser-v5-artifact-manifest-v1"
CONTRACT_VERSION = 1
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
S1_SPECIAL_ROW = "0,1,0,00010000,00020000,0003,0004,0005,06,07,08,09,000a,000b"
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
    ["0", "1", "2", "1"] + [format(value, "x") for value in range(4, 48)]
)
S3K_SPECIAL_HEADER = (
    "frame,input,input_p2,lag,anim_frame,x_pos,y_pos,angle,velocity,turning,"
    "jumping,fade_timer,spheres_left,ring_count,rings_left,rate,rate_timer,"
    "clear_timer,clear_routine,started"
)
S3K_SPECIAL_ROW = ",".join(
    ["0", "1", "2", "1"]
    + [format(value, "x") for value in range(4, 19)]
    + ["1"]
)


@dataclass(frozen=True)
class Case:
    identifier: str
    rules: tuple[str, ...]
    consumer_entry: str
    files: dict[str, bytes]
    normalized_semantics: dict[str, Any] | None = None
    exact_diagnostic: str | None = None
    consumer_exact_diagnostic: str | None = None

    @property
    def expected_outcome(self) -> str:
        return "accept" if self.exact_diagnostic is None else "reject"


def build_pack(destination: Path) -> None:
    """Write the complete pack into a new or empty directory."""
    if destination.exists() and any(destination.iterdir()):
        raise ValueError(f"destination is not empty: {destination}")
    destination.mkdir(parents=True, exist_ok=True)

    schema = _manifest_schema_bytes()
    readme = _readme_bytes()
    members: dict[str, bytes] = {
        "readme.md": readme,
        "manifest.schema.json": schema,
    }
    cases = _cases()
    for case in cases:
        root = f"fixtures/{case.identifier}"
        for relative_path, content in case.files.items():
            members[f"{root}/{relative_path}"] = content

    entries = [
        _file_entry(path, content, _case_for_path(path, cases))
        for path, content in sorted(members.items())
    ]
    manifest = {
        "format": MANIFEST_FORMAT,
        "contract_version": CONTRACT_VERSION,
        "consumer_expectation_format": "openggf-trace-v5-consumer-expectation-v1",
        "cases": [_case_entry(case) for case in cases],
        "files": entries,
    }
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
        case_root = root / case["root"]
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
        Case(
            "accept-level",
            (
                "metadata-envelope", "ordinary-42-columns", "aux-jsonl",
                "removed-v5-field",
                "hardware-kind", "hardware-boundary", "hardware-order",
                "hardware-ordinal", "hardware-frame-bound", "hardware-fingerprint",
            ),
            LEVEL_CONSUMER,
            ordinary_files,
            _ordinary_semantics(compressed=False),
        ),
        Case(
            "accept-gzip",
            ("deterministic-gzip",),
            LEVEL_CONSUMER,
            {
                "metadata.json": ordinary_metadata,
                "physics.csv.gz": _gzip(_csv(LEVEL_HEADER, LEVEL_ROW)),
                "aux_state.jsonl.gz": _gzip(
                    b'{"frame":0,"event":"state_snapshot","tag":"gzip","value":8}\n'
                ),
            },
            _gzip_semantics(),
        ),
        Case(
            "accept-s1-special", ("s1-special-14-columns",),
            S1_SPECIAL_CONSUMER,
            {"metadata.json": _metadata("s1", "s1_special_stage"),
             "physics.csv": _csv(S1_SPECIAL_HEADER, S1_SPECIAL_ROW)},
            _special_semantics("s1"),
        ),
        Case(
            "accept-s2-special", ("s2-special-48-columns",),
            S2_SPECIAL_CONSUMER,
            {"metadata.json": _metadata("s2", "s2_special_stage"),
             "physics.csv": _csv(S2_SPECIAL_HEADER, S2_SPECIAL_ROW)},
            _special_semantics("s2"),
        ),
        Case(
            "accept-s3k-special", ("s3k-special-20-columns",),
            S3K_SPECIAL_CONSUMER,
            {"metadata.json": _metadata("s3k", "s3k_special_stage"),
             "physics.csv": _csv(S3K_SPECIAL_HEADER, S3K_SPECIAL_ROW)},
            _special_semantics("s3k"),
        ),
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
                            "line 1 has invalid kind"),
        _reject_timing_case("reject-hardware-boundary", "hardware-boundary",
                            _timing(0, "unknown_boundary", "kos_module_queue", 0,
                                    FINGERPRINT_A),
                            "line 1 has invalid boundary"),
        _reject_timing_case("reject-direct-boundary", "hardware-boundary",
                            _timing(0, "post_objects", "kos_decompression_queue", 0,
                                    FINGERPRINT_A),
                            "line 1 direct completion kind requires pre_main_loop boundary"),
        _reject_timing_order_case(),
        _reject_timing_ordinal_case(),
        _reject_timing_case("reject-hardware-frame", "hardware-frame-bound",
                            _timing(1, "vint_service", "kos_module_queue", 0,
                                    FINGERPRINT_A),
                            "line 1 raw_frame 1 is outside [0, 1)"),
        _reject_timing_case("reject-hardware-fingerprint", "hardware-fingerprint",
                            _timing(0, "vint_service", "kos_module_queue", 0,
                                    "SHA256:" + "a" * 64),
                            "line 1 has invalid submission_fingerprint"),
        _reject_gzip_case(),
        _reject_run_collections_case(),
        _reject_run_order_case(),
    ])
    for field in ("lua_script_version", "csv_version", "ss_csv_version",
                  "hardware_timing_schema", "run_schema"):
        cases.append(_reject_removed_field_case(field))
    return sorted(cases, key=lambda case: case.identifier)


def _accepted_run_case() -> Case:
    manifest = {
        "trace_schema": 5,
        "game": "s2",
        "run_id": "synthetic-two-segment-run",
        "source_bk2": "synthetic-input.bk2",
        "rom_checksum": "synthetic-no-rom",
        "recorder": "native-bizhawk-headless",
        "recorder_version": "3.0",
        "segments": [
            {"dir": "seg0", "kind": "level", "trace_profile": "complete_run",
             "bk2_frame_offset": 10, "trace_frame_count": 1, "zone_id": 0, "act": 1},
            {"dir": "seg1", "kind": "level", "trace_profile": "complete_run",
             "bk2_frame_offset": 20, "trace_frame_count": 1, "zone_id": 0, "act": 2},
        ],
        "transitions": [
            {"from_segment": 0, "to_segment": 1, "entry_kind": "level_advance",
             "mode_change_bk2_frame": 19},
        ],
        "dynamic_art_gap_transitions": [],
    }
    return Case(
        "accept-run-manifest",
        ("run-manifest-collections", "run-manifest-member-order"),
        RUN_CONSUMER,
        _run_files(manifest),
        {
            "game": "s2", "run_id": "synthetic-two-segment-run",
            "segments": [
                {"dir": "seg0", "kind": "level", "trace_profile": "complete_run",
                 "bk2_frame_offset": 10, "trace_frame_count": 1},
                {"dir": "seg1", "kind": "level", "trace_profile": "complete_run",
                 "bk2_frame_offset": 20, "trace_frame_count": 1},
            ],
            "transitions": [
                {"from_segment": 0, "to_segment": 1,
                 "entry_kind": "level_advance"},
            ],
            "dynamic_art_gap_transition_count": 0,
            "member_order": ["seg0", "seg1"],
        },
    )


def _reject_metadata_case(identifier: str, field: str, value: Any, rule: str,
                          producer: str, consumer: str) -> Case:
    metadata = json.loads(_metadata("s2", "complete_run"))
    metadata[field] = value
    return Case(identifier, (rule,), LEVEL_CONSUMER,
                {"metadata.json": _json_bytes(metadata),
                 "physics.csv": _csv(LEVEL_HEADER, LEVEL_ROW)},
                exact_diagnostic=producer,
                consumer_exact_diagnostic=consumer)


def _reject_removed_field_case(field: str) -> Case:
    return _reject_metadata_case(
        f"reject-removed-{field.replace('_', '-')}", field, 1,
        "removed-v5-field",
        f"metadata.json: forbidden legacy key {field}",
        f"metadata.json: unsupported legacy field {field}",
    )


def _reject_width_case(identifier: str, rule: str, game: str, profile: str,
                       header: str, bad_width: int, expected_width: int,
                       consumer_entry: str) -> Case:
    row = ",".join(["0"] * bad_width)
    producer = f"physics.csv: row 1 has {bad_width} columns; expected {expected_width}"
    if expected_width == 42:
        consumer = (
            f"Trace schema 5 requires 42 or 43 CSV columns, got {bad_width}: {row}"
        )
    else:
        consumer = f"Expected {expected_width} CSV columns, got {bad_width}: {row}"
    return Case(identifier, (rule,), consumer_entry,
                {"metadata.json": _metadata(game, profile),
                 "physics.csv": _csv(header, row)},
                exact_diagnostic=producer,
                consumer_exact_diagnostic=consumer)


def _reject_aux_case() -> Case:
    line = '{"event":"state_snapshot","tag":"missing-frame"}'
    return Case(
        "reject-aux-missing-frame", ("aux-jsonl",), LEVEL_CONSUMER,
        {"metadata.json": _metadata("s2", "complete_run"),
         "physics.csv": _csv(LEVEL_HEADER, LEVEL_ROW),
         "aux_state.jsonl": (line + "\n").encode()},
        exact_diagnostic="aux_state.jsonl: line 1 has invalid or missing frame",
        consumer_exact_diagnostic=f"Failed to parse JSONL line: {line}",
    )


def _reject_timing_case(identifier: str, rule: str, event: dict[str, Any],
                        diagnostic: str) -> Case:
    return Case(
        identifier, (rule,), LEVEL_CONSUMER,
        {"metadata.json": _metadata("s2", "complete_run"),
         "physics.csv": _csv(LEVEL_HEADER, LEVEL_ROW),
         "hardware_timing.jsonl": _timing_bytes(event)},
        exact_diagnostic=f"hardware_timing.jsonl: {diagnostic}",
        consumer_exact_diagnostic=f"hardware_timing.jsonl: {diagnostic}",
    )


def _reject_timing_order_case() -> Case:
    return Case(
        "reject-hardware-order", ("hardware-order",), LEVEL_CONSUMER,
        {"metadata.json": _metadata("s2", "complete_run"),
         "physics.csv": _csv(LEVEL_HEADER, LEVEL_ROW),
         "hardware_timing.jsonl": _timing_bytes(
             _timing(0, "pre_main_loop", "kos_decompression_queue", 0, FINGERPRINT_A),
             _timing(0, "post_objects", "kos_module_queue", 0, FINGERPRINT_B))},
        exact_diagnostic="hardware_timing.jsonl: line 2 events must use canonical ordering",
        consumer_exact_diagnostic="hardware_timing.jsonl: events must use canonical ordering",
    )


def _reject_timing_ordinal_case() -> Case:
    return Case(
        "reject-hardware-ordinal", ("hardware-ordinal",), LEVEL_CONSUMER,
        {"metadata.json": _metadata("s2", "complete_run"),
         "physics.csv": _csv(LEVEL_HEADER, LEVEL_ROW),
         "hardware_timing.jsonl": _timing_bytes(
             _timing(0, "vint_service", "kos_module_queue", 2, FINGERPRINT_A),
             _timing(0, "post_objects", "kos_module_queue", 1, FINGERPRINT_B))},
        exact_diagnostic=(
            "hardware_timing.jsonl: line 2 ordinal must increase per kind kos_module_queue"
        ),
        consumer_exact_diagnostic=(
            "hardware_timing.jsonl: ordinal must increase per kind KOS_MODULE_QUEUE"
        ),
    )


def _reject_gzip_case() -> Case:
    row = ",".join(["0"] * 41)
    return Case(
        "reject-gzip-logical-width", ("deterministic-gzip",), LEVEL_CONSUMER,
        {"metadata.json": _metadata("s2", "complete_run"),
         "physics.csv.gz": _gzip(_csv(LEVEL_HEADER, row))},
        exact_diagnostic="physics.csv.gz: row 1 has 41 columns; expected 42",
        consumer_exact_diagnostic=(
            f"Trace schema 5 requires 42 or 43 CSV columns, got 41: {row}"
        ),
    )


def _reject_run_collections_case() -> Case:
    manifest = {
        "trace_schema": 5, "game": "s2", "run_id": "bad-collections",
        "recorder": "native-bizhawk-headless", "recorder_version": "3.0",
        "segments": {}, "transitions": [], "dynamic_art_gap_transitions": [],
    }
    return Case(
        "reject-run-collections", ("run-manifest-collections",), RUN_CONSUMER,
        _run_files(manifest, segments=("seg0",)),
        exact_diagnostic="run_manifest.json: segments must be an array",
        consumer_exact_diagnostic="Manifest has no segments",
    )


def _reject_run_order_case() -> Case:
    manifest = {
        "trace_schema": 5, "game": "s2", "run_id": "bad-order",
        "recorder": "native-bizhawk-headless", "recorder_version": "3.0",
        "segments": [
            {"dir": "seg0", "kind": "level", "trace_profile": "complete_run",
             "bk2_frame_offset": 20, "trace_frame_count": 1},
            {"dir": "seg1", "kind": "level", "trace_profile": "complete_run",
             "bk2_frame_offset": 10, "trace_frame_count": 1},
        ],
        "transitions": [], "dynamic_art_gap_transitions": [],
    }
    return Case(
        "reject-run-order", ("run-manifest-member-order",), RUN_CONSUMER,
        _run_files(manifest),
        exact_diagnostic=(
            "run_manifest.json: segment 1 bk2_frame_offset must be strictly increasing"
        ),
        consumer_exact_diagnostic=(
            "Segment 1 bk2_frame_offset 10 is not strictly increasing"
        ),
    )


def _run_files(manifest: dict[str, Any], segments: tuple[str, ...] = ("seg0", "seg1")) \
        -> dict[str, bytes]:
    files = {"run_manifest.json": _json_bytes(manifest)}
    for segment in segments:
        files[f"{segment}/metadata.json"] = _metadata("s2", "complete_run")
        files[f"{segment}/physics.csv"] = _csv(LEVEL_HEADER, LEVEL_ROW)
    return files


def _metadata(game: str, profile: str, frame_count: int = 1) -> bytes:
    return _json_bytes({
        "game": game,
        "zone": "synthetic",
        "zone_id": 0,
        "act": 1,
        "bk2_frame_offset": 0,
        "trace_frame_count": frame_count,
        "start_x": "0x0000",
        "start_y": "0x0000",
        "trace_profile": profile,
        "trace_schema": 5,
        "recorder": "native-bizhawk-headless",
        "recorder_version": "3.0",
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


def _ordinary_semantics(compressed: bool) -> dict[str, Any]:
    return {
        "metadata": {"game": "s2", "trace_profile": "complete_run",
                     "trace_schema": 5, "trace_frame_count": 1},
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
        "aux_events": ([{"frame": 0, "event": "state_snapshot",
                         "tag": "gzip", "value": 8}]
                       if compressed else
                       [{"frame": 0, "event": "state_snapshot",
                         "tag": "synthetic", "value": 7}]),
        "hardware_work_completed": ([] if compressed else [
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
        ]),
    }


def _gzip_semantics() -> dict[str, Any]:
    return _ordinary_semantics(compressed=True)


def _special_semantics(game: str) -> dict[str, Any]:
    if game == "s1":
        frame = {"frame": 0, "input": 1, "lag": False, "x_pos": 65536,
                 "y_pos": 131072, "vel_x": 3, "vel_y": 4, "rings": 10,
                 "emeralds": 11}
        profile = "s1_special_stage"
    elif game == "s2":
        frame = {"frame": 0, "input": 1, "input_p2": 2, "lag": True,
                 "speed_factor": 4, "current_segment": 10,
                 "sonic_present": True, "sonic_ss_x": 17,
                 "tails_present": True, "tails_ss_x": 33}
        profile = "s2_special_stage"
    else:
        frame = {"frame": 0, "input": 1, "input_p2": 2, "lag": True,
                 "anim_frame": 4, "x_pos": 5, "y_pos": 6,
                 "spheres_left": 12, "rings_left": 14, "started": True}
        profile = "s3k_special_stage"
    return {"metadata": {"game": game, "trace_profile": profile,
                         "trace_schema": 5, "trace_frame_count": 1},
            "frames": [frame], "aux_events": []}


def _normalize_case(root: Path, consumer_entry: str) -> dict[str, Any]:
    if consumer_entry == RUN_CONSUMER:
        manifest = _read_json(root / "run_manifest.json")
        return {
            "game": manifest["game"], "run_id": manifest["run_id"],
            "segments": [
                {key: segment[key] for key in (
                    "dir", "kind", "trace_profile", "bk2_frame_offset",
                    "trace_frame_count")}
                for segment in manifest["segments"]
            ],
            "transitions": [
                {key: transition[key] for key in (
                    "from_segment", "to_segment", "entry_kind")}
                for transition in manifest["transitions"]
            ],
            "dynamic_art_gap_transition_count": len(
                manifest["dynamic_art_gap_transitions"]),
            "member_order": [segment["dir"] for segment in manifest["segments"]],
        }

    metadata = _read_json(root / "metadata.json")
    normalized_metadata = {
        key: metadata[key]
        for key in ("game", "trace_profile", "trace_schema", "trace_frame_count")
    }
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
                "rings": hex_value(12), "emeralds": hex_value(13)}
    if game == "s2":
        return {"frame": decimal, "input": hex_value(1), "input_p2": hex_value(2),
                "lag": row[3] != "0", "speed_factor": hex_value(4),
                "current_segment": hex_value(10), "sonic_present": hex_value(16) != 0,
                "sonic_ss_x": hex_value(17), "tails_present": hex_value(32) != 0,
                "tails_ss_x": hex_value(33)}
    return {"frame": decimal, "input": hex_value(1), "input_p2": hex_value(2),
            "lag": row[3] != "0", "anim_frame": hex_value(4),
            "x_pos": hex_value(5), "y_pos": hex_value(6),
            "spheres_left": hex_value(12), "rings_left": hex_value(14),
            "started": row[19] != "0"}


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
    if manifest.get("format") != MANIFEST_FORMAT:
        return ["manifest.json: invalid format"]
    if manifest.get("contract_version") != CONTRACT_VERSION:
        return ["manifest.json: invalid contract_version"]
    if not isinstance(manifest.get("files"), list) or not isinstance(
            manifest.get("cases"), list):
        return ["manifest.json: files and cases must be arrays"]
    paths = [entry.get("path") for entry in manifest["files"]
             if isinstance(entry, dict)]
    if len(paths) != len(manifest["files"]) or paths != sorted(paths) \
            or len(paths) != len(set(paths)):
        return ["manifest.json: file paths must be unique and sorted"]
    case_ids = [case.get("id") for case in manifest["cases"]
                if isinstance(case, dict)]
    if len(case_ids) != len(manifest["cases"]) or case_ids != sorted(case_ids) \
            or len(case_ids) != len(set(case_ids)):
        return ["manifest.json: case ids must be unique and sorted"]
    return []


def _case_entry(case: Case) -> dict[str, Any]:
    entry = {
        "id": case.identifier,
        "root": f"fixtures/{case.identifier}",
        "rules": list(case.rules),
        "expected_outcome": case.expected_outcome,
        "producer_entry": PRODUCER_ENTRY,
        "consumer_entry": case.consumer_entry,
    }
    if case.expected_outcome == "accept":
        entry["normalized_semantics"] = case.normalized_semantics
    else:
        entry["exact_diagnostic"] = case.exact_diagnostic
        entry["consumer_exact_diagnostic"] = case.consumer_exact_diagnostic
    return entry


def _file_entry(path: str, content: bytes, case: Case | None) -> dict[str, Any]:
    if case is None:
        entry: dict[str, Any] = {
            "path": path, "stored_size": len(content), "stored_sha256": _sha256(content),
            "case_id": "pack-self-description", "expected_outcome": "accept",
            "producer_entry": "traces.v5_conformance.validate_pack",
            "consumer_entry": "OpenGGF conformance resource manifest loader",
            "normalized_semantics": {"role": "documentation" if path.endswith(".md")
                                     else "manifest-schema"},
        }
    else:
        entry = {
            "path": path, "stored_size": len(content), "stored_sha256": _sha256(content),
            "case_id": case.identifier, "expected_outcome": case.expected_outcome,
            "producer_entry": PRODUCER_ENTRY, "consumer_entry": case.consumer_entry,
        }
        if case.expected_outcome == "accept":
            entry["normalized_semantics"] = case.normalized_semantics
        else:
            entry["exact_diagnostic"] = case.exact_diagnostic
            entry["consumer_exact_diagnostic"] = case.consumer_exact_diagnostic
    if path.endswith(".gz"):
        logical = gzip.decompress(content)
        entry["logical_size"] = len(logical)
        entry["logical_sha256"] = _sha256(logical)
    return entry


def _case_for_path(path: str, cases: list[Case]) -> Case | None:
    for case in cases:
        if path.startswith(f"fixtures/{case.identifier}/"):
            return case
    return None


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


def _gzip(content: bytes) -> bytes:
    target = io.BytesIO()
    with gzip.GzipFile(fileobj=target, mode="wb", filename="", mtime=0) as output:
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
        "required": ["format", "contract_version", "consumer_expectation_format",
                     "cases", "files"],
        "properties": {
            "format": {"const": MANIFEST_FORMAT},
            "contract_version": {"const": CONTRACT_VERSION},
            "consumer_expectation_format": {
                "const": "openggf-trace-v5-consumer-expectation-v1"},
            "cases": {"type": "array", "items": {"$ref": "#/$defs/case"}},
            "files": {"type": "array", "items": {"$ref": "#/$defs/file"}},
        },
        "$defs": {
            "outcome": {"enum": ["accept", "reject"]},
            "case": {"type": "object", "required": ["id", "root", "rules",
                "expected_outcome", "producer_entry", "consumer_entry"],
                "properties": {
                    "id": {"type": "string"}, "root": {"type": "string"},
                    "rules": {"type": "array", "items": {"type": "string"}},
                    "expected_outcome": {"$ref": "#/$defs/outcome"},
                    "producer_entry": {"type": "string"},
                    "consumer_entry": {"type": "string"},
                    "normalized_semantics": {"type": "object"},
                    "exact_diagnostic": {"type": "string"},
                    "consumer_exact_diagnostic": {"type": "string"},
                }},
            "file": {"type": "object", "required": ["path", "stored_size",
                "stored_sha256", "case_id", "expected_outcome", "producer_entry",
                "consumer_entry"], "properties": {
                    "path": {"type": "string"},
                    "stored_size": {"type": "integer", "minimum": 0},
                    "stored_sha256": {"type": "string", "pattern": "^[0-9a-f]{64}$"},
                    "logical_size": {"type": "integer", "minimum": 0},
                    "logical_sha256": {"type": "string", "pattern": "^[0-9a-f]{64}$"},
                    "case_id": {"type": "string"},
                    "expected_outcome": {"$ref": "#/$defs/outcome"},
                    "producer_entry": {"type": "string"},
                    "consumer_entry": {"type": "string"},
                    "normalized_semantics": {"type": "object"},
                    "exact_diagnostic": {"type": "string"},
                    "consumer_exact_diagnostic": {"type": "string"},
                }},
        },
    })


def _readme_bytes() -> bytes:
    return b"""# Trace-v5 semantic conformance pack

This bounded pack contains synthetic trace-schema-5 documents only. It contains
no ROM bytes, BK2 movie data, or copied canonical OpenGGF traces.

`manifest.json` pins every member's stored SHA-256 and byte length. Deterministic
gzip members additionally pin logical SHA-256 and length. Every case names the
TraceChaser producer entry, the real OpenGGF Java consumer entry for the Task 10
copy, and either normalized accepted semantics or exact producer and consumer
diagnostics. Array order in normalized semantics is significant.

Run `python3 traces/validate_v5_conformance.py` from the repository root. The
validator checks identities, rejects unmanifested members, runs every case
through `traces.validate_trace_v5.Validation`, and compares accepted values
instead of treating hashes as semantic proof.

Regenerate into an empty directory with
`python3 traces/generate_v5_conformance.py <directory>` and compare the result
byte-for-byte. Do not hand-edit generated members.
"""
