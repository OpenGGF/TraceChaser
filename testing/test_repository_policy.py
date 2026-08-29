import importlib.util
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
SCANNER = REPOSITORY_ROOT / "testing" / "repository_policy.py"


class RepositoryPolicyIntegrationTest(unittest.TestCase):
    def setUp(self):
        self._temporary_directory = tempfile.TemporaryDirectory()
        self.repository = Path(self._temporary_directory.name)
        self._git("init", "-q", "-b", "main")
        self._write("LICENSE", (REPOSITORY_ROOT / "LICENSE").read_bytes())
        notice = "bizhawk-headless/native/gpgx-audio-observer/notices/zstd-LICENSE"
        self._write(notice, (REPOSITORY_ROOT / notice).read_bytes())
        self._git("add", ".")

    def tearDown(self):
        self._temporary_directory.cleanup()

    def test_rejects_forbidden_tracked_artifact_paths(self):
        paths = {
            "roms/game.gen": b"synthetic ROM placeholder",
            "movies/run.bk2": b"synthetic movie placeholder",
            ".dependencies/BizHawk-2.11/EmuHawk.exe": b"synthetic emulator binary",
            "bizhawk-headless/bin/Debug/runner.dll": b"synthetic build output",
            "bizhawk-headless/obj/runner.o": b"synthetic object output",
            "testing/__pycache__/policy.pyc": b"synthetic cache",
            "captures/physics.csv": b"frame,x,y\n",
            "captures/session.log": b"synthetic log\n",
            "captures/diag_output.txt": b"synthetic output\n",
            "odd\nmovie.bk2": b"synthetic unusual-path movie",
        }
        for path, content in paths.items():
            self._write(path, content)
        self._git("add", ".")

        result = self._audit()

        self.assertEqual(1, result.returncode, result.stdout + result.stderr)
        lines = result.stdout.splitlines()
        self.assertEqual(sorted(lines), lines)
        for path in paths:
            displayed = path.replace("\n", r"\n")
            self.assertTrue(
                any(f"path={displayed} " in line for line in lines),
                f"missing violation for {displayed!r}:\n{result.stdout}",
            )
        self.assertEqual(lines, self._find_violations())

    def test_rejects_disguised_binary_content_and_altered_curated_notice(self):
        rom = bytearray(0x200)
        rom[0x100:0x104] = b"SEGA"
        machine_path = b"/" + b"home" + b"/example/private"
        self._write("contracts/disguised-rom.dat", bytes(rom))
        self._write("contracts/disguised-archive.dat", b"PK\x03\x04synthetic archive")
        self._write("scripts/local-default.py", b"root = '" + machine_path + b"'\n")
        notice = "bizhawk-headless/native/gpgx-audio-observer/notices/zstd-LICENSE"
        self._write(notice, b"altered notice\n")
        self._git("add", ".")

        result = self._audit()

        self.assertEqual(1, result.returncode, result.stdout + result.stderr)
        expected = {
            "contracts/disguised-rom.dat": "Mega Drive ROM magic",
            "contracts/disguised-archive.dat": "archive or BK2 magic",
            "scripts/local-default.py": "machine-local absolute path",
            notice: "unapproved license or notice content",
        }
        for path, reason in expected.items():
            self.assertIn(f"path={path} reason={reason}", result.stdout)

    def test_accepts_source_locks_small_contracts_and_exact_notices(self):
        safe_files = {
            "bizhawk/recorder.lua": b"return {}\n",
            "bizhawk-headless/src/Recorder.cs": b"sealed class Recorder {}\n",
            "traces/validate.py": b"print('validate')\n",
            "dependencies/bizhawk-2.11.lock.json": b'{"version":"2.11"}\n',
            "contracts/trace-v5/metadata.json": b'{"trace_schema":5}\n',
            "testing/fixtures/expected.txt": b"small curated contract\n",
        }
        for path, content in safe_files.items():
            self._write(path, content)
        self._git("add", ".")

        result = self._audit()

        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual("repository policy: PASS\n", result.stdout)
        self.assertEqual([], self._find_violations())

    def test_ignores_untracked_artifacts(self):
        self._write("traces/validate.py", b"print('tracked source')\n")
        self._git("add", ".")
        self._write("scratch/untracked.bk2", b"PK\x03\x04synthetic movie")

        result = self._audit()

        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual("repository policy: PASS\n", result.stdout)

    def _audit(self):
        return subprocess.run(
            [sys.executable, str(SCANNER), "--root", str(self.repository)],
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=False,
        )

    def _find_violations(self):
        if not SCANNER.is_file():
            self.fail(f"repository policy scanner is missing: {SCANNER}")
        spec = importlib.util.spec_from_file_location("repository_policy", SCANNER)
        module = importlib.util.module_from_spec(spec)
        assert spec.loader is not None
        spec.loader.exec_module(module)
        return module.find_violations(self.repository)

    def _write(self, relative_path, content):
        path = self.repository / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)

    def _git(self, *arguments):
        return subprocess.run(
            ["git", "-C", str(self.repository), *arguments],
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=True,
        )


if __name__ == "__main__":
    unittest.main()
