"""Producer-owned contracts for the portable trace-v5 capture matrix."""

from __future__ import annotations

import copy
import hashlib
import json
import subprocess
import tempfile
import unittest
import zlib
from pathlib import Path
from unittest.mock import patch

from traces.trace_v5_capture_matrix import (
    ROWS,
    EXTRACTION_IDS,
    EXTRACTION_MATRIX_FORMAT,
    assemble,
    expand_commands,
    load_document,
    main,
    validate_document,
    verify_movies,
    verify_roms,
    verify_extraction_freeze,
)


TRACECHASER_ROOT = Path(__file__).resolve().parents[1]


class TraceV5CaptureMatrixTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary_directory.name)
        self.fixture_root = self.root / "consumer fixtures"
        self.movie_root = self.root / "consumer movies"
        self.batch_root = self.root / "capture batch"
        self.candidate_root = self.root / "candidate fleet"
        self.input_repository_root = self.root / "consumer checkout"
        self.fixture_root.mkdir()
        self.movie_root.mkdir()
        self.input_repository_root.mkdir()
        self.roms: dict[str, Path] = {}
        self.document = self._document()

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def test_loader_preserves_the_exact_six_row_extraction_contract(self) -> None:
        matrix = self.root / "matrix with spaces.json"
        matrix.write_text(json.dumps(self.document), encoding="utf-8")

        loaded = load_document(matrix)

        self.assertEqual(EXTRACTION_MATRIX_FORMAT, loaded["format"])
        self.assertEqual(list(EXTRACTION_IDS), [row["id"] for row in loaded["rows"]])
        reordered = copy.deepcopy(loaded)
        reordered["rows"][0], reordered["rows"][1] = reordered["rows"][1], reordered["rows"][0]
        with self.assertRaisesRegex(ValueError, "ids or ordering"):
            validate_document(reordered)

    def test_full_reviewed_matrix_retains_all_36_rows(self) -> None:
        self.assertEqual(36, len(ROWS))
        self.assertEqual(36, len({row["id"] for row in ROWS}))

    def test_rom_and_movie_checks_require_every_explicit_verified_input(self) -> None:
        verify_roms(self.document, self.roms)
        verify_movies(self.movie_root, self.document)

        incomplete = dict(self.roms)
        incomplete.pop("s2")
        with self.assertRaisesRegex(ValueError, "explicit ROM paths"):
            verify_roms(self.document, incomplete)

        wrong = self.root / "wrong explicit s1.gen"
        wrong.write_bytes(b"not the verified ROM")
        explicit = dict(self.roms)
        explicit["s1"] = wrong
        with self.assertRaisesRegex(ValueError, "ROM identity mismatch"):
            verify_roms(self.document, explicit)

    def test_expander_returns_literal_argv_without_shell_reparsing(self) -> None:
        commands = expand_commands(
            TRACECHASER_ROOT,
            self.movie_root,
            self.batch_root,
            self.document,
            self.roms,
        )

        self.assertEqual(6, len(commands))
        for row, argv in zip(self.document["rows"], commands):
            self.assertIsInstance(argv, list)
            self.assertEqual(str(TRACECHASER_ROOT / "bizhawk-headless" / "run.sh"), argv[0])
            self.assertEqual(str(self.roms[row["game"]]), argv[argv.index("--rom") + 1])
            self.assertEqual(str(self.movie_root / row["movie"]), argv[argv.index("--movie") + 1])
            self.assertEqual(
                str(self.batch_root / "captures" / row["id"]),
                argv[argv.index("--output") + 1],
            )

    def test_command_ledger_output_is_guarded_from_consumer_tree(self) -> None:
        matrix = self.root / "matrix with spaces.json"
        matrix.write_text(json.dumps(self.document), encoding="utf-8")
        result = main([
            "expand", "--matrix", str(matrix),
            "--input-repository-root", str(self.input_repository_root),
            "--fixture-root", str(self.fixture_root),
            "--movie-root", str(self.movie_root),
            "--batch-root", str(self.batch_root),
            "--s1-rom", str(self.roms["s1"]),
            "--s2-rom", str(self.roms["s2"]),
            "--s3k-rom", str(self.roms["s3k"]),
            "--output", str(self.input_repository_root / "commands.txt"),
        ])
        self.assertEqual(2, result)
        self.assertFalse((self.input_repository_root / "commands.txt").exists())

    def test_assembler_copies_only_explicit_fixture_and_capture_inputs(self) -> None:
        static_movie = self.fixture_root / "s1" / "kept movie.bk2"
        static_movie.parent.mkdir()
        static_movie.write_bytes(b"movie")
        static_sidecar = self.fixture_root / "static" / "readme.txt"
        static_sidecar.parent.mkdir()
        static_sidecar.write_text("static\n", encoding="utf-8")
        self.document["static_paths"] = ["static/readme.txt"]
        for row in self.document["rows"]:
            capture = self.batch_root / "captures" / row["id"]
            capture.mkdir(parents=True)
            (capture / "metadata.json").write_text(
                json.dumps({"trace_schema": 5, "id": row["id"]}) + "\n",
                encoding="utf-8",
            )

        result = assemble(
            TRACECHASER_ROOT,
            self.input_repository_root,
            self.fixture_root,
            self.batch_root,
            self.candidate_root,
            self.document,
        )

        self.assertEqual(8, result["copied_files"])
        self.assertEqual(b"movie", (self.candidate_root / "s1" / "kept movie.bk2").read_bytes())
        self.assertEqual("static\n", (self.candidate_root / "static" / "readme.txt").read_text())
        for row in self.document["rows"]:
            metadata = json.loads(
                (self.candidate_root / row["mappings"][0]["destination"] / "metadata.json").read_text()
            )
            self.assertEqual(5, metadata["trace_schema"])
        with self.assertRaisesRegex(ValueError, "candidate root must be absent"):
            assemble(
                TRACECHASER_ROOT,
                self.input_repository_root,
                self.fixture_root,
                self.batch_root,
                self.candidate_root,
                self.document,
            )

    def test_extraction_freeze_verifies_synthetic_source_toolchain_and_artifacts(self) -> None:
        repository, bizhawk_home, document = self._freeze_document()
        received: list[tuple[str, ...]] = []

        def build_runner(_repository, _source_commit, _bizhawk_home, filters):
            received.append(filters)
            return {
                "exit_code": 0,
                "artifacts": copy.deepcopy(document["freeze"]["native_artifacts"]),
            }

        with patch(
            "traces.trace_v5_capture_matrix._command_first_line",
            side_effect=("synthetic mono", "synthetic xbuild", "synthetic roslyn"),
        ):
            verify_extraction_freeze(
                repository,
                document,
                bizhawk_home=bizhawk_home,
                build_test_runner=build_runner,
            )

        self.assertEqual([("TraceCli",)], received)

    def test_extraction_freeze_rejects_source_toolchain_build_and_artifact_drift(self) -> None:
        repository, bizhawk_home, document = self._freeze_document()
        cases = (
            ("source diff", "source_diff_sha256", "0" * 64, "source diff hash mismatch"),
            ("compiler", "roslyn_csc_sha256", "0" * 64, "compiler SHA-256 mismatch"),
        )
        for name, key, value, message in cases:
            with self.subTest(name=name):
                changed = copy.deepcopy(document)
                target = changed["freeze"]["tracechaser_build"] if key == "source_diff_sha256" else changed["freeze"]["toolchain"]
                target[key] = value
                with patch(
                    "traces.trace_v5_capture_matrix._command_first_line",
                    side_effect=("synthetic mono", "synthetic xbuild", "synthetic roslyn"),
                ), self.assertRaisesRegex(ValueError, message):
                    verify_extraction_freeze(
                        repository,
                        changed,
                        bizhawk_home=bizhawk_home,
                        build_test_runner=lambda *_: 0,
                    )

        with patch(
            "traces.trace_v5_capture_matrix._command_first_line",
            side_effect=("synthetic mono", "synthetic xbuild", "synthetic roslyn"),
        ), self.assertRaisesRegex(ValueError, "native build/tests failed"):
            verify_extraction_freeze(
                repository,
                document,
                bizhawk_home=bizhawk_home,
                build_test_runner=lambda *_: 1,
            )

        wrong_artifacts = copy.deepcopy(document["freeze"]["native_artifacts"])
        wrong_artifacts["BizHawk.Headless.Gpgx.exe"] = {"size": 0, "sha256": "0" * 64}
        with patch(
            "traces.trace_v5_capture_matrix._command_first_line",
            side_effect=("synthetic mono", "synthetic xbuild", "synthetic roslyn"),
        ), self.assertRaisesRegex(ValueError, "deterministic native artifact identity mismatch"):
            verify_extraction_freeze(
                repository,
                document,
                bizhawk_home=bizhawk_home,
                build_test_runner=lambda *_: {"exit_code": 0, "artifacts": wrong_artifacts},
            )

    def _freeze_document(self) -> tuple[Path, Path, dict]:
        repository = self.root / "synthetic source repository"
        repository.mkdir()
        subprocess.run(["git", "init", "-q", str(repository)], check=True)
        subprocess.run(["git", "-C", str(repository), "config", "user.name", "TraceChaser Test"], check=True)
        subprocess.run(["git", "-C", str(repository), "config", "user.email", "test@example.invalid"], check=True)
        source = repository / "source.txt"
        source.write_text("base\n", encoding="utf-8")
        archive_lock = repository / "bizhawk-archive-lock.sh"
        archive_name = "BizHawk-2.11-linux-x64.tar.gz"
        archive_sha256 = "a" * 64
        archive_lock.write_text(f"{archive_name} {archive_sha256}\n", encoding="utf-8")
        source_lock = repository / "source-lock.json"
        source_lock.write_text("{}\n", encoding="utf-8")
        subprocess.run(["git", "-C", str(repository), "add", "."], check=True)
        subprocess.run(["git", "-C", str(repository), "commit", "-qm", "base"], check=True)
        base_commit = subprocess.check_output(
            ["git", "-C", str(repository), "rev-parse", "HEAD"], text=True
        ).strip()
        source.write_text("source\n", encoding="utf-8")
        subprocess.run(["git", "-C", str(repository), "add", "source.txt"], check=True)
        subprocess.run(["git", "-C", str(repository), "commit", "-qm", "source"], check=True)
        source_commit = subprocess.check_output(
            ["git", "-C", str(repository), "rev-parse", "HEAD"], text=True
        ).strip()
        source_diff = subprocess.check_output([
            "git", "-C", str(repository), "diff", "--full-index", "--binary",
            f"{base_commit}..{source_commit}",
        ])

        bizhawk_home = self.root / "explicit BizHawk home"
        runtime_input = bizhawk_home / "dll" / "runtime.dll"
        runtime_input.parent.mkdir(parents=True)
        runtime_input.write_bytes(b"runtime")
        capability = bizhawk_home / "Lua" / "capability.lua"
        capability.parent.mkdir()
        capability.write_text("client.invisibleemulation\n", encoding="utf-8")
        compiler = self.root / "synthetic roslyn csc.exe"
        compiler.write_bytes(b"compiler")
        artifact = {"size": 7, "sha256": hashlib.sha256(b"artifact").hexdigest()}
        provenance_repository = self.root / "synthetic provenance repository"
        provenance_repository.mkdir()
        subprocess.run(["git", "init", "-q", str(provenance_repository)], check=True)
        subprocess.run(["git", "-C", str(provenance_repository), "config", "user.name", "TraceChaser Test"], check=True)
        subprocess.run(["git", "-C", str(provenance_repository), "config", "user.email", "test@example.invalid"], check=True)
        (provenance_repository / "provenance.txt").write_text("opaque provenance\n", encoding="utf-8")
        subprocess.run(["git", "-C", str(provenance_repository), "add", "."], check=True)
        subprocess.run(["git", "-C", str(provenance_repository), "commit", "-qm", "provenance"], check=True)
        provenance_commit = subprocess.check_output(
            ["git", "-C", str(provenance_repository), "rev-parse", "HEAD"], text=True
        ).strip()
        return repository, bizhawk_home, {
            "freeze": {
                "policy": "extraction-build-test-v1",
                "source_commit": provenance_commit,
                "source_diff_base_commit": "1" * 40,
                "source_diff_sha256": "2" * 64,
                "tracechaser_build": {
                    "source_commit": source_commit,
                    "source_diff_base_commit": base_commit,
                    "source_diff_sha256": hashlib.sha256(source_diff).hexdigest(),
                    "native_test_filters": ["TraceCli"],
                },
                "native_artifacts": {
                    name: copy.deepcopy(artifact)
                    for name in (
                        "BizHawk.Headless.Gpgx.exe",
                        "BizHawk.Headless.Gpgx.pdb",
                        "BizHawk.Headless.Gpgx.Tests.exe",
                        "BizHawk.Headless.Gpgx.Tests.pdb",
                    )
                },
                "bizhawk": {
                    "version": "2.11",
                    "archive_lock": {
                        "path": archive_lock.name,
                        "sha256": hashlib.sha256(archive_lock.read_bytes()).hexdigest(),
                        "archive_name": archive_name,
                        "archive_sha256": archive_sha256,
                    },
                    "source_lock": {
                        "path": source_lock.name,
                        "sha256": hashlib.sha256(source_lock.read_bytes()).hexdigest(),
                    },
                    "runtime_inputs": {
                        "dll/runtime.dll": {
                            "size": runtime_input.stat().st_size,
                            "sha256": hashlib.sha256(runtime_input.read_bytes()).hexdigest(),
                        },
                    },
                    "capabilities": [{
                        "path": "Lua/capability.lua",
                        "contains": "client.invisibleemulation",
                    }],
                },
                "toolchain": {
                    "mono_version": "synthetic mono",
                    "xbuild_version": "synthetic xbuild",
                    "roslyn_csc_path": str(compiler),
                    "roslyn_csc_sha256": hashlib.sha256(compiler.read_bytes()).hexdigest(),
                    "roslyn_csc_version": "synthetic roslyn",
                },
            },
        }

    def _document(self) -> dict:
        roms: dict[str, dict[str, str]] = {}
        for game in ("s1", "s2", "s3k"):
            payload = f"verified {game} ROM".encode()
            path = self.root / f"explicit {game} ROM.gen"
            path.write_bytes(payload)
            self.roms[game] = path
            roms[game] = {
                "sha256": hashlib.sha256(payload).hexdigest(),
                "sha1": hashlib.sha1(payload).hexdigest(),
                "crc32": f"{zlib.crc32(payload) & 0xffffffff:08X}",
            }
        rows = []
        games = ("s1", "s1", "s2", "s2", "s3k", "s3k")
        for identifier, game in zip(EXTRACTION_IDS, games):
            relative_movie = f"{game}/{identifier} movie.bk2"
            movie = self.movie_root / relative_movie
            movie.parent.mkdir(parents=True, exist_ok=True)
            payload = f"verified movie {identifier}".encode()
            movie.write_bytes(payload)
            rows.append({
                "id": identifier,
                "game": game,
                "movie": relative_movie,
                "movie_sha256": hashlib.sha256(payload).hexdigest(),
                "selectors": ["--trace-profile", "complete_run"],
                "mappings": [{"source": "", "destination": f"{game}/{identifier}"}],
            })
        return {
            "format": EXTRACTION_MATRIX_FORMAT,
            "roms": roms,
            "capture": {
                "mode": "trace",
                "output_template": "captures/{id}",
                "estimated_peak_bytes": 1,
                "required_free_space_multiplier": 1,
            },
            "static_paths": [],
            "rows": rows,
        }


if __name__ == "__main__":
    unittest.main()
