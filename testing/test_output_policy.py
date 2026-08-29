from __future__ import annotations

import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


class OutputPolicyTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory(prefix="trace chaser output policy ")
        self.root = Path(self.temporary.name)
        self.tracechaser = self.root / "Trace Chaser checkout"
        self.consumer = self.root / "Open GGF checkout"
        self.external = self.root / "external output"
        self.tracechaser.mkdir()
        self.consumer.mkdir()
        self.external.mkdir()

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def test_python_policy_rejects_both_roots_and_symlink_aliases(self) -> None:
        from traces.output_policy import require_external_output_root

        for output in (self.tracechaser / "out", self.consumer / "out"):
            with self.subTest(output=output), self.assertRaisesRegex(ValueError, "outside"):
                require_external_output_root(output, self.tracechaser, self.consumer)

        alias = self.root / "checkout alias"
        alias.symlink_to(self.tracechaser, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "outside"):
            require_external_output_root(alias / "out", self.tracechaser, self.consumer)

        self.assertEqual(
            self.external.resolve(),
            require_external_output_root(self.external, self.tracechaser, self.consumer),
        )

    def test_every_lua_recorder_invokes_behavioral_output_policy(self) -> None:
        recorders = (
            "s1_trace_recorder.lua", "s1_complete_run_recorder.lua",
            "s2_trace_recorder.lua", "s2_ss_trace_recorder.lua",
            "s3k_trace_recorder.lua", "s3k_complete_run_recorder.lua",
        )
        for recorder in recorders:
            with self.subTest(recorder=recorder):
                content = (ROOT / "bizhawk" / recorder).read_text(encoding="utf-8")
                self.assertIn("C.require_external_output_dir()", content)
                self.assertNotIn('or "trace_output/"', content)

        harness = (
            "local C=assert(loadfile(arg[0]))(); "
            "local ok,err=pcall(C.require_external_output_dir); "
            "if ok then os.exit(0) else io.stderr:write(err..'\\n'); os.exit(7) end"
        )
        environment = os.environ.copy()
        environment.update({
            "OGGF_TRACE_OUTPUT_DIR": str(self.tracechaser / "out"),
            "OGGF_TRACECHASER_ROOT": str(self.tracechaser),
            "OGGF_INPUT_REPOSITORY_ROOT": str(self.consumer),
        })
        result = subprocess.run(
            ["lua", "-e", harness, str(ROOT / "bizhawk/lib/oggf_trace_common.lua")],
            env=environment, text=True, capture_output=True, check=False,
        )
        self.assertEqual(7, result.returncode)
        self.assertIn("outside", result.stderr)

    def test_retro_entry_points_require_explicit_external_output_before_dependencies(self) -> None:
        for script in ("s1_trace_recorder.py", "s1_credits_trace_recorder.py"):
            with self.subTest(script=script):
                result = subprocess.run(
                    [sys.executable, str(ROOT / "retro" / script)],
                    text=True, capture_output=True, check=False,
                )
                self.assertEqual(2, result.returncode)
                self.assertIn("--output-dir", result.stderr)
                self.assertNotIn("stable-retro is not installed", result.stderr)

    def test_retro_entry_points_reject_producer_tree_output_before_dependencies(self) -> None:
        for script in ("s1_trace_recorder.py", "s1_credits_trace_recorder.py"):
            with self.subTest(script=script):
                result = subprocess.run(
                    [sys.executable, str(ROOT / "retro" / script),
                     "--input-repository-root", str(self.consumer),
                     "--output-dir", str(ROOT / "forbidden output")],
                    text=True, capture_output=True, check=False,
                )
                self.assertNotEqual(0, result.returncode)
                self.assertIn("outside", result.stderr)
                self.assertNotIn("stable-retro and numpy are required", result.stderr)

    def test_powershell_help_executes_from_spaced_path_without_inputs(self) -> None:
        result = subprocess.run(
            ["pwsh", "-NoProfile", "-NonInteractive", "-File",
             str(ROOT / "bizhawk/record_s2_level_select_traces.ps1"), "-Help"],
            text=True, capture_output=True, check=False,
        )
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("Usage:", result.stdout)

    def test_powershell_guard_rejects_spaced_symlink_alias(self) -> None:
        alias = self.root / "consumer alias with spaces"
        alias.symlink_to(self.consumer, target_is_directory=True)
        result = subprocess.run(
            ["pwsh", "-NoProfile", "-NonInteractive", "-File",
             str(ROOT / "bizhawk/assert_external_output.ps1"),
             "-TraceChaserRoot", str(self.tracechaser),
             "-InputRepositoryRoot", str(self.consumer),
             "-OutputRoot", str(alias / "capture")],
            text=True, capture_output=True, check=False,
        )
        self.assertNotEqual(0, result.returncode)
        self.assertIn("outside", result.stdout + result.stderr)

    def test_bash_launcher_rejects_alias_before_starting_bizhawk(self) -> None:
        alias = self.root / "tracechaser alias with spaces"
        alias.symlink_to(ROOT, target_is_directory=True)
        result = subprocess.run(
            [str(ROOT / "bizhawk/record_trace.sh"), str(self.consumer),
             str(self.consumer / "rom.gen"), str(self.consumer / "movie.bk2"),
             str(alias / "capture")],
            text=True, capture_output=True, check=False,
        )
        self.assertNotEqual(0, result.returncode)
        self.assertIn("outside", result.stdout + result.stderr)
        self.assertNotIn("BizHawk", result.stdout)


if __name__ == "__main__":
    unittest.main()
