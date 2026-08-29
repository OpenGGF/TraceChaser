"""Executable contract for the portable trace-v5 semantic fixture pack."""

from __future__ import annotations

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
            else:
                self.assertTrue(case["exact_diagnostic"])
                self.assertTrue(case["consumer_exact_diagnostic"])

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

    def test_every_manifested_member_declares_identity_parser_and_semantic_outcome(self) -> None:
        manifest = json.loads((COMMITTED_PACK / "manifest.json").read_text())

        for entry in manifest["files"]:
            with self.subTest(path=entry["path"]):
                self.assertEqual(64, len(entry["stored_sha256"]))
                self.assertIsInstance(entry["stored_size"], int)
                self.assertTrue(entry["case_id"])
                self.assertTrue(entry["producer_entry"])
                self.assertTrue(entry["consumer_entry"])
                if entry["expected_outcome"] == "accept":
                    self.assertIsInstance(entry["normalized_semantics"], dict)
                else:
                    self.assertTrue(entry["exact_diagnostic"])
                    self.assertTrue(entry["consumer_exact_diagnostic"])
                if entry["path"].endswith(".gz"):
                    self.assertEqual(64, len(entry["logical_sha256"]))
                    self.assertIsInstance(entry["logical_size"], int)
                else:
                    self.assertNotIn("logical_sha256", entry)
                    self.assertNotIn("logical_size", entry)

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


if __name__ == "__main__":
    unittest.main()
