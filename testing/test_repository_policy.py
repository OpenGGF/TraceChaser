import gzip
import hashlib
import importlib.util
import io
import json
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
            "contracts/audio/normalization-contract-v1.json": b'{"version":1}\n',
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

    def test_accepts_bounded_manifested_v5_payloads_and_deterministic_gzip(self):
        self._stage_contract_pack()

        result = self._audit()

        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual("repository policy: PASS\n", result.stdout)

    def test_v5_contract_sources_are_visible_through_root_ignore_rules(self):
        self._write(".gitignore", (REPOSITORY_ROOT / ".gitignore").read_bytes())
        files, manifest = self._contract_pack()
        for path, content in files.items():
            self._write(f"contracts/v5/{path}", content)
        self._write("contracts/v5/manifest.json", manifest)

        hidden = []
        for path in [*files, "manifest.json"]:
            relative_path = f"contracts/v5/{path}"
            result = self._git("check-ignore", "-q", relative_path, check=False)
            if result.returncode == 0:
                hidden.append(relative_path)

        self.assertEqual([], hidden)

    def test_rejects_unmanifested_pack_members_and_arbitrary_contract_files(self):
        self._stage_contract_pack(
            extras={
                "contracts/v5/fixtures/unlisted.txt": b"not curated\n",
                "contracts/arbitrary.json": b"{}\n",
            }
        )

        result = self._audit()

        self.assertEqual(1, result.returncode)
        self.assertIn(
            "path=contracts/v5/fixtures/unlisted.txt reason=unmanifested v5 contract file",
            result.stdout,
        )
        self.assertIn(
            "path=contracts/arbitrary.json reason=contract file outside curated exceptions",
            result.stdout,
        )

    def test_rejects_hash_size_and_logical_identity_mismatches(self):
        files, manifest_bytes = self._contract_pack()
        manifest = json.loads(manifest_bytes)
        entries = {entry["path"]: entry for entry in manifest["files"]}
        entries["fixtures/run_manifest.json"]["stored_sha256"] = "0" * 64
        entries["fixtures/aux_state.jsonl"]["stored_size"] += 1
        entries["fixtures/physics.csv.gz"]["logical_sha256"] = "f" * 64
        self._stage_contract_pack(files=files, manifest=manifest)

        result = self._audit()

        self.assertEqual(1, result.returncode)
        self.assertIn("reason=v5 contract stored SHA-256 mismatch", result.stdout)
        self.assertIn("reason=v5 contract stored size mismatch", result.stdout)
        self.assertIn("reason=v5 contract logical SHA-256 mismatch", result.stdout)

    def test_rejects_oversized_and_nondeterministic_gzip_contracts(self):
        files, manifest_bytes = self._contract_pack()
        files["fixtures/physics.csv"] = b"x" * 65537
        files["fixtures/physics.csv.gz"] = gzip.compress(b"frame,x\n0,0\n", mtime=1)
        manifest = self._manifest(files)
        self._stage_contract_pack(files=files, manifest=manifest)

        result = self._audit()

        self.assertEqual(1, result.returncode)
        self.assertIn("reason=v5 contract file exceeds 65536 bytes", result.stdout)
        self.assertIn(
            "reason=v5 contract gzip member header is not deterministic",
            result.stdout,
        )

    def test_rejects_manifest_paths_that_escape_the_exact_pack_boundary(self):
        files, manifest_bytes = self._contract_pack()
        manifest = json.loads(manifest_bytes)
        manifest["files"].append(
            {
                "path": "../escape.json",
                "stored_size": 3,
                "stored_sha256": hashlib.sha256(b"{}\n").hexdigest(),
            }
        )
        self._stage_contract_pack(files=files, manifest=manifest)

        result = self._audit()

        self.assertEqual(1, result.returncode)
        self.assertIn(
            "path=contracts/v5/manifest.json reason=v5 contract manifest path escapes pack boundary",
            result.stdout,
        )

    def test_manifest_cannot_curate_arbitrary_types_or_compressed_rom_content(self):
        files, _manifest_bytes = self._contract_pack()
        rom = bytearray(0x200)
        rom[0x100:0x104] = b"SEGA"
        compressed_rom = io.BytesIO()
        with gzip.GzipFile(
            fileobj=compressed_rom, mode="wb", filename="", mtime=0
        ) as output:
            output.write(bytes(rom))
        files["fixtures/disguised.csv.gz"] = compressed_rom.getvalue()
        files["fixtures/movie.bk2"] = b"synthetic movie\n"
        files["fixtures/notes.txt"] = b"arbitrary contract file\n"
        self._stage_contract_pack(files=files, manifest=self._manifest(files))

        result = self._audit()

        self.assertEqual(1, result.returncode)
        self.assertIn(
            "path=contracts/v5/fixtures/disguised.csv.gz reason=Mega Drive ROM magic",
            result.stdout,
        )
        self.assertIn(
            "path=contracts/v5/fixtures/movie.bk2 reason=forbidden suffix .bk2",
            result.stdout,
        )
        self.assertIn(
            "path=contracts/v5/fixtures/notes.txt reason=v5 contract file type is not admissible",
            result.stdout,
        )

    def test_v5_contract_filenames_and_suffixes_are_exactly_lowercase(self):
        files, _manifest_bytes = self._contract_pack()
        files["fixtures/physics.CSV.GZ"] = b"not a lowercase gzip contract\n"
        files["fixtures/Physics.csv.gz"] = gzip.compress(b"frame,x\n", mtime=0)
        manifest = self._manifest(files)
        self._stage_contract_pack(files=files, manifest=manifest)

        result = self._audit()

        self.assertEqual(1, result.returncode)
        self.assertIn(
            "path=contracts/v5/fixtures/physics.CSV.GZ reason=v5 contract file type is not admissible",
            result.stdout,
        )
        self.assertIn(
            "path=contracts/v5/fixtures/Physics.csv.gz reason=v5 contract file type is not admissible",
            result.stdout,
        )

    def test_v5_gzip_entries_require_complete_stored_and_logical_identity(self):
        files, manifest_bytes = self._contract_pack()
        manifest = json.loads(manifest_bytes)
        gzip_entry = next(
            entry for entry in manifest["files"] if entry["path"].endswith(".gz")
        )
        del gzip_entry["stored_size"]
        del gzip_entry["logical_size"]
        del gzip_entry["logical_sha256"]
        self._stage_contract_pack(files=files, manifest=manifest)

        result = self._audit()

        self.assertEqual(1, result.returncode)
        self.assertIn(
            "reason=v5 contract gzip stored and logical identity fields are required",
            result.stdout,
        )

    def test_v5_gzip_parser_checks_every_member_and_rejects_trailing_or_malformed_data(self):
        deterministic = gzip.compress(b"frame,x\n0,0\n", mtime=0)
        nondeterministic_member = gzip.compress(b"1,1\n", mtime=1)
        unsafe_member = bytearray(gzip.compress(b"2,2\n", mtime=0))
        unsafe_member[3] = 4
        files = {
            "fixtures/bad-second.csv.gz": deterministic + nondeterministic_member,
            "fixtures/bad-flags.csv.gz": deterministic + bytes(unsafe_member),
            "fixtures/trailing.csv.gz": deterministic + b"trailing junk",
            "fixtures/malformed.csv.gz": deterministic[:-4],
        }
        logical = {
            "fixtures/bad-second.csv.gz": b"frame,x\n0,0\n1,1\n",
            "fixtures/bad-flags.csv.gz": b"frame,x\n0,0\n2,2\n",
            "fixtures/trailing.csv.gz": b"frame,x\n0,0\n",
            "fixtures/malformed.csv.gz": b"frame,x\n0,0\n",
        }
        manifest = {
            "format": "tracechaser-v5-artifact-manifest-v1",
            "files": [
                self._manifest_entry(path, content, logical[path])
                for path, content in sorted(files.items())
            ],
        }
        self._stage_contract_pack(files=files, manifest=manifest)

        result = self._audit()

        self.assertEqual(1, result.returncode)
        self.assertIn(
            "path=contracts/v5/fixtures/bad-second.csv.gz reason=v5 contract gzip member header is not deterministic",
            result.stdout,
        )
        self.assertIn(
            "path=contracts/v5/fixtures/bad-flags.csv.gz reason=v5 contract gzip member header is not deterministic",
            result.stdout,
        )
        self.assertIn(
            "path=contracts/v5/fixtures/trailing.csv.gz reason=v5 contract gzip stream has trailing data",
            result.stdout,
        )
        self.assertIn(
            "path=contracts/v5/fixtures/malformed.csv.gz reason=v5 contract gzip payload is malformed",
            result.stdout,
        )

    def test_v5_contract_entries_must_be_regular_git_files(self):
        files, _manifest_bytes = self._contract_pack()
        link_target = b"../../outside.json"
        files["fixtures/link.json"] = link_target
        self._stage_contract_pack(files=files, manifest=self._manifest(files))
        link = self.repository / "contracts/v5/fixtures/link.json"
        link.unlink()
        link.symlink_to(link_target.decode())
        self._git("add", "-f", "contracts/v5/fixtures/link.json")

        result = self._audit()

        self.assertEqual(1, result.returncode)
        self.assertIn(
            "path=contracts/v5/fixtures/link.json reason=v5 contract entry is not a regular Git file",
            result.stdout,
        )

    def test_v5_contract_gitlink_is_rejected_without_loading_its_object(self):
        files, _manifest_bytes = self._contract_pack()
        files["fixtures/gitlink.json"] = b"placeholder"
        self._stage_contract_pack(files=files, manifest=self._manifest(files))
        self._git(
            "update-index",
            "--add",
            "--cacheinfo",
            f"160000,{'2' * 40},contracts/v5/fixtures/gitlink.json",
        )

        result = self._audit()

        self.assertEqual(1, result.returncode)
        self.assertIn(
            "path=contracts/v5/fixtures/gitlink.json reason=v5 contract entry is not a regular Git file",
            result.stdout,
        )

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

    def _stage_contract_pack(self, files=None, manifest=None, extras=None):
        if files is None:
            files, manifest_bytes = self._contract_pack()
        else:
            manifest_bytes = json.dumps(
                manifest,
                sort_keys=True,
                separators=(",", ":"),
            ).encode() + b"\n"
        for path, content in files.items():
            self._write(f"contracts/v5/{path}", content)
        self._write("contracts/v5/manifest.json", manifest_bytes)
        for path, content in (extras or {}).items():
            self._write(path, content)
        self._git("add", "-f", "contracts")

    def _contract_pack(self):
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
        return files, json.dumps(
            self._manifest(files),
            sort_keys=True,
            separators=(",", ":"),
        ).encode() + b"\n"

    def _manifest(self, files):
        entries = [
            self._manifest_entry(path, content)
            for path, content in sorted(files.items())
        ]
        return {
            "format": "tracechaser-v5-artifact-manifest-v1",
            "files": entries,
        }

    def _manifest_entry(self, path, content, logical=None):
        entry = {
            "path": path,
            "stored_size": len(content),
            "stored_sha256": hashlib.sha256(content).hexdigest(),
        }
        if path.endswith(".gz"):
            if logical is None:
                logical = gzip.decompress(content)
            entry["logical_size"] = len(logical)
            entry["logical_sha256"] = hashlib.sha256(logical).hexdigest()
        return entry

    def _git(self, *arguments, check=True):
        return subprocess.run(
            ["git", "-C", str(self.repository), *arguments],
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=check,
        )


if __name__ == "__main__":
    unittest.main()
