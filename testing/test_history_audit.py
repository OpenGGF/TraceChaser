import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
SCANNER = REPOSITORY_ROOT / "testing" / "history_audit.py"


class HistoryAuditIntegrationTest(unittest.TestCase):
    def setUp(self):
        self._temporary_directory = tempfile.TemporaryDirectory()
        self.repository = Path(self._temporary_directory.name)
        self._git("init", "-q", "-b", "main")
        self._git("config", "user.name", "TraceChaser policy test")
        self._git("config", "user.email", "policy-test@example.invalid")
        self._write("LICENSE", (REPOSITORY_ROOT / "LICENSE").read_bytes())
        self._write(
            "bizhawk-headless/native/gpgx-audio-observer/notices/zstd-LICENSE",
            (
                REPOSITORY_ROOT
                / "bizhawk-headless/native/gpgx-audio-observer/notices/zstd-LICENSE"
            ).read_bytes(),
        )
        self._commit("add exact license exceptions")

    def tearDown(self):
        self._temporary_directory.cleanup()

    def test_clean_source_and_exact_notices_pass(self):
        self._write("traces/validator.py", b"print('source only')\n")
        self._write("fixtures/opaque.dat", b"\xff\x00source evidence")
        self._commit("add safe source")

        result = self._audit()

        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual("history audit: PASS\n", result.stdout)

    def test_deleted_bk2_is_reported_with_commit_object_and_path(self):
        self._write("scratch/movie.bk2", b"PK\x03\x04synthetic movie")
        introducing_commit = self._commit("add prohibited movie")
        object_id = self._git("rev-parse", "HEAD:scratch/movie.bk2").stdout.strip()
        os.remove(self.repository / "scratch/movie.bk2")
        self._git("add", "-u")
        self._git("commit", "-q", "-m", "remove prohibited movie")

        result = self._audit()

        self.assertEqual(1, result.returncode)
        self.assertIn(f"commit={introducing_commit}", result.stdout)
        self.assertIn(f"object={object_id}", result.stdout)
        self.assertIn("path=scratch/movie.bk2", result.stdout)
        self.assertIn("reason=forbidden suffix .bk2", result.stdout)

    def test_modified_exact_notice_is_rejected(self):
        notice = "bizhawk-headless/native/gpgx-audio-observer/notices/zstd-LICENSE"
        self._write(notice, b"substituted notice\n")
        commit = self._commit("substitute notice")
        object_id = self._git("rev-parse", f"HEAD:{notice}").stdout.strip()

        result = self._audit()

        self.assertEqual(1, result.returncode)
        self.assertIn(f"commit={commit}", result.stdout)
        self.assertIn(f"object={object_id}", result.stdout)
        self.assertIn(f"path={notice}", result.stdout)
        self.assertIn("reason=unapproved license or notice content", result.stdout)

    def test_forbidden_paths_and_nonexception_notice_are_rejected(self):
        paths = {
            "roms/game.gen": b"not even a real rom",
            "movies/run.bk2": b"not even a real movie",
            "distribution/EmuHawk.dll": b"not even a real dll",
            "native/obj/output.o": b"object output",
            "captures/session.log": b"uncurated log",
            "captures/diag_output.txt": b"uncurated output",
            "captures/physics.csv.gz": b"raw trace payload",
            "vendor/notices/zstd-LICENSE": b"misplaced notice",
        }
        for path, content in paths.items():
            self._write(path, content)
        self._commit("add prohibited paths")

        result = self._audit()

        self.assertEqual(1, result.returncode)
        for path in paths:
            self.assertIn(f"path={path}", result.stdout)

    def test_binary_magic_oversize_and_machine_paths_are_rejected(self):
        rom = bytearray(0x200)
        rom[0x100:0x104] = b"SEGA"
        tar = bytearray(300)
        tar[257:262] = b"ustar"
        self._write("fixtures/archive.dat", b"PK\x03\x04synthetic archive")
        self._write("fixtures/rom.dat", bytes(rom))
        self._write("fixtures/tar.dat", bytes(tar))
        self._write("fixtures/oversize.dat", b"x" * (1024 * 1024 + 1))
        unix_path = b"/" + b"home" + b"/example/project"
        windows_path = b"C:" + b"\\" + b"Users" + b"\\example\\project"
        self._write("scripts/unix.py", b"root = '" + unix_path + b"'\n")
        self._write("scripts/windows.py", b"root = r'" + windows_path + b"'\n")
        self._commit("add prohibited blob content")

        result = self._audit()

        self.assertEqual(1, result.returncode)
        expected = {
            "fixtures/archive.dat": "archive or BK2 magic",
            "fixtures/rom.dat": "Mega Drive ROM magic",
            "fixtures/tar.dat": "archive or BK2 magic",
            "fixtures/oversize.dat": "blob exceeds 1048576 bytes",
            "scripts/unix.py": "machine-local absolute path",
            "scripts/windows.py": "machine-local absolute path",
        }
        for path, reason in expected.items():
            self.assertIn(f"path={path}", result.stdout)
            self.assertIn(f"reason={reason}", result.stdout)

    def _audit(self):
        return subprocess.run(
            [sys.executable, str(SCANNER), "--root", str(self.repository)],
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=False,
        )

    def _write(self, relative_path, content):
        path = self.repository / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)

    def _commit(self, message):
        self._git("add", ".")
        self._git("commit", "-q", "-m", message)
        return self._git("rev-parse", "HEAD").stdout.strip()

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
