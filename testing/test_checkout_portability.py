"""Standalone-checkout contracts for the filtered TraceChaser utilities."""

from __future__ import annotations

import ast
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from testing.bizhawk_runtime_fixture import write_install, write_monodis


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]


class CheckoutPortabilityTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory(
            prefix="tracechaser-portability-"
        )
        self.root = Path(self.temporary_directory.name)
        self.checkout = self.root / "TraceChaser checkout with spaces"
        shutil.copytree(
            REPOSITORY_ROOT,
            self.checkout,
            ignore=shutil.ignore_patterns(".git", "__pycache__", "bin", "obj", ".scratch"),
        )

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def test_internal_python_imports_and_public_entry_point_work_from_spaced_checkout(self) -> None:
        modules = (
            "traces.compare_trace_v5_candidates",
            "traces.s1_credits_raw_evidence",
            "traces.build_s1_credits_raw_host_evidence",
            "traces.verify_s1_credits_raw_host_evidence",
            "traces.trace_v5_capture_matrix",
        )
        command = [
            sys.executable,
            "-B",
            "-c",
            "import sys; sys.path.insert(0, sys.argv[1]); "
            + "; ".join(f"import {module}" for module in modules),
            str(self.checkout),
        ]
        imported = subprocess.run(command, cwd=self.root, text=True, capture_output=True)
        self.assertEqual(0, imported.returncode, imported.stderr)

        for entry_point in (
            self.checkout / "traces" / "trace_v5_capture_matrix.py",
            self.checkout / "bizhawk-headless" / "trace_v5_capture_matrix.py",
        ):
            with self.subTest(entry_point=entry_point):
                result = subprocess.run(
                    [sys.executable, "-B", str(entry_point), "--help"],
                    cwd=self.root,
                    text=True,
                    capture_output=True,
                )
                self.assertEqual(0, result.returncode, result.stderr)

    def test_python_sources_have_no_unresolved_tools_package_imports(self) -> None:
        violations = []
        for path in sorted(self.checkout.rglob("*.py")):
            tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
            for node in ast.walk(tree):
                imported = None
                if isinstance(node, ast.ImportFrom):
                    imported = node.module
                elif isinstance(node, ast.Import):
                    imported = ",".join(alias.name for alias in node.names)
                if imported and any(name.startswith("tools.") for name in imported.split(",")):
                    violations.append(f"{path.relative_to(self.checkout)}:{node.lineno}: {imported}")
        self.assertEqual([], violations)

    def test_capture_matrix_expands_only_explicit_external_inputs_without_source_writes(self) -> None:
        consumer_root = self.root / "OpenGGF inputs with spaces"
        fixture_root = consumer_root / "fixtures"
        movie_root = consumer_root / "movies"
        fixture_root.mkdir(parents=True)
        movie_root.mkdir()
        scratch_root = self.root / "durable scratch with spaces"
        scratch_root.mkdir()
        roms = {}
        for game in ("s1", "s2", "s3k"):
            roms[game] = self.root / f"{game} explicit rom.gen"
            roms[game].write_bytes(game.encode("ascii"))

        ids = (
            ("s1-ghz1", "s1"),
            ("s1-emeralds-run", "s1"),
            ("s2-ehz1", "s2"),
            ("s2-emeralds-run", "s2"),
            ("s3k-aiz", "s3k"),
            ("s3k-complete", "s3k"),
        )
        rows = []
        for identifier, game in ids:
            movie = f"{game}/{identifier}.bk2"
            (movie_root / game).mkdir(exist_ok=True)
            (movie_root / movie).write_bytes(identifier.encode("ascii"))
            rows.append({
                "id": identifier,
                "game": game,
                "movie": movie,
                "movie_sha256": "a" * 64,
                "selectors": [],
                "mappings": [],
            })
        document = {
            "format": "openggf-tracechaser-extraction-capture-matrix-v1",
            "capture": {"mode": "trace", "output_template": "captures/{id}"},
            "freeze": {},
            "roms": {game: {} for game in roms},
            "rows": rows,
        }
        matrix = consumer_root / "capture matrix.json"
        matrix.write_text(json.dumps(document), encoding="utf-8")
        ledger = scratch_root / "commands ledger.txt"
        before_checkout = self.snapshot(self.checkout)
        before_consumer = self.snapshot(consumer_root)

        result = subprocess.run(
            [
                sys.executable,
                "-B",
                str(self.checkout / "bizhawk-headless" / "trace_v5_capture_matrix.py"),
                "expand",
                "--matrix", str(matrix),
                "--input-repository-root", str(consumer_root),
                "--fixture-root", str(fixture_root),
                "--movie-root", str(movie_root),
                "--batch-root", str(scratch_root),
                "--s1-rom", str(roms["s1"]),
                "--s2-rom", str(roms["s2"]),
                "--s3k-rom", str(roms["s3k"]),
                "--output", str(ledger),
            ],
            cwd=self.root,
            text=True,
            capture_output=True,
        )

        self.assertEqual(0, result.returncode, result.stderr)
        commands = ledger.read_text(encoding="utf-8").splitlines()
        self.assertEqual(6, len(commands))
        self.assertTrue(all(str(self.checkout / "bizhawk-headless" / "run.sh") in line for line in commands))
        self.assertTrue(all(str(movie_root) in line for line in commands))
        for rom in roms.values():
            self.assertTrue(any(str(rom) in line for line in commands))
        self.assertEqual(before_checkout, self.snapshot(self.checkout))
        self.assertEqual(before_consumer, self.snapshot(consumer_root))

    def test_scratch_roots_inside_either_source_tree_are_rejected(self) -> None:
        consumer_root = self.root / "consumer repository"
        consumer_root.mkdir()
        external = self.root / "external scratch"
        external.mkdir()
        program = (
            "import sys; from pathlib import Path; "
            "sys.path.insert(0, sys.argv[1]); "
            "from traces.trace_v5_capture_matrix import require_external_scratch; "
            "tracechaser, consumer, external = map(Path, sys.argv[1:4]); "
            "require_external_scratch(tracechaser, consumer, external); "
            "\nfor unsafe in (tracechaser / 'scratch', consumer / 'target' / 'capture'):\n"
            " try: require_external_scratch(tracechaser, consumer, unsafe)\n"
            " except ValueError as error:\n"
            "  assert 'outside both repositories' in str(error)\n"
            " else: raise AssertionError(f'accepted unsafe scratch: {unsafe}')\n"
        )
        result = subprocess.run(
            [sys.executable, "-B", "-c", program, str(self.checkout),
             str(consumer_root), str(external)],
            cwd=self.root,
            text=True,
            capture_output=True,
        )
        self.assertEqual(0, result.returncode, result.stderr)

    def test_native_default_bizhawk_home_is_checkout_local_and_space_safe(self) -> None:
        dependency = self.checkout / ".dependencies" / "BizHawk-2.11-linux-x64"
        lock = json.loads(
            (
                self.checkout
                / "dependencies"
                / "bizhawk-2.11-linux-x64.lock.json"
            ).read_text(encoding="utf-8")
        )
        write_install(dependency, lock)
        fake_bin = self.root / "fake tool bin"
        fake_bin.mkdir()
        for command in ("mono", "xbuild"):
            path = fake_bin / command
            path.write_text("#!/usr/bin/env bash\nexit 0\n", encoding="utf-8")
            path.chmod(0o755)
        write_monodis(fake_bin / "monodis", lock)
        environment = os.environ.copy()
        environment.pop("BIZHAWK_HOME", None)
        environment["PATH"] = f"{fake_bin}:{environment['PATH']}"
        result = subprocess.run(
            ["bash", "-c", 'source "$1"; printf "%s" "$BIZHAWK_HOME"', "bash", str(self.checkout / "bizhawk-headless" / "common-env.sh")],
            cwd=self.root,
            env=environment,
            text=True,
            capture_output=True,
        )
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(str(dependency), result.stdout)

    def test_all_shell_and_powershell_sources_parse_from_spaced_checkout(self) -> None:
        for path in sorted(self.checkout.rglob("*.sh")):
            with self.subTest(shell=path):
                result = subprocess.run(["bash", "-n", str(path)], cwd=self.root, capture_output=True, text=True)
                self.assertEqual(0, result.returncode, result.stderr)
        pwsh = shutil.which("pwsh")
        if pwsh is None:
            self.skipTest("pwsh is unavailable")
        parser = (
            "$tokens=$null; $errors=$null; "
            "[System.Management.Automation.Language.Parser]::ParseFile($env:PORTABILITY_SCRIPT,[ref]$tokens,[ref]$errors)|Out-Null; "
            "if ($errors.Count -ne 0) { $errors | ForEach-Object { Write-Error $_ }; exit 1 }"
        )
        for path in sorted(self.checkout.rglob("*.ps1")):
            with self.subTest(powershell=path):
                environment = os.environ.copy()
                environment["PORTABILITY_SCRIPT"] = str(path)
                result = subprocess.run(
                    [pwsh, "-NoProfile", "-Command", parser],
                    cwd=self.root,
                    env=environment,
                    capture_output=True,
                    text=True,
                )
                self.assertEqual(0, result.returncode, result.stderr)

    @staticmethod
    def snapshot(root: Path) -> dict[str, bytes]:
        return {
            path.relative_to(root).as_posix(): path.read_bytes()
            for path in root.rglob("*")
            if path.is_file()
        }


if __name__ == "__main__":
    unittest.main()
