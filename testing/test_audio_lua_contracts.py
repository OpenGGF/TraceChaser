import json
import os
import shutil
import subprocess
import tempfile
import unittest
from dataclasses import dataclass
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SUPPORTED_LUA_VERSION = "Lua 5.4"


@dataclass(frozen=True)
class LuaInterpreter:
    executable: str
    version: str


def resolve_lua(environment: dict[str, str] | None = None) -> LuaInterpreter | None:
    env = os.environ if environment is None else environment
    requested = env.get("LUA_BIN", "lua5.4")
    executable = shutil.which(requested, path=env.get("PATH"))
    if executable is None:
        return None
    process = subprocess.run([executable, "-v"], capture_output=True, text=True, check=False)
    if process.returncode != 0:
        raise AssertionError(f"{executable} -v failed: {process.stdout}{process.stderr}")
    return LuaInterpreter(executable, (process.stdout + process.stderr).strip())


def lua_54_or_skip(test: unittest.TestCase) -> LuaInterpreter:
    interpreter = resolve_lua()
    if interpreter is None:
        requested = os.environ.get("LUA_BIN", "lua5.4")
        test.skipTest(f"Lua executable is unavailable on PATH: {requested}")
    test.assertRegex(interpreter.version, r"^Lua 5\.4(?:\.\d+)?\b")
    print(
        f"Lua contract interpreter: {interpreter.executable} "
        f"({interpreter.version}); supported={SUPPORTED_LUA_VERSION}"
    )
    return interpreter


def _source(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8")


def _run_lua_contract(
    test: unittest.TestCase, arguments: list[Path], success_marker: str
) -> None:
    lua = lua_54_or_skip(test)
    process = subprocess.run(
        [lua.executable, *(str(argument) for argument in arguments)],
        capture_output=True,
        text=True,
        check=False,
    )
    output = process.stdout + process.stderr
    test.assertEqual(0, process.returncode, output)
    test.assertIn(success_marker, output)


class LuaInterpreterContractTests(unittest.TestCase):
    def test_default_interpreter_is_supported_lua_54(self) -> None:
        lua_54_or_skip(self)

    def test_lua_bin_override_is_resolved_through_path(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            fake = Path(temporary) / "reviewed-lua"
            fake.write_text("#!/bin/sh\nprintf 'Lua 5.4.99 test interpreter\\n'\n", encoding="utf-8")
            fake.chmod(0o700)
            environment = {"LUA_BIN": "reviewed-lua", "PATH": temporary}

            interpreter = resolve_lua(environment)

            self.assertIsNotNone(interpreter)
            assert interpreter is not None
            self.assertEqual(str(fake), interpreter.executable)
            self.assertEqual("Lua 5.4.99 test interpreter", interpreter.version)


class PureLuaAudioContractTests(unittest.TestCase):
    def test_s1_complete_run_queue_priority_lifecycle_and_dac_semantics(self) -> None:
        _run_lua_contract(
            self,
            [
                ROOT / "bizhawk" / "audio" / "s1_complete_run_audio_contract_test.lua",
                ROOT / "bizhawk" / "audio" / "s1_complete_run_audio_contract.lua",
            ],
            "S1_COMPLETE_RUN_AUDIO_CONTRACT_OK",
        )

    def test_s1_parity_contract_reproduces_hand_derived_vector(self) -> None:
        _run_lua_contract(
            self,
            [
                ROOT / "testing" / "lua" / "s1_audio_parity_contract_test.lua",
                ROOT / "bizhawk" / "audio" / "s1_audio_parity_contract.lua",
                ROOT / "contracts" / "audio" / "normalization-contract-v1.json",
            ],
            "S1_AUDIO_PARITY_CONTRACT_OK",
        )

    def test_s1_gameplay_timeline_preserves_queue_and_contention_semantics(self) -> None:
        _run_lua_contract(
            self,
            [
                ROOT / "testing" / "lua" / "s1_gameplay_audio_timeline_contract_test.lua",
                ROOT / "bizhawk" / "audio" / "s1_gameplay_audio_timeline_contract.lua",
            ],
            "S1_GAMEPLAY_AUDIO_TIMELINE_CONTRACT_OK",
        )


class S1CompleteRunProbeContractTests(unittest.TestCase):
    PROBE = "bizhawk/probes/s1_complete_run_audio_probe.lua"

    def test_probe_is_read_only_and_pins_m68k_lifecycle_and_loader_sites(self) -> None:
        source = _source(self.PROBE)
        for token in (
            "ProbeRuntime.run({",
            "s1_complete_run_audio_contract.lua",
            "expectedOpcode",
            "860",
            "225101",
            "frame_service_counts",
            "musicRoleByTrackRam",
            "loader_roles",
            "M68K A5",
        ):
            self.assertIn(token, source)
        addresses = (
            "0x138E", "0x1394", "0x139A", "0x71B4C", "0x71BB2", "0x71F02", "0x71F4C",
            "0x71C4C", "0x71F26", "0x71F2C", "0x71FCE", "0x71FD0", "0x71FD2", "0x71FE6",
            "0x71FF8", "0x72012", "0x72018", "0x7202C", "0x72098", "0x72126", "0x72182",
            "0x7218E", "0x721B6", "0x721B8", "0x721C6", "0x721CA", "0x721CE", "0x721D2",
            "0x721D6", "0x721DA", "0x721F4", "0x7222E", "0x7227C", "0x7230C", "0x72310",
            "0x72314", "0x72318", "0x7231C", "0x72320", "0x7234C", "0x7236E", "0x722C6",
            "0x723C6", "0x7259E", "0x725BC", "0x7267C", "0x72688", "0x7268E", "0x726D6",
            "0x726DC", "0x726E0", "0x72B14", "0x72B1E", "0x72B24", "0x72B3A", "0x72B66",
            "0x72B70", "0x72B82", "0x72B88", "0x72B8E", "0x72B9A", "0x72B9C", "0x72C22",
            "0x72C24", "0x72E02", "0x72E04",
        )
        for address in addresses:
            with self.subTest(address=address):
                self.assertIn(address, source)
        for continuation in ("0x71BD4", "0x71BE6", "0x71BF8", "0x71C10", "0x71C22", "0x71C38", "0x71C44"):
            self.assertIn(continuation, source)
        for role_byte in ("0x06", "0x00", "0x01", "0x02", "0x04", "0x05", "0x80", "0xA0", "0xC0"):
            self.assertIn(role_byte, source)
        for frame in ("3698", "3699", "3702", "3910"):
            self.assertIn(frame, source)
        self.assertIn("frame < FIRST_FRAME", source)
        self.assertIn("baseline", source)
        self.assertNotIn("oneTickPerFrame", source)
        for forbidden in (
            "mainmemory.write", "memory.write", "joypad.set", "savestate.",
            "emu.setregister", "io.open", "event.onmemoryexecute", "event.onmemorywrite",
        ):
            self.assertNotIn(forbidden, source)

    def test_probe_consumes_typed_z80_dac_services_without_m68k_parent(self) -> None:
        source = _source(self.PROBE)
        for token in (
            "typed_z80_dac", "acceptTypedZ80Service", "source_cpu", "Z80",
            "z80_dpcm_byte", "z80_sega_pcm_byte", "0x77", "0x86", "0x89", "0x9C",
            "0x9F", "0xAC", "0xC1", "0xC2", "0xC5", "0xD0", "raw_chip_events",
        ):
            self.assertIn(token, source)
        self.assertIn("requires_m68k_parent = false", source)
        self.assertNotIn("allWritesInsideUpdateMusic", source)
        self.assertNotIn("assert(activeInvocation", source)


class S1AudioParityProbeContractTests(unittest.TestCase):
    PROBE = "bizhawk/probes/s1_audio_driver_parity_probe.lua"

    def test_observer_is_runtime_owned_read_only_and_covers_reviewed_sites(self) -> None:
        source = _source(self.PROBE)
        for token in (
            "ProbeRuntime.run({",
            "s1_audio_parity_contract.lua",
            "mainmemory.read_u8",
            "0xF000",
            "expectedOpcode",
            "verifyFallbackManifest",
            "readManifestValue",
            "pc_manifest",
            "OGGF_AUDIO_FORCE_PC_MANIFEST",
            "manifest_sites = #fallbackManifest",
            'memory.read_u8(address, "System Bus")',
            "assertNoCommandContamination",
            "M68K D7",
            "& 0xFF",
            "acceptBgm",
            "newInvocationLifecycle",
            "newCallbackProof",
            "assertVerified",
            "ProbeRuntime.siblingPath(runtimePath",
            "OGGF_BIZHAWK_MOVIE_SHA256",
            "requireSha256",
            "readU8(0x2A) == 0",
            "readU8(base + 0x0C)",
            "volumeEnvelopeIndex",
            "continueAfterMovie = true",
            "joypad.get(1)",
            "joypad.get(2)",
            "context.log(",
        ):
            with self.subTest(token=token):
                self.assertIn(token, source)
        for forbidden in ("mainmemory.write", "memory.write", "joypad.set", "savestate.", "emu.setregister", "io.open"):
            self.assertNotIn(forbidden, source)
        for address in (
            "0x71B4C", "0x71C4C", "0x71FD0", "0x71FD2", "0xA04000", "0xA04001",
            "0xA04002", "0xA04003", "0xC00011", "0x7273A", "0x72752", "0x72770",
            "0x72788", "0x7225E", "0x72268", "0x723B6", "0x723C0", "0x7246A",
            "0x724DC", "0x72912", "0x72918", "0x72984", "0x729AE", "0x729BC",
            "0x729C0", "0x729C4", "0x729C8", "0x72DFA", "0x72E16", "0x71F02", "0x71F4C",
        ):
            self.assertIn(address, source)
        for operand in (
            "M68K D0", "M68K D1", "M68K D4", "M68K D6", "$1F(A0)", "$1F(A5)",
            "-1(A4)", "#$9F", "#$BF", "#$DF", "#$FF",
        ):
            self.assertIn(operand, source)

    def test_linux_launcher_supplies_digest_of_actual_movie_bytes(self) -> None:
        if shutil.which("bash") is None or shutil.which("sha256sum") is None:
            self.skipTest("Linux launcher dependencies are unavailable on PATH")
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            home = root / "bizhawk"
            lock = json.loads(
                (
                    ROOT
                    / "dependencies"
                    / "bizhawk-2.11-linux-x64.lock.json"
                ).read_text(encoding="utf-8")
            )
            for relative in lock["required_files"]:
                path = home / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b"fixture")
            (home / "dll" / "BizHawk.Client.Common.dll").write_bytes(
                b"\0".join(
                    marker.encode("utf-8")
                    for capability in lock["lua_capabilities"]
                    for marker in (
                        capability["library_marker"], capability["method_marker"]
                    )
                )
            )
            for capability in lock["lua_capabilities"]:
                if "example_path" in capability:
                    (home / capability["example_path"]).write_text(
                        capability["example_marker"], encoding="utf-8"
                    )
            lua = root / "probe.lua"
            lua.write_text("return true\n", encoding="utf-8")
            movie = root / "movie.bk2"
            movie.write_text("wrong movie content\n", encoding="utf-8")
            rom = root / "rom.gen"
            rom.write_text("rom\n", encoding="utf-8")
            fake_mono = root / "fake-mono.sh"
            fake_mono.write_text(
                "#!/bin/sh\nprintf 'MOVIE_SHA=%s\\n' \"$OGGF_BIZHAWK_MOVIE_SHA256\"\n",
                encoding="utf-8",
            )
            fake_mono.chmod(0o700)
            fake_bin = root / "fake-bin"
            fake_bin.mkdir()
            fake_monodis = fake_bin / "monodis"
            fake_monodis.write_text(
                "#!/bin/sh\nprintf 'Version: 2.11.0.0\\n'\n", encoding="utf-8"
            )
            fake_monodis.chmod(0o700)
            work = root / "work"
            work.mkdir()
            environment = os.environ.copy()
            environment.update(
                {
                    "BIZHAWK_HOME": str(home),
                    "MONO_BIN": str(fake_mono),
                    "OGGF_WORKDIR": str(work),
                    "OGGF_BIZHAWK_MOVIE_SHA256": "caller-value-must-not-win",
                    "PATH": f"{fake_bin}:{os.environ['PATH']}",
                }
            )
            environment.pop("OGGF_TRACE_OUTPUT_DIR", None)
            process = subprocess.run(
                [
                    shutil.which("bash") or "bash",
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
            output = process.stdout + process.stderr
            self.assertEqual(0, process.returncode, output)
            self.assertIn(
                "MOVIE_SHA=22957c24718a1d19fee7dfecb153002b00c2a35b98662b9548140e9227b784f3",
                output,
            )


class S1GameplayAudioProbeContractTests(unittest.TestCase):
    PROBE = "bizhawk/probes/s1_ghz1_gameplay_audio_timeline_probe.lua"

    def test_probe_is_read_only_pinned_and_uses_timeline_contract(self) -> None:
        source = _source(self.PROBE)
        for token in (
            "ProbeRuntime.run({", "s1_gameplay_audio_timeline_contract.lua", "context.log(",
            "s1_gameplay_audio_timeline.v2", "0x138E", "0x1394", "0x139A", "0x71F02",
            "0x71F4C", "0x71FD2", "0x721C6", "0x721F4", "0x7230C", "0x71B4C",
            "0x71C4C", "0x81", "860", "4975",
            "f2e817936d07b2b1f2b80d61451f174189509a2817da2b2349ce0e19b8a5567b",
            "expectedOpcode", "mainmemory.read_u8", "movie.length()", "Genesis Plus GX", "2.11",
            "18", "newQueueBuffer", "baselineMusicId", "queueBuffer:consume",
            "cycle(queues, retained, readU8(0x09))", "assertSelectedIdentity", "selected_sound_id",
        ):
            with self.subTest(token=token):
                self.assertIn(token, source)
        self.assertNotIn("cycledBySoundId", source)
        normal_init = source.index("local function normalRoleInitialized()")
        special_init = source.index("local function specialRoleInitialized()")
        self.assertLess(normal_init, special_init)
        self.assertIn("Timeline.assertSelectedIdentity", source[normal_init:special_init])
        self.assertIn("Timeline.assertSelectedIdentity", source[special_init:])
        resolved = source.index("local function normalIdResolved()")
        self.assertIn("M68K D7", source[resolved:normal_init])
        self.assertNotIn("M68K D7", source[normal_init:special_init])
        for forbidden in (
            "mainmemory.write", "memory.write", "joypad.set", "savestate.",
            "emu.setregister", "io.open", "event.onmemoryexecute", "event.onmemorywrite", "client.exit",
        ):
            self.assertNotIn(forbidden, source)


if __name__ == "__main__":
    unittest.main()
