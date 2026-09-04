import gzip
import hashlib
import io
import json
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

    def test_curated_audio_schema_removed_from_head_still_passes_history(self):
        schema_path = "contracts/audio/override-resume-first-divergence-metadata-v1.schema.json"
        self._write(
            schema_path,
            b'{"$id": "openggf.override-resume-first-divergence-metadata.v1"}\n',
        )
        self._commit("add curated audio override schema")
        os.remove(self.repository / schema_path)
        self._git("add", "-u")
        self._git("commit", "-q", "-m", "remove curated audio override schema")

        result = self._audit()

        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual("history audit: PASS\n", result.stdout)

    def test_uncurated_audio_contract_shape_is_rejected_in_history(self):
        self._write(
            "contracts/audio/nested/override-resume-first-divergence-v1.schema.json",
            b"{}\n",
        )
        introducing_commit = self._commit("add nested audio schema")

        result = self._audit()

        self.assertEqual(1, result.returncode)
        self.assertIn(f"commit={introducing_commit}", result.stdout)
        self.assertIn(
            "path=contracts/audio/nested/override-resume-first-divergence-v1.schema.json",
            result.stdout,
        )
        self.assertIn("reason=contract file outside curated exceptions", result.stdout)

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

    def test_control_and_non_utf8_path_bytes_are_lossless(self):
        paths = (
            b"safe\nmovie.bk2",
            b"safe\tmovie.gen",
            b"safe-\xff-movie.bk2",
        )
        for path in paths:
            self._write_raw_path(path, b"ordinary non-magic content")
        commit = self._commit("add Git-valid unusual path bytes")

        result = self._audit()

        self.assertEqual(1, result.returncode)
        self.assertIn(f"commit={commit}", result.stdout)
        self.assertIn(r"path=safe\nmovie.bk2", result.stdout)
        self.assertIn(r"path=safe\tmovie.gen", result.stdout)
        self.assertIn(r"path=safe-\xff-movie.bk2", result.stdout)

    def test_reused_prohibited_blob_reports_every_committed_path(self):
        machine_path = b"/" + b"home" + b"/example/private"
        content = b"root = '" + machine_path + b"'\n"
        self._write("alpha/safe.txt", content)
        self._write("beta/safe.txt", content)
        commit = self._commit("reuse prohibited blob")
        object_id = self._git("rev-parse", "HEAD:alpha/safe.txt").stdout.strip()

        result = self._audit()

        matching = [
            line
            for line in result.stdout.splitlines()
            if f"object={object_id}" in line and "reason=machine-local absolute path" in line
        ]
        self.assertEqual(2, len(matching), result.stdout)
        self.assertTrue(all(f"commit={commit}" in line for line in matching))
        self.assertTrue(any("path=alpha/safe.txt" in line for line in matching))
        self.assertTrue(any("path=beta/safe.txt" in line for line in matching))

    def test_pathless_rev_list_annotation_uses_committed_tree_occurrence(self):
        machine_path = b"C:" + b"\\" + b"Users" + b"\\example\\private"
        self._write("tree/safe.txt", b"root = r'" + machine_path + b"'\n")
        commit = self._commit("add prohibited committed blob")
        object_id = self._git("rev-parse", "HEAD:tree/safe.txt").stdout.strip()
        self._git("update-ref", "refs/test/direct-blob", object_id)

        result = self._audit()

        matching = [line for line in result.stdout.splitlines() if f"object={object_id}" in line]
        self.assertEqual(1, len(matching), result.stdout)
        self.assertIn(f"commit={commit}", matching[0])
        self.assertIn("path=tree/safe.txt", matching[0])
        self.assertNotIn("path=<unknown>", result.stdout)

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

    def test_manifested_v5_contract_pack_passes_reachable_history_audit(self):
        self._write_contract_pack()
        self._commit("add bounded v5 contract pack")

        result = self._audit()

        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual("history audit: PASS\n", result.stdout)

    def test_v5_manifest_relationship_is_enforced_at_each_historical_commit(self):
        files = self._write_contract_pack()
        valid_commit = self._commit("add valid v5 contract pack")
        self._write("contracts/v5/fixtures/physics.csv", files["fixtures/physics.csv"] + b"1,1\n")
        invalid_commit = self._commit("change contract without manifest update")

        result = self._audit()

        self.assertEqual(1, result.returncode)
        self.assertNotIn(f"commit={valid_commit}", result.stdout)
        self.assertIn(f"commit={invalid_commit}", result.stdout)
        self.assertIn("path=contracts/v5/fixtures/physics.csv", result.stdout)
        self.assertIn("reason=v5 contract stored size mismatch", result.stdout)

    def test_v5_contract_case_and_gzip_member_policy_is_enforced_in_history(self):
        logical = b"frame,x\n0,0\n"
        content = gzip.compress(logical, mtime=0) + gzip.compress(b"1,1\n", mtime=1)
        path = "fixtures/physics.CSV.GZ"
        self._write(f"contracts/v5/{path}", content)
        manifest = {
            "format": "tracechaser-v5-artifact-manifest-v1",
            "files": [
                {
                    "path": path,
                    "stored_size": len(content),
                    "stored_sha256": hashlib.sha256(content).hexdigest(),
                    "logical_size": len(logical) + len(b"1,1\n"),
                    "logical_sha256": hashlib.sha256(logical + b"1,1\n").hexdigest(),
                }
            ],
        }
        self._write(
            "contracts/v5/manifest.json",
            json.dumps(manifest, sort_keys=True, separators=(",", ":")).encode() + b"\n",
        )
        invalid_commit = self._commit("add uppercase nondeterministic contract")

        result = self._audit()

        self.assertEqual(1, result.returncode)
        self.assertIn(f"commit={invalid_commit}", result.stdout)
        self.assertIn("reason=v5 contract file type is not admissible", result.stdout)

    def test_v5_contract_symlink_is_rejected_in_its_historical_commit(self):
        files = self._write_contract_pack()
        valid_commit = self._commit("add valid v5 contract pack")
        link_target = b"../../outside.json"
        files["fixtures/link.json"] = link_target
        self._write_contract_pack(files)
        link = self.repository / "contracts/v5/fixtures/link.json"
        link.unlink()
        link.symlink_to(link_target.decode())
        invalid_commit = self._commit("replace contract fixture with escaping symlink")

        result = self._audit()

        self.assertEqual(1, result.returncode)
        self.assertNotIn(f"commit={valid_commit}", result.stdout)
        self.assertIn(f"commit={invalid_commit}", result.stdout)
        self.assertIn("path=contracts/v5/fixtures/link.json", result.stdout)
        self.assertIn("reason=v5 contract entry is not a regular Git file", result.stdout)

    def test_v5_contract_gitlink_manifest_is_rejected_in_history(self):
        target_commit = "1" * 40
        self._git(
            "update-index",
            "--add",
            "--cacheinfo",
            f"160000,{target_commit},contracts/v5/manifest.json",
        )
        self._git("commit", "-q", "-m", "add invalid gitlink contract manifest")
        invalid_commit = self._git("rev-parse", "HEAD").stdout.strip()

        result = self._audit()

        self.assertEqual(1, result.returncode)
        self.assertIn(f"commit={invalid_commit}", result.stdout)
        self.assertIn("path=contracts/v5/manifest.json", result.stdout)
        self.assertIn("reason=v5 contract entry is not a regular Git file", result.stdout)

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

    def _write_raw_path(self, relative_path, content):
        repository = os.fsencode(self.repository)
        path = os.path.join(repository, relative_path)
        parent = os.path.dirname(path)
        if parent:
            os.makedirs(parent, exist_ok=True)
        descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
        try:
            os.write(descriptor, content)
        finally:
            os.close(descriptor)

    def _commit(self, message):
        self._git("add", ".")
        self._git("commit", "-q", "-m", message)
        return self._git("rev-parse", "HEAD").stdout.strip()

    def _write_contract_pack(self, files=None):
        if files is None:
            logical_physics = b"frame,x\n0,0\n"
            compressed = io.BytesIO()
            with gzip.GzipFile(fileobj=compressed, mode="wb", filename="", mtime=0) as output:
                output.write(logical_physics)
            files = {
                "fixtures/physics.csv": logical_physics,
                "fixtures/physics.csv.gz": compressed.getvalue(),
                "fixtures/aux_state.jsonl": b'{"event":"synthetic"}\n',
                "fixtures/hardware_timing.jsonl": b'{"kind":"synthetic"}\n',
                "fixtures/run_manifest.json": b'{"segments":[]}\n',
            }
        entries = []
        for path, content in sorted(files.items()):
            entry = {
                "path": path,
                "stored_size": len(content),
                "stored_sha256": hashlib.sha256(content).hexdigest(),
            }
            if path.endswith(".gz"):
                logical = gzip.decompress(content)
                entry["logical_size"] = len(logical)
                entry["logical_sha256"] = hashlib.sha256(logical).hexdigest()
            entries.append(entry)
            self._write(f"contracts/v5/{path}", content)
        manifest = {
            "format": "tracechaser-v5-artifact-manifest-v1",
            "files": entries,
        }
        self._write(
            "contracts/v5/manifest.json",
            json.dumps(manifest, sort_keys=True, separators=(",", ":")).encode() + b"\n",
        )
        return files

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
