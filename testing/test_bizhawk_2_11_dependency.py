import hashlib
import io
import json
import os
import shutil
import stat
import subprocess
import tarfile
import tempfile
import unittest
from unittest import mock
from pathlib import Path

import bizhawk.bizhawk_2_11 as bizhawk_dependency
from bizhawk.bizhawk_2_11 import (
    DependencyError,
    _extract_archive,
    _publish_directory_noreplace,
    acquire_archive,
    load_runtime_lock,
    parse_lua_metadata,
    preflight_installation,
    verify_archive,
)
from bizhawk.lua_source import collect_lua_api_references


ROOT = Path(__file__).resolve().parents[1]
LOCK_PATH = ROOT / "dependencies" / "bizhawk-2.11-linux-x64.lock.json"
EXPECTED_ARCHIVE_SHA256 = (
    "cdaf9650d880bae660d63a388430f630b8d8a96b1ba59ebf0e0195a645c3bab8"
)
RECORDER_PATHS = tuple(
    ROOT / "bizhawk" / name
    for name in (
        "s1_trace_recorder.lua",
        "s1_complete_run_recorder.lua",
        "s2_trace_recorder.lua",
        "s2_ss_trace_recorder.lua",
        "s3k_trace_recorder.lua",
        "s3k_complete_run_recorder.lua",
    )
)
PROBE_ROOT = ROOT / "bizhawk" / "probes"


def _synthetic_lock(archive_sha256: str = "0" * 64) -> dict:
    return {
        "schema": "tracechaser.bizhawk-runtime-archive-lock.v1",
        "consumer": "official-linux-runtime",
        "release": {
            "version": "2.11",
            "assembly_version": "2.11.0.0",
            "assembly_version_raw": "Version: 2.11.0.0",
            "archive_name": "BizHawk-2.11-linux-x64.tar.gz",
            "official_url": (
                "https://github.com/TASEmulators/BizHawk/releases/download/"
                "2.11/BizHawk-2.11-linux-x64.tar.gz"
            ),
            "sha256": archive_sha256,
            "install_directory": "BizHawk-2.11-linux-x64",
        },
        "versioned_files": ["EmuHawk.exe", "dll/BizHawk.Client.Common.dll"],
        "required_files": [
            "EmuHawk.exe",
            "dll/BizHawk.Client.Common.dll",
            "dll/NLua.dll",
            "Lua/GBA/SonicAdvance_CamHack.lua",
        ],
        "lua_capabilities": [
            {
                "api": "client.invisibleemulation",
                "assembly": "dll/BizHawk.Client.Common.dll",
                "library_class": "BizHawk.Client.Common.ClientLuaLibrary",
                "managed_method": "InvisibleEmulation",
                "registered_name": "invisibleemulation",
                "example_path": "Lua/GBA/SonicAdvance_CamHack.lua",
                "example_marker": "client.invisibleemulation",
            },
            {
                "api": "emu.frameadvance",
                "assembly": "dll/BizHawk.Client.Common.dll",
                "library_class": "BizHawk.Client.Common.EmulationLuaLibrary",
                "managed_method": "FrameAdvance",
                "registered_name": "frameadvance",
            },
        ],
    }


def _write_install(home: Path, lock: dict) -> None:
    for relative in lock["required_files"]:
        path = home / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b"synthetic")
    client = home / "dll" / "BizHawk.Client.Common.dll"
    client.write_bytes(b"synthetic managed assembly")
    for capability in lock["lua_capabilities"]:
        if "example_path" in capability:
            (home / capability["example_path"]).write_text(
                f"{capability['example_marker']}(true)\n", encoding="utf-8"
            )


def _version_probe(raw: str):
    return lambda _path: raw


METHOD_METADATA = """Method Table (1..2)
########## BizHawk.Client.Common.ClientLuaLibrary
1: instance default void InvisibleEmulation (bool invisible)  (param: 1 impl_flags: cil managed )
########## BizHawk.Client.Common.EmulationLuaLibrary
2: instance default void FrameAdvance ()  (param: 2 impl_flags: cil managed )
"""
ATTRIBUTE_METADATA = """Custom Attributes Table (1..2)
1: MethodDef: 1: instance void class BizHawk.Client.Common.LuaMethodAttribute::'.ctor'(string, string) ["invisibleemulation\u0003cap", "cap"]
2: MethodDef: 2: instance void class BizHawk.Client.Common.LuaMethodAttribute::'.ctor'(string, string) ["frameadvance\u0003cap", "cap"]
"""


def _metadata_probe(
    method_output: str = METHOD_METADATA,
    attribute_output: str = ATTRIBUTE_METADATA,
):
    return lambda _path: parse_lua_metadata(method_output, attribute_output)


def _valid_offline_archive(path: Path) -> dict:
    payload = io.BytesIO()
    with tarfile.open(fileobj=payload, mode="w:gz") as archive:
        for relative, content in (
            ("EmuHawk.exe", b"managed"),
            ("dll/BizHawk.Client.Common.dll", b"managed"),
            ("dll/NLua.dll", b"nlua"),
            (
                "Lua/GBA/SonicAdvance_CamHack.lua",
                b"client.invisibleemulation(true)\n",
            ),
        ):
            info = tarfile.TarInfo(f"BizHawk-2.11-linux-x64/{relative}")
            info.size = len(content)
            archive.addfile(info, io.BytesIO(content))
    archive_bytes = payload.getvalue()
    path.write_bytes(archive_bytes)
    return _synthetic_lock(hashlib.sha256(archive_bytes).hexdigest())


def _extract_fully_trusted(archive: Path, staging_root: Path) -> None:
    with tarfile.open(archive, mode="r:gz") as source:
        source.extractall(staging_root, filter="fully_trusted")


class RuntimeArchiveLockTests(unittest.TestCase):
    def test_committed_lock_preserves_reviewed_official_linux_archive(self) -> None:
        lock = load_runtime_lock(LOCK_PATH)

        self.assertEqual("tracechaser.bizhawk-runtime-archive-lock.v1", lock["schema"])
        self.assertEqual("official-linux-runtime", lock["consumer"])
        self.assertEqual("2.11", lock["release"]["version"])
        self.assertEqual("2.11.0.0", lock["release"]["assembly_version"])
        self.assertEqual("BizHawk-2.11-linux-x64.tar.gz", lock["release"]["archive_name"])
        self.assertEqual(
            "https://github.com/TASEmulators/BizHawk/releases/download/"
            "2.11/BizHawk-2.11-linux-x64.tar.gz",
            lock["release"]["official_url"],
        )
        self.assertEqual(EXPECTED_ARCHIVE_SHA256, lock["release"]["sha256"])
        self.assertNotIn("source_lock", lock)
        self.assertNotIn("source_commit", json.dumps(lock, sort_keys=True))
        invisible = next(
            capability
            for capability in lock["lua_capabilities"]
            if capability["api"] == "client.invisibleemulation"
        )
        self.assertEqual("Lua/GBA/SonicAdvance_CamHack.lua", invisible["example_path"])
        self.assertEqual("client.invisibleemulation", invisible["example_marker"])
        for capability in lock["lua_capabilities"]:
            self.assertNotIn("library_marker", capability)
            self.assertNotIn("method_marker", capability)
            self.assertTrue(capability["library_class"].endswith("LuaLibrary"))
            self.assertEqual(
                capability["api"].split(".", 1)[1], capability["registered_name"]
            )

    def test_lock_capabilities_exactly_cover_recorder_and_probe_lua_calls(self) -> None:
        required = collect_lua_api_references(RECORDER_PATHS, PROBE_ROOT)
        locked = {
            capability["api"]
            for capability in load_runtime_lock(LOCK_PATH)["lua_capabilities"]
        }

        self.assertEqual(required, locked)

    def test_nested_probe_unique_api_is_included_without_comment_or_string_decoys(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            nested = root / "examples" / "nested.lua"
            nested.parent.mkdir()
            nested.write_text(
                """-- client.comment_decoy()
local text = 'emu.string_decoy()'
memory.unique_nested_api()
""",
                encoding="utf-8",
            )

            required = collect_lua_api_references((), root)

        self.assertEqual({"memory.unique_nested_api"}, required)


class PreflightTests(unittest.TestCase):
    def test_metadata_parser_preserves_registration_with_quoted_description(self) -> None:
        methods = """########## BizHawk.Client.Common.MovieLuaLibrary
44: instance default string Mode ()  (param: 1 impl_flags: cil managed )
"""
        description = (
            'Returns the mode of the current movie. Possible modes: '
            '"PLAY", "RECORD", "FINISHED", "INACTIVE"'
        )
        attributes = (
            "44: MethodDef: 44: instance void class "
            "BizHawk.Client.Common.LuaMethodAttribute::'.ctor'(string, string) "
            f'["mode{chr(len(description))}{description}", "{description}"]\n'
        )

        registrations = parse_lua_metadata(methods, attributes)

        self.assertIn(
            (
                "BizHawk.Client.Common.MovieLuaLibrary",
                "Mode",
                "mode",
            ),
            {
                (
                    registration.library_class,
                    registration.managed_method,
                    registration.registered_name,
                )
                for registration in registrations
            },
        )

    def test_exact_211_managed_version_and_all_capabilities_are_accepted(self) -> None:
        lock = _synthetic_lock()
        with tempfile.TemporaryDirectory() as temporary:
            home = Path(temporary) / "explicit-user-install"
            _write_install(home, lock)

            report = preflight_installation(
                home,
                lock,
                version_probe=_version_probe("Version: 2.11.0.0"),
                metadata_probe=_metadata_probe(),
            )

        self.assertEqual("2.11", report.version)
        self.assertEqual("Version: 2.11.0.0", report.detected_version_raw)
        self.assertEqual("Version: 2.11.0.0", report.expected_version_raw)
        self.assertEqual(
            ("client.invisibleemulation", "emu.frameadvance"), report.lua_capabilities
        )

    def test_nonexact_and_unparseable_versions_report_raw_detected_and_expected(self) -> None:
        cases = (
            "Version: 2.11.1.0",
            "Version: 2.10.0.0",
            "Version: 2.12.0.0",
            "not a managed assembly version",
        )
        lock = _synthetic_lock()
        with tempfile.TemporaryDirectory() as temporary:
            home = Path(temporary) / "explicit-user-install"
            _write_install(home, lock)
            for raw in cases:
                with self.subTest(raw=raw), self.assertRaises(DependencyError) as raised:
                    preflight_installation(home, lock, version_probe=_version_probe(raw))
                diagnostic = str(raised.exception)
                self.assertIn(f"detected raw={raw!r}", diagnostic)
                self.assertIn("expected raw='Version: 2.11.0.0'", diagnostic)

    @unittest.skipUnless(shutil.which("monodis"), "monodis is unavailable")
    def test_real_monodis_failure_reports_raw_detected_and_locked_expected(self) -> None:
        lock = _synthetic_lock()
        with tempfile.TemporaryDirectory() as temporary:
            home = Path(temporary) / "explicit-user-install"
            _write_install(home, lock)

            with self.assertRaises(DependencyError) as raised:
                preflight_installation(home, lock)

        diagnostic = str(raised.exception)
        self.assertIn("managed-version probe failed", diagnostic)
        self.assertIn("detected raw=", diagnostic)
        self.assertIn("expected raw='Version: 2.11.0.0'", diagnostic)

    def test_cross_library_method_pairing_is_rejected(self) -> None:
        lock = _synthetic_lock()
        lock["lua_capabilities"][1]["library_class"] = (
            "BizHawk.Client.Common.ClientLuaLibrary"
        )
        with tempfile.TemporaryDirectory() as temporary:
            home = Path(temporary) / "explicit-user-install"
            _write_install(home, lock)

            with self.assertRaises(DependencyError) as raised:
                preflight_installation(
                    home,
                    lock,
                    version_probe=_version_probe("Version: 2.11.0.0"),
                    metadata_probe=_metadata_probe(),
                )

        diagnostic = str(raised.exception)
        self.assertIn("emu.frameadvance", diagnostic)
        self.assertIn("ClientLuaLibrary.FrameAdvance", diagnostic)
        self.assertIn("detected raw='missing'", diagnostic)

    def test_method_without_lua_registration_is_rejected(self) -> None:
        lock = _synthetic_lock()
        attributes = ATTRIBUTE_METADATA.splitlines()[0:2]
        with tempfile.TemporaryDirectory() as temporary:
            home = Path(temporary) / "explicit-user-install"
            _write_install(home, lock)

            with self.assertRaises(DependencyError) as raised:
                preflight_installation(
                    home,
                    lock,
                    version_probe=_version_probe("Version: 2.11.0.0"),
                    metadata_probe=_metadata_probe(
                        attribute_output="\n".join(attributes)
                    ),
                )

        diagnostic = str(raised.exception)
        self.assertIn("emu.frameadvance", diagnostic)
        self.assertIn("LuaMethod('frameadvance')", diagnostic)

    def test_comment_and_string_example_decoys_are_rejected(self) -> None:
        lock = _synthetic_lock()
        decoys = (
            "-- client.invisibleemulation(true)\nreturn true\n",
            "local note = 'client.invisibleemulation(true)'\nreturn note\n",
            "--[=[ client.invisibleemulation(true) ]=]\nreturn true\n",
            "fakeclient.invisibleemulation(true)\n",
            "client.invisibleemulation_removed(true)\n",
        )
        for source in decoys:
            with self.subTest(source=source), tempfile.TemporaryDirectory() as temporary:
                home = Path(temporary) / "explicit-user-install"
                _write_install(home, lock)
                example = home / "Lua" / "GBA" / "SonicAdvance_CamHack.lua"
                example.write_text(source, encoding="utf-8")

                with self.assertRaises(DependencyError) as raised:
                    preflight_installation(
                        home,
                        lock,
                        version_probe=_version_probe("Version: 2.11.0.0"),
                        metadata_probe=_metadata_probe(),
                    )
                diagnostic = str(raised.exception)
                self.assertIn("client.invisibleemulation", diagnostic)
                self.assertIn("detected raw='missing'", diagnostic)
                self.assertIn("expected raw='client.invisibleemulation'", diagnostic)

    def test_exact_example_call_tokens_allow_whitespace(self) -> None:
        lock = _synthetic_lock()
        with tempfile.TemporaryDirectory() as temporary:
            home = Path(temporary) / "explicit-user-install"
            _write_install(home, lock)
            example = home / "Lua" / "GBA" / "SonicAdvance_CamHack.lua"
            example.write_text(
                "client  .  invisibleemulation  ( true )\n", encoding="utf-8"
            )

            report = preflight_installation(
                home,
                lock,
                version_probe=_version_probe("Version: 2.11.0.0"),
                metadata_probe=_metadata_probe(),
            )

        self.assertIn("client.invisibleemulation", report.lua_capabilities)

    def test_lua_launcher_rejects_wrong_version_before_emulator_start(self) -> None:
        lock = load_runtime_lock(LOCK_PATH)
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            home = root / "explicit BizHawk"
            _write_install(home, lock)
            fake_bin = root / "fake-bin"
            fake_bin.mkdir()
            monodis = fake_bin / "monodis"
            monodis.write_text(
                "#!/bin/sh\nprintf 'Assembly Table\\nVersion: 2.11.1.0\\n'\n",
                encoding="utf-8",
            )
            monodis.chmod(0o755)
            emulator_marker = root / "emulator-started"
            mono = fake_bin / "mono"
            mono.write_text(
                f"#!/bin/sh\ntouch {str(emulator_marker)!r}\n",
                encoding="utf-8",
            )
            mono.chmod(0o755)
            lua = root / "probe.lua"
            movie = root / "movie.bk2"
            rom = root / "rom.gen"
            work = root / "work"
            lua.write_text("return true\n", encoding="utf-8")
            movie.write_bytes(b"movie")
            rom.write_bytes(b"rom")
            work.mkdir()
            environment = os.environ.copy()
            environment.update(
                {
                    "BIZHAWK_HOME": str(home),
                    "MONO_BIN": str(mono),
                    "OGGF_WORKDIR": str(work),
                    "PATH": f"{fake_bin}:{environment['PATH']}",
                }
            )

            process = subprocess.run(
                [
                    str(ROOT / "bizhawk" / "run_bizhawk_lua.sh"),
                    str(lua),
                    str(movie),
                    str(rom),
                ],
                env=environment,
                capture_output=True,
                text=True,
                check=False,
            )

        self.assertEqual(2, process.returncode, process.stdout + process.stderr)
        self.assertIn("detected raw='Version: 2.11.1.0'", process.stderr)
        self.assertIn("expected raw='Version: 2.11.0.0'", process.stderr)
        self.assertFalse(emulator_marker.exists())

    def test_native_environment_rejects_wrong_version_before_build(self) -> None:
        lock = load_runtime_lock(LOCK_PATH)
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            home = root / "explicit BizHawk"
            _write_install(home, lock)
            fake_bin = root / "fake-bin"
            fake_bin.mkdir()
            for command, body in (
                ("mono", "exit 0"),
                ("xbuild", "exit 0"),
                ("monodis", "printf 'Assembly Table\\nVersion: 2.12.0.0\\n'"),
            ):
                executable = fake_bin / command
                executable.write_text(f"#!/bin/sh\n{body}\n", encoding="utf-8")
                executable.chmod(0o755)
            environment = os.environ.copy()
            environment.update(
                {
                    "BIZHAWK_HOME": str(home),
                    "PATH": f"{fake_bin}:{environment['PATH']}",
                }
            )

            process = subprocess.run(
                [
                    "bash",
                    "-c",
                    'source "$1"',
                    "bash",
                    str(ROOT / "bizhawk-headless" / "common-env.sh"),
                ],
                env=environment,
                capture_output=True,
                text=True,
                check=False,
            )

        self.assertEqual(2, process.returncode, process.stdout + process.stderr)
        self.assertIn("detected raw='Version: 2.12.0.0'", process.stderr)
        self.assertIn("expected raw='Version: 2.11.0.0'", process.stderr)


class AcquisitionTests(unittest.TestCase):
    def test_atomic_publication_rejects_destination_that_appears_at_boundary(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / "TraceChaser"
            root.mkdir()
            archive_path = Path(temporary) / "offline.tar.gz"
            lock = _valid_offline_archive(archive_path)
            real_publish = _publish_directory_noreplace
            observed: dict[str, bool] = {}

            def publish_after_competitor(staged: Path, destination: Path) -> None:
                self.assertFalse(destination.exists())
                destination.mkdir()
                existing = destination / "existing"
                existing.write_text("keep", encoding="utf-8")
                try:
                    real_publish(staged, destination)
                except DependencyError:
                    observed["staged_preserved"] = (
                        staged.is_dir() and (staged / "EmuHawk.exe").is_file()
                    )
                    observed["destination_preserved"] = (
                        existing.read_text(encoding="utf-8") == "keep"
                    )
                    raise

            with mock.patch.object(
                bizhawk_dependency,
                "_publish_directory_noreplace",
                side_effect=publish_after_competitor,
            ) as publisher:
                with self.assertRaises(DependencyError) as raised:
                    acquire_archive(
                        root,
                        lock,
                        archive_path=archive_path,
                        version_probe=_version_probe("Version: 2.11.0.0"),
                        metadata_probe=_metadata_probe(),
                    )
            publisher.assert_called_once()

            destination = root / ".dependencies" / "BizHawk-2.11-linux-x64"
            self.assertEqual("keep", (destination / "existing").read_text())
            self.assertEqual(
                {"staged_preserved": True, "destination_preserved": True}, observed
            )
        diagnostic = str(raised.exception)
        self.assertIn("appeared and was left untouched", diagnostic)
        self.assertIn("errno=17 (File exists)", diagnostic)

    def test_legacy_check_then_rename_control_overwrites_competing_empty_destination(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            staged = root / "staged"
            destination = root / "destination"
            staged.mkdir()
            (staged / "payload").write_text("candidate", encoding="utf-8")

            self.assertFalse(destination.exists())
            destination.mkdir()
            os.rename(staged, destination)

            self.assertFalse(staged.exists())
            self.assertEqual(
                "candidate", (destination / "payload").read_text(encoding="utf-8")
            )

    def test_wrong_archive_hash_is_rejected_before_extraction(self) -> None:
        lock = _synthetic_lock("f" * 64)
        extracted = []
        with tempfile.TemporaryDirectory() as temporary:
            archive = Path(temporary) / "candidate.tar.gz"
            archive.write_bytes(b"wrong archive")

            with self.assertRaises(DependencyError) as raised:
                verify_archive(archive, lock, extractor=lambda *_args: extracted.append(True))

        self.assertEqual([], extracted)
        diagnostic = str(raised.exception)
        self.assertIn(
            f"detected raw='{hashlib.sha256(b'wrong archive').hexdigest()}'", diagnostic
        )
        self.assertIn(f"expected raw='{'f' * 64}'", diagnostic)

    def test_parent_traversal_archive_is_rejected_without_escape_write(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            payload = io.BytesIO()
            with tarfile.open(fileobj=payload, mode="w:gz") as archive:
                member = tarfile.TarInfo("../escaped-by-traversal/sentinel")
                content = b"must not escape"
                member.size = len(content)
                archive.addfile(member, io.BytesIO(content))
            archive_path = Path(temporary) / "traversal.tar.gz"
            archive_path.write_bytes(payload.getvalue())
            control_area = root / "control"
            (control_area / "escaped-symlink-target").mkdir(parents=True)
            control_staging = control_area / "staging"
            control_staging.mkdir()

            def require_rejection(extractor) -> None:
                with self.assertRaises(DependencyError):
                    extractor(archive_path, control_staging)

            # Mutation control: fully_trusted must fail the rejection assertion
            # and demonstrate the exact write the production filter prevents.
            with self.assertRaises(AssertionError):
                require_rejection(_extract_fully_trusted)
            self.assertEqual(
                content,
                (control_area / "escaped-by-traversal" / "sentinel").read_bytes(),
            )

            secure_area = root / "secure"
            secure_staging = secure_area / "staging"
            secure_staging.mkdir(parents=True)
            with self.assertRaises(DependencyError) as raised:
                _extract_archive(archive_path, secure_staging)

            self.assertIs(
                type(raised.exception.__cause__), tarfile.OutsideDestinationError
            )
            self.assertFalse(
                (secure_area / "escaped-by-traversal" / "sentinel").exists()
            )
            escaped_sentinels = [
                path
                for path in secure_area.rglob("sentinel")
                if not path.is_relative_to(secure_staging)
            ]
            self.assertEqual([], escaped_sentinels)

    def test_escaping_symlink_archive_is_rejected_without_escape_write(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            payload = io.BytesIO()
            with tarfile.open(fileobj=payload, mode="w:gz") as archive:
                link = tarfile.TarInfo("escaping-link")
                link.type = tarfile.SYMTYPE
                link.linkname = "../escaped-symlink-target"
                archive.addfile(link)
                content = b"must not follow escaping link"
                sentinel = tarfile.TarInfo("escaping-link/sentinel")
                sentinel.size = len(content)
                archive.addfile(sentinel, io.BytesIO(content))
            archive_path = Path(temporary) / "symlink.tar.gz"
            archive_path.write_bytes(payload.getvalue())
            control_area = root / "control"
            (control_area / "escaped-symlink-target").mkdir(parents=True)
            control_staging = control_area / "staging"
            control_staging.mkdir()

            def require_rejection(extractor) -> None:
                with self.assertRaises(DependencyError):
                    extractor(archive_path, control_staging)

            # Mutation control: fully_trusted follows the link and writes out.
            with self.assertRaises(AssertionError):
                require_rejection(_extract_fully_trusted)
            self.assertEqual(
                content,
                (control_area / "escaped-symlink-target" / "sentinel").read_bytes(),
            )

            secure_area = root / "secure"
            (secure_area / "escaped-symlink-target").mkdir(parents=True)
            secure_staging = secure_area / "staging"
            secure_staging.mkdir()
            with self.assertRaises(DependencyError) as raised:
                _extract_archive(archive_path, secure_staging)

            self.assertIs(
                type(raised.exception.__cause__), tarfile.LinkOutsideDestinationError
            )
            self.assertFalse(
                (secure_area / "escaped-symlink-target" / "sentinel").exists()
            )
            escaped_sentinels = [
                path
                for path in secure_area.rglob("sentinel")
                if not path.is_relative_to(secure_staging)
            ]
            self.assertEqual([], escaped_sentinels)

    def test_special_device_archive_is_rejected_without_device_creation(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            payload = io.BytesIO()
            with tarfile.open(fileobj=payload, mode="w:gz") as archive:
                member = tarfile.TarInfo("special-entry")
                member.type = tarfile.FIFOTYPE
                archive.addfile(member)
            archive_path = Path(temporary) / "special.tar.gz"
            archive_path.write_bytes(payload.getvalue())
            control_staging = root / "control" / "staging"
            control_staging.mkdir(parents=True)

            def require_rejection(extractor) -> None:
                with self.assertRaises(DependencyError):
                    extractor(archive_path, control_staging)

            # Mutation control: fully_trusted creates the forbidden FIFO.
            with self.assertRaises(AssertionError):
                require_rejection(_extract_fully_trusted)
            self.assertTrue(
                stat.S_ISFIFO(os.lstat(control_staging / "special-entry").st_mode)
            )

            secure_area = root / "secure"
            secure_staging = secure_area / "staging"
            secure_staging.mkdir(parents=True)
            with self.assertRaises(DependencyError) as raised:
                _extract_archive(archive_path, secure_staging)

            self.assertIs(
                type(raised.exception.__cause__), tarfile.SpecialFileError
            )
            self.assertFalse((secure_staging / "special-entry").exists())
            escaped_sentinels = [
                path
                for path in secure_area.rglob("sentinel")
                if not path.is_relative_to(secure_staging)
            ]
            self.assertEqual([], escaped_sentinels)

    def test_offline_archive_is_verified_staged_and_published_only_under_dependencies(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / "TraceChaser source"
            root.mkdir()
            payload = io.BytesIO()
            with tarfile.open(fileobj=payload, mode="w:gz") as archive:
                for relative, content in (
                    ("EmuHawk.exe", b"managed"),
                    (
                        "dll/BizHawk.Client.Common.dll",
                        b"ClientLuaLibrary\0invisibleemulation\0"
                        b"EmulationLuaLibrary\0frameadvance\0",
                    ),
                    ("dll/NLua.dll", b"nlua"),
                    (
                        "Lua/GBA/SonicAdvance_CamHack.lua",
                        b"client.invisibleemulation(true)\n",
                    ),
                ):
                    info = tarfile.TarInfo(
                        f"BizHawk-2.11-linux-x64/{relative}"
                    )
                    info.size = len(content)
                    archive.addfile(info, io.BytesIO(content))
            archive_path = Path(temporary) / "offline input.tar.gz"
            archive_path.write_bytes(payload.getvalue())
            lock = _synthetic_lock(hashlib.sha256(payload.getvalue()).hexdigest())

            destination = acquire_archive(
                root,
                lock,
                archive_path=archive_path,
                version_probe=_version_probe("Version: 2.11.0.0"),
                metadata_probe=_metadata_probe(),
            )

            self.assertEqual(
                root / ".dependencies" / "BizHawk-2.11-linux-x64", destination
            )
            self.assertTrue((destination / "EmuHawk.exe").is_file())
            self.assertFalse(any(root.glob(".bizhawk-*")))
            self.assertEqual(payload.getvalue(), archive_path.read_bytes())


class PlcDirectEntryPointTests(unittest.TestCase):
    def test_plc_contract_direct_script_entry_point_runs(self) -> None:
        environment = os.environ.copy()
        environment["LUA_BIN"] = "tracechaser-task6-deliberately-missing-lua"
        process = subprocess.run(
            ["python3", "-B", str(ROOT / "testing" / "test_plc_probe_contract.py")],
            cwd=ROOT,
            env=environment,
            capture_output=True,
            text=True,
            check=False,
        )

        self.assertEqual(0, process.returncode, process.stdout + process.stderr)
        self.assertIn("skipped=1", process.stderr)


if __name__ == "__main__":
    unittest.main()
