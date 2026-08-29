import hashlib
import io
import json
import os
import subprocess
import tarfile
import tempfile
import unittest
from pathlib import Path

from bizhawk.bizhawk_2_11 import (
    DependencyError,
    acquire_archive,
    load_runtime_lock,
    preflight_installation,
    verify_archive,
)
from testing.test_probe_contract import strip_lua_comments_and_strings


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
PROBE_PATHS = tuple(sorted((ROOT / "bizhawk" / "probes").glob("*.lua")))
LUA_API_REFERENCE_PATTERN = (
    r"\b(client|emu|event|mainmemory|memory|movie|joypad)\."
    r"([A-Za-z_][A-Za-z0-9_]*)\b"
)
NON_API_EVENT_FIELDS = {
    "event.begin_pc",
    "event.completion_pc",
    "event.kind",
    "event.raw_chip_events",
    "event.source_cpu",
}


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
                "library_marker": "ClientLuaLibrary",
                "method_marker": "invisibleemulation",
                "example_path": "Lua/GBA/SonicAdvance_CamHack.lua",
                "example_marker": "client.invisibleemulation",
            },
            {
                "api": "emu.frameadvance",
                "assembly": "dll/BizHawk.Client.Common.dll",
                "library_marker": "EmulationLuaLibrary",
                "method_marker": "frameadvance",
            },
        ],
    }


def _write_install(home: Path, lock: dict) -> None:
    for relative in lock["required_files"]:
        path = home / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b"synthetic")
    client = home / "dll" / "BizHawk.Client.Common.dll"
    client.write_bytes(
        b"\0".join(
            marker.encode("utf-8")
            for capability in lock["lua_capabilities"]
            for marker in (
                capability["library_marker"],
                capability["method_marker"],
            )
        )
    )
    for capability in lock["lua_capabilities"]:
        if "example_path" in capability:
            (home / capability["example_path"]).write_text(
                capability["example_marker"], encoding="utf-8"
            )


def _version_probe(raw: str):
    return lambda _path: raw


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

    def test_lock_capabilities_exactly_cover_recorder_and_probe_lua_calls(self) -> None:
        import re

        required = set()
        for path in (*RECORDER_PATHS, *PROBE_PATHS):
            executable = strip_lua_comments_and_strings(path.read_text(encoding="utf-8"))
            required.update(
                f"{namespace}.{method}"
                for namespace, method in re.findall(
                    LUA_API_REFERENCE_PATTERN, executable
                )
            )
        required.difference_update(NON_API_EVENT_FIELDS)
        locked = {
            capability["api"]
            for capability in load_runtime_lock(LOCK_PATH)["lua_capabilities"]
        }

        self.assertEqual(required, locked)


class PreflightTests(unittest.TestCase):
    def test_exact_211_managed_version_and_all_capabilities_are_accepted(self) -> None:
        lock = _synthetic_lock()
        with tempfile.TemporaryDirectory() as temporary:
            home = Path(temporary) / "explicit-user-install"
            _write_install(home, lock)

            report = preflight_installation(
                home,
                lock,
                version_probe=_version_probe("Version: 2.11.0.0"),
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

    def test_missing_capability_reports_detected_and_expected_marker(self) -> None:
        lock = _synthetic_lock()
        with tempfile.TemporaryDirectory() as temporary:
            home = Path(temporary) / "explicit-user-install"
            _write_install(home, lock)
            client = home / "dll" / "BizHawk.Client.Common.dll"
            client.write_bytes(client.read_bytes().replace(b"invisibleemulation", b"removed"))

            with self.assertRaises(DependencyError) as raised:
                preflight_installation(
                    home,
                    lock,
                    version_probe=_version_probe("Version: 2.11.0.0"),
                )

        diagnostic = str(raised.exception)
        self.assertIn("client.invisibleemulation", diagnostic)
        self.assertIn("detected raw='missing'", diagnostic)
        self.assertIn("expected raw='invisibleemulation'", diagnostic)

    def test_missing_shipped_invisibleemulation_example_is_rejected(self) -> None:
        lock = _synthetic_lock()
        with tempfile.TemporaryDirectory() as temporary:
            home = Path(temporary) / "explicit-user-install"
            _write_install(home, lock)
            example = home / "Lua" / "GBA" / "SonicAdvance_CamHack.lua"
            example.write_text("example capability removed\n", encoding="utf-8")

            with self.assertRaises(DependencyError) as raised:
                preflight_installation(
                    home,
                    lock,
                    version_probe=_version_probe("Version: 2.11.0.0"),
                )

        diagnostic = str(raised.exception)
        self.assertIn("client.invisibleemulation", diagnostic)
        self.assertIn("detected raw='missing'", diagnostic)
        self.assertIn("expected raw='client.invisibleemulation'", diagnostic)

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
                        b"client.invisibleemulation",
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
