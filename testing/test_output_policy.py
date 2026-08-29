from __future__ import annotations

import os
import shutil
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

    def test_direct_lua_policy_canonicalizes_literal_safe_and_spaced_paths(self) -> None:
        quoted = self.root / "external 'quoted' output"
        quoted.mkdir()
        for output in (self.external, quoted):
            with self.subTest(output=output):
                result = self._run_lua_policy(output)
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertEqual(str(output.resolve()) + "/\n", result.stdout)

    def test_direct_lua_policy_rejects_protected_literal_proc_alias_and_forged_sentinel(self) -> None:
        for output in (
            ROOT / "forbidden",
            Path("/proc/self/root" + str(ROOT / "forbidden")),
            self.consumer / "forbidden",
        ):
            with self.subTest(output=output):
                result = self._run_lua_policy(
                    output,
                    extra={"OGGF_OUTPUT_BOUNDARY_VALIDATED":
                           "tracechaser-output-policy-v1:" + str(output)},
                )
                self.assertEqual(7, result.returncode)
                self.assertIn("outside", result.stderr)

    def test_direct_lua_policy_fails_closed_for_missing_interpreter_roots_and_helper(self) -> None:
        cases = (
            ({"OGGF_PYTHON_PATH": None}, "interpreter"),
            ({"OGGF_PYTHON_PATH": "python3"}, "absolute"),
            ({"OGGF_PYTHON_PATH": "/definitely/missing/python3"}, "missing"),
            ({"OGGF_TRACECHASER_ROOT": None}, "TraceChaser root"),
            ({"OGGF_TRACECHASER_ROOT": str(self.tracechaser)}, "does not own"),
            ({"OGGF_INPUT_REPOSITORY_ROOT": None}, "consumer"),
            ({"OGGF_INPUT_REPOSITORY_ROOT": str(self.root / "missing consumer")}, "existing"),
        )
        for changes, message in cases:
            with self.subTest(changes=changes):
                result = self._run_lua_policy(self.external, extra=changes)
                self.assertEqual(7, result.returncode)
                self.assertIn(message, result.stderr)

        fake_root = self.root / "helper missing checkout"
        module = fake_root / "bizhawk/lib/oggf_trace_common.lua"
        module.parent.mkdir(parents=True)
        shutil.copyfile(ROOT / "bizhawk/lib/oggf_trace_common.lua", module)
        result = self._run_lua_policy(
            self.external, module=module,
            extra={"OGGF_TRACECHASER_ROOT": str(fake_root)},
        )
        self.assertEqual(7, result.returncode)
        self.assertIn("path-policy helper", result.stderr)

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

    def test_native_and_audio_launchers_reject_proc_root_consumer_aliases(self) -> None:
        proc_alias = Path("/proc/self/root" + str(self.consumer / "capture"))
        fixture = self.consumer / "fixtures"
        fixture.mkdir()
        native = subprocess.run([
            str(ROOT / "bizhawk-headless/run.sh"),
            "--tracechaser-root", str(ROOT), "--input-repository-root", str(self.consumer),
            "--fixture-root", str(fixture), "--mode", "trace", "--rom", "/missing.rom",
            "--movie", "/missing.bk2", "--output", str(proc_alias),
        ], text=True, capture_output=True, check=False)
        self.assertNotEqual(0, native.returncode)
        self.assertIn("outside", native.stdout + native.stderr)
        self.assertNotIn("BIZHAWK_HOME", native.stdout + native.stderr)

        source_map = self.external / "map.tsv"
        input_file = self.external / "input.tsv"
        source_map.write_text("header\n")
        input_file.write_text("header\n")
        ledger = subprocess.run(["bash",
            str(ROOT / "bizhawk-headless/native/gpgx-audio-lab/build-representative-ledger.sh"),
            "s1", str(source_map), str(input_file), str(self.consumer), str(proc_alias),
        ], text=True, capture_output=True, check=False)
        self.assertNotEqual(0, ledger.returncode)
        self.assertIn("outside", ledger.stdout + ledger.stderr)

        capture = subprocess.run(["bash",
            str(ROOT / "bizhawk-headless/native/gpgx-audio-lab/capture-ym-write-timing.sh"),
            "--game", "s1", "--sound-id", "0xB5", "--fm-channel", "4",
            "--input-repository-root", str(self.consumer), "--output", str(proc_alias),
        ], text=True, capture_output=True, check=False)
        self.assertNotEqual(0, capture.returncode)
        self.assertIn("outside", capture.stdout + capture.stderr)

    def _run_lua_policy(
        self, output: Path, *, module: Path | None = None,
        extra: dict[str, str | None] | None = None,
    ) -> subprocess.CompletedProcess[str]:
        harness = (
            "local C=assert(loadfile(arg[0]))(); "
            "local ok,value=pcall(C.require_external_output_dir); "
            "if ok then io.write(value..'\\n'); os.exit(0) "
            "else io.stderr:write(value..'\\n'); os.exit(7) end"
        )
        environment = os.environ.copy()
        environment.update({
            "OGGF_TRACE_OUTPUT_DIR": str(output),
            "OGGF_TRACECHASER_ROOT": str(ROOT),
            "OGGF_INPUT_REPOSITORY_ROOT": str(self.consumer),
            "OGGF_PYTHON_PATH": sys.executable,
        })
        for name, value in (extra or {}).items():
            if value is None:
                environment.pop(name, None)
            else:
                environment[name] = value
        return subprocess.run(
            ["lua", "-e", harness,
             str(module or ROOT / "bizhawk/lib/oggf_trace_common.lua")],
            env=environment, text=True, capture_output=True, check=False,
        )


if __name__ == "__main__":
    unittest.main()
