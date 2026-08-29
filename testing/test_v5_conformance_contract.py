"""Executable contract for the portable trace-v5 semantic fixture pack."""

from __future__ import annotations

import base64
import gzip
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from traces.v5_conformance import build_pack, validate_pack


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
COMMITTED_PACK = REPOSITORY_ROOT / "contracts" / "v5"


class V5ConformanceContractTests(unittest.TestCase):
    """The pack catches semantic producer/consumer drift, not only byte drift."""

    def test_generator_reproduces_every_committed_member_byte_for_byte(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            generated = Path(temporary_directory) / "v5"

            build_pack(generated)

            self.assertEqual(self.snapshot(COMMITTED_PACK), self.snapshot(generated))

    def test_manifest_covers_required_rules_and_machine_readable_consumer_expectations(self) -> None:
        manifest = json.loads((COMMITTED_PACK / "manifest.json").read_text())
        cases = manifest["cases"]
        rules = {rule for case in cases for rule in case["rules"]}

        self.assertEqual(
            {
                "metadata-envelope",
                "ordinary-42-columns",
                "s1-special-14-columns",
                "s2-special-48-columns",
                "s3k-special-20-columns",
                "aux-jsonl",
                "hardware-kind",
                "hardware-boundary",
                "hardware-order",
                "hardware-ordinal",
                "hardware-frame-bound",
                "hardware-fingerprint",
                "removed-v5-field",
                "deterministic-gzip",
                "run-manifest-collections",
                "run-manifest-member-order",
            },
            rules,
        )
        for case in cases:
            self.assertIn(case["expected_outcome"], {"accept", "reject"})
            self.assertTrue(case["producer_entry"])
            self.assertTrue(case["consumer_entry"])
            if case["expected_outcome"] == "accept":
                self.assertIsInstance(case["normalized_semantics"], dict)
                self.assertNotEqual({}, case["normalized_semantics"])
                self.assertEqual(
                    {"outcome": "accept", "semantics_ref": "#/normalized_semantics"},
                    case["consumer_expectation"],
                )
            else:
                self.assertTrue(case["exact_diagnostic"])
                self.assertIsInstance(case["fault"], dict)
                self.assertIn(case["consumer_expectation"]["outcome"],
                              {"accept", "reject"})

        outcomes_by_rule = {
            rule: {
                case["expected_outcome"]
                for case in cases
                if rule in case["rules"]
            }
            for rule in rules
        }
        self.assertTrue(all(outcomes == {"accept", "reject"}
                            for outcomes in outcomes_by_rule.values()))

    def test_real_validator_executes_every_case_and_matches_declared_semantics(self) -> None:
        self.assertEqual([], validate_pack(COMMITTED_PACK))

    def test_reject_cases_bind_literal_faults_to_the_documents_that_carry_them(self) -> None:
        manifest = json.loads((COMMITTED_PACK / "manifest.json").read_text())
        cases = {case["id"]: case for case in manifest["cases"]}
        expected = {
            "reject-aux-missing-frame": ("aux-jsonl", "aux-missing-frame"),
            "reject-direct-boundary": ("hardware-boundary", "timing-value"),
            "reject-gzip-malformed": ("deterministic-gzip", "gzip-malformed"),
            "reject-gzip-nondeterministic": (
                "deterministic-gzip", "gzip-nondeterministic"),
            "reject-hardware-boundary": ("hardware-boundary", "timing-value"),
            "reject-hardware-fingerprint": ("hardware-fingerprint", "timing-value"),
            "reject-hardware-frame": ("hardware-frame-bound", "timing-value"),
            "reject-hardware-kind": ("hardware-kind", "timing-value"),
            "reject-hardware-order": ("hardware-order", "timing-order"),
            "reject-hardware-ordinal": ("hardware-ordinal", "timing-ordinal"),
            "reject-level-width": ("ordinary-42-columns", "csv-width"),
            "reject-removed-csv-version": ("removed-v5-field", "removed-field"),
            "reject-removed-hardware-timing-schema": (
                "removed-v5-field", "removed-field"),
            "reject-removed-lua-script-version": (
                "removed-v5-field", "removed-field"),
            "reject-removed-run-schema": ("removed-v5-field", "removed-field"),
            "reject-removed-ss-csv-version": (
                "removed-v5-field", "removed-field"),
            "reject-run-gap-collections": (
                "run-manifest-collections", "run-collection-shape"),
            "reject-run-segment-order": (
                "run-manifest-member-order", "run-segment-order"),
            "reject-run-segments-collections": (
                "run-manifest-collections", "run-collection-empty"),
            "reject-run-transition-order": (
                "run-manifest-member-order", "run-transition-order"),
            "reject-run-transitions-collections": (
                "run-manifest-collections", "run-collection-shape"),
            "reject-s1-special-width": ("s1-special-14-columns", "csv-width"),
            "reject-s2-special-width": ("s2-special-48-columns", "csv-width"),
            "reject-s3k-special-width": ("s3k-special-20-columns", "csv-width"),
            "reject-schema": ("metadata-envelope", "metadata-value"),
        }

        self.assertEqual(set(expected), {
            case["id"] for case in manifest["cases"]
            if case["expected_outcome"] == "reject"
        })
        for case_id, (rule, kind) in expected.items():
            with self.subTest(case=case_id):
                case = cases[case_id]
                self.assertEqual([rule], case["rules"])
                self.assertEqual(rule, case["fault"]["rule"])
                self.assertEqual(kind, case["fault"]["kind"])
                self.assert_fault_is_present(case)

    def test_java_consumer_diagnostics_name_reachable_authority_paths(self) -> None:
        manifest = json.loads((COMMITTED_PACK / "manifest.json").read_text())
        cases = {case["id"]: case for case in manifest["cases"]}

        self.assertEqual(
            {
                "outcome": "reject",
                "diagnostic": {
                    "exception_class": "java.io.IOException",
                    "message_match": "exact",
                    "message": "hardware_timing.jsonl: raw_frame 1 is outside [0, 1)",
                },
            },
            cases["reject-hardware-frame"]["consumer_expectation"],
        )
        segments = json.loads(
            (COMMITTED_PACK / cases["reject-run-segments-collections"]["root"]
             / "run_manifest.json").read_text()
        )["segments"]
        self.assertEqual([], segments)
        self.assertEqual(
            {
                "outcome": "reject",
                "diagnostic": {
                    "exception_class": "java.lang.IllegalStateException",
                    "message_match": "exact",
                    "message": "Manifest has no segments",
                },
            },
            cases["reject-run-segments-collections"]["consumer_expectation"],
        )

    def test_manifest_authority_rejects_every_reviewed_tamper_class(self) -> None:
        mutations = (
            "consumer-format", "case-root", "case-parser", "case-diagnostic",
            "case-outcome", "file-link", "file-parser", "file-outcome",
            "file-expectation-link", "missing-case-field", "missing-file-field",
            "duplicate-case", "reordered-case", "duplicate-file", "reordered-file",
            "stored-identity", "logical-identity", "unexpected-top-level-field",
        )
        for mutation in mutations:
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as temporary:
                copied = Path(temporary) / "v5"
                shutil.copytree(COMMITTED_PACK, copied)
                manifest_path = copied / "manifest.json"
                manifest = json.loads(manifest_path.read_text())
                self.apply_manifest_mutation(manifest, mutation)
                manifest_path.write_text(json.dumps(
                    manifest, sort_keys=True, separators=(",", ":")) + "\n")

                errors = validate_pack(copied)

                self.assertTrue(errors, f"tamper was accepted: {mutation}")
                self.assertTrue(any(error.startswith("manifest.json:")
                                    or "identity mismatch" in error.lower()
                                    for error in errors), errors)

    def test_special_stage_expectations_publish_every_java_record_field(self) -> None:
        manifest = json.loads((COMMITTED_PACK / "manifest.json").read_text())
        cases = {case["id"]: case for case in manifest["cases"]}
        expected_frames = {
            "accept-s1-special": {
                "frame": 10, "input": 42, "lag": True,
                "x_pos": 2147483649, "y_pos": 2147483646,
                "vel_x": 65520, "vel_y": 17, "inertia": 34,
                "status": 51, "ss_angle": 68, "ss_rotate": 85,
                "bg_anim": 102, "rings": 119, "emeralds": 8,
            },
            "accept-s2-special": {
                "frame": 10, "input": 16, "input_p2": 17, "lag": True,
                "speed_factor": 18, "track_anim": 19, "track_anim_frame": 20,
                "track_drawing_index": 21, "track_orientation": 22,
                "track_duration_timer": 23, "current_segment": 24,
                "player_anim_frame_timer": 25, "rings_togo_bcd": 26,
                "check_rings_flag": 27, "tails_control_counter": 28,
                "swap_positions_flag": 29,
                "sonic": {
                    "present": True, "ss_x": 31, "ss_x_sub": 32, "ss_y": 33,
                    "ss_y_sub": 34, "ss_z": 35, "angle": 36, "routine": 37,
                    "routine_secondary": 38, "status": 39, "anim": 40,
                    "anim_frame": 41, "rings_bcd": 42, "hurt_timer": 43,
                    "slide_timer": 44, "flip_timer": 45,
                },
                "tails": {
                    "present": False, "ss_x": 47, "ss_x_sub": 48, "ss_y": 49,
                    "ss_y_sub": 50, "ss_z": 51, "angle": 52, "routine": 53,
                    "routine_secondary": 54, "status": 55, "anim": 56,
                    "anim_frame": 57, "rings_bcd": 58, "hurt_timer": 59,
                    "slide_timer": 60, "flip_timer": 61,
                },
            },
            "accept-s3k-special": {
                "frame": 10, "input": 16, "input_p2": 17, "lag": False,
                "anim_frame": 18, "x_pos": 19, "y_pos": 20, "angle": 21,
                "velocity": 22, "turning": 23, "jumping": 24,
                "fade_timer": 25, "spheres_left": 26, "ring_count": 27,
                "rings_left": 28, "rate": 29, "rate_timer": 30,
                "clear_timer": 31, "clear_routine": 32, "started": True,
            },
        }
        for case_id, expected in expected_frames.items():
            with self.subTest(case=case_id):
                self.assertEqual(
                    expected,
                    cases[case_id]["normalized_semantics"]["frames"][0],
                )

    def test_metadata_and_run_expectations_cover_declared_java_contract_fields(self) -> None:
        manifest = json.loads((COMMITTED_PACK / "manifest.json").read_text())
        cases = {case["id"]: case for case in manifest["cases"]}
        metadata = cases["accept-level"]["normalized_semantics"]["metadata"]
        self.assertEqual(
            {
                "game", "zone", "zone_id", "act", "bk2_frame_offset",
                "ring_floor_check_counter_phase", "trace_frame_count",
                "start_x_hex", "start_y_hex", "start_x", "start_y",
                "recording_date", "recorder", "recorder_version", "trace_schema",
                "trace_profile", "bizhawk_version", "genesis_core",
                "aux_schema_extras", "rom_zone_id", "route", "source_bk2",
                "rom_checksum", "notes", "characters", "main_character",
                "sidekicks", "pre_trace_osc_frames", "rng_seed_hex",
                "initial_rng_seed", "trace_type", "input_source",
                "credits_demo_index", "credits_demo_slug", "special_stage_index",
                "run_id", "segment_index", "bonus_stage_type", "fresh_load",
                "v_int_run_count",
            },
            set(metadata),
        )
        run = cases["accept-run-manifest"]["normalized_semantics"]
        self.assertEqual(
            {"trace_schema", "game", "run_id", "source_bk2", "rom_checksum",
             "expected_movie_end_mode", "segments", "transitions",
             "dynamic_art_gap_transitions", "member_order"},
            set(run),
        )
        self.assertEqual(3, len(run["segments"]))
        self.assertEqual(2, len(run["transitions"]))
        self.assertEqual(
            {"dir", "kind", "trace_profile", "bk2_frame_offset",
             "trace_frame_count", "zone_id", "act", "special_stage_index",
             "bonus_stage_type", "dynamic_art_initial_ledger_descriptors",
             "dynamic_art_initial_ledger_fingerprint"},
            set(run["segments"][0]),
        )
        self.assertEqual(
            {"from_segment", "to_segment", "entry_kind",
             "mode_change_bk2_frame", "special_bonus_entry_flag", "saved_x_pos",
             "saved_y_pos", "last_star_post_hit", "rings_before", "rings_after",
             "emeralds_before", "emeralds_after", "gap_admission_runs"},
            set(run["transitions"][0]),
        )

    def test_every_manifested_member_declares_identity_parser_and_semantic_outcome(self) -> None:
        manifest = json.loads((COMMITTED_PACK / "manifest.json").read_text())

        for entry in manifest["files"]:
            with self.subTest(path=entry["path"]):
                self.assertEqual(64, len(entry["stored_sha256"]))
                self.assertIsInstance(entry["stored_size"], int)
                self.assertTrue(entry["case_id"])
                self.assertTrue(entry["parser_ref"])
                self.assertTrue(entry["expectation_ref"])
                if entry["path"].endswith(".gz"):
                    self.assertEqual(64, len(entry["logical_sha256"]))
                    self.assertIsInstance(entry["logical_size"], int)
                else:
                    self.assertNotIn("logical_sha256", entry)
                    self.assertNotIn("logical_size", entry)

                if entry["case_id"] == "pack-self-description":
                    self.assertEqual("pack-self-description", entry["parser_ref"])
                    self.assertEqual("pack-self-description", entry["expectation_ref"])
                else:
                    case_index = int(entry["parser_ref"].removeprefix("#/cases/"))
                    self.assertEqual(entry["case_id"], manifest["cases"][case_index]["id"])
                    self.assertEqual(entry["parser_ref"], entry["expectation_ref"])

    def test_validator_rejects_an_unmanifested_file(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            copied = Path(temporary_directory) / "v5"
            shutil.copytree(COMMITTED_PACK, copied)
            extra = copied / "fixtures" / "accept-level" / "unmanifested.json"
            extra.write_text("{}\n")

            errors = validate_pack(copied)

        self.assertIn("unmanifested file: fixtures/accept-level/unmanifested.json", errors)

    def test_script_entry_points_run_from_the_repository_root(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            destination = Path(temporary_directory) / "v5"
            generated = subprocess.run(
                [sys.executable, "traces/generate_v5_conformance.py", str(destination)],
                cwd=REPOSITORY_ROOT, text=True, capture_output=True, check=False)

            self.assertEqual(0, generated.returncode, generated.stderr)
            validated = subprocess.run(
                [sys.executable, "traces/validate_v5_conformance.py", str(destination)],
                cwd=REPOSITORY_ROOT, text=True, capture_output=True, check=False)

        self.assertEqual(0, validated.returncode, validated.stdout + validated.stderr)
        self.assertIn("trace v5 conformance: PASS", validated.stdout)

    @staticmethod
    def snapshot(root: Path) -> dict[str, bytes]:
        return {
            path.relative_to(root).as_posix(): path.read_bytes()
            for path in sorted(root.rglob("*"))
            if path.is_file()
        }

    def assert_fault_is_present(self, case: dict[str, object]) -> None:
        root = COMMITTED_PACK / str(case["root"])
        fault = case["fault"]
        kind = fault["kind"]
        if kind in {"metadata-value", "removed-field"}:
            metadata = json.loads((root / "metadata.json").read_text())
            self.assertEqual(fault["value"], metadata[fault["field"]])
        elif kind == "csv-width":
            row = (root / "physics.csv").read_text().splitlines()[1]
            self.assertEqual(fault["actual_columns"], len(row.split(",")))
        elif kind == "aux-missing-frame":
            event = json.loads((root / "aux_state.jsonl").read_text())
            self.assertNotIn("frame", event)
        elif kind in {"timing-value", "timing-order", "timing-ordinal"}:
            events = [json.loads(line) for line in
                      (root / "hardware_timing.jsonl").read_text().splitlines()]
            if kind == "timing-value":
                self.assertEqual(fault["value"], events[fault["event_index"]][fault["field"]])
            elif kind == "timing-order":
                self.assertEqual(["pre_main_loop", "post_objects"],
                                 [event["boundary"] for event in events])
            else:
                self.assertGreater(events[0]["ordinal"], events[1]["ordinal"])
        elif kind.startswith("gzip-"):
            payload = json.loads((root / fault["source"]).read_text())
            content = base64.b64decode(payload["base64"])
            if kind == "gzip-malformed":
                with self.assertRaises((gzip.BadGzipFile, EOFError)):
                    gzip.decompress(content)
            else:
                self.assertEqual(b"\x01\x00\x00\x00", content[4:8])
                self.assertGreater(len(gzip.decompress(content)), 0)
        elif kind.startswith("run-collection"):
            run = json.loads((root / "run_manifest.json").read_text())
            value = run[fault["field"]]
            if kind == "run-collection-empty":
                self.assertEqual([], value)
            else:
                self.assertIsInstance(value, dict)
        elif kind == "run-segment-order":
            run = json.loads((root / "run_manifest.json").read_text())
            offsets = [segment["bk2_frame_offset"] for segment in run["segments"]]
            self.assertGreater(offsets[0], offsets[1])
        elif kind == "run-transition-order":
            run = json.loads((root / "run_manifest.json").read_text())
            members = [transition["from_segment"] for transition in run["transitions"]]
            self.assertGreater(members[0], members[1])
        else:
            self.fail(f"unasserted fault kind: {kind}")

    @staticmethod
    def apply_manifest_mutation(manifest: dict[str, object], mutation: str) -> None:
        cases = manifest["cases"]
        files = manifest["files"]
        reject = next(case for case in cases if case["id"] == "reject-hardware-frame")
        accepted_file = next(entry for entry in files
                             if entry["path"] == "fixtures/accept-level/physics.csv")
        gzip_file = next(entry for entry in files if entry["path"].endswith(".gz"))
        if mutation == "consumer-format":
            manifest["consumer_expectation_format"] = "invented"
        elif mutation == "case-root":
            reject["root"] = "fixtures/accept-level"
        elif mutation == "case-parser":
            reject["consumer_entry"] = "com.example.DoesNotExist.load"
        elif mutation == "case-diagnostic":
            reject["consumer_expectation"]["diagnostic"]["message"] = "invented"
        elif mutation == "case-outcome":
            reject["expected_outcome"] = "accept"
        elif mutation == "file-link":
            accepted_file["case_id"] = "reject-schema"
        elif mutation == "file-parser":
            accepted_file["parser_ref"] = "#/cases/999"
        elif mutation == "file-outcome":
            accepted_file["expected_outcome"] = "reject"
        elif mutation == "file-expectation-link":
            accepted_file["expectation_ref"] = "#/cases/999"
        elif mutation == "missing-case-field":
            reject.pop("consumer_expectation")
        elif mutation == "missing-file-field":
            accepted_file.pop("parser_ref")
        elif mutation == "duplicate-case":
            cases.append(dict(cases[0]))
        elif mutation == "reordered-case":
            cases[0], cases[1] = cases[1], cases[0]
        elif mutation == "duplicate-file":
            files.append(dict(files[0]))
        elif mutation == "reordered-file":
            files[0], files[1] = files[1], files[0]
        elif mutation == "stored-identity":
            accepted_file["stored_sha256"] = "0" * 64
        elif mutation == "logical-identity":
            gzip_file["logical_sha256"] = "0" * 64
        elif mutation == "unexpected-top-level-field":
            manifest["invented"] = True
        else:
            raise AssertionError(mutation)


if __name__ == "__main__":
    unittest.main()
