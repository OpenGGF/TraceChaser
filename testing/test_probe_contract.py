import os
import re
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PROBE_ROOT = ROOT / "bizhawk" / "probes"
RUNTIME = PROBE_ROOT / "probe_runtime.lua"

FORBIDDEN_PROBE_OPERATIONS = (
    "event.onmemoryexecute",
    "event.onmemorywrite",
    "event.unregisterbyname",
    "emu.limitframerate",
    "client.speedmode",
    "client.invisibleemulation",
    "client.SetSoundOn",
    "client.exit",
    "io.open",
    "while true",
    "mainmemory.write",
    "memory.write",
    "joypad.set",
    "savestate.",
    "emu.setregister",
)


def _long_bracket_close(source: str, start: int) -> str | None:
    if start >= len(source) or source[start] != "[":
        return None
    cursor = start + 1
    while cursor < len(source) and source[cursor] == "=":
        cursor += 1
    if cursor >= len(source) or source[cursor] != "[":
        return None
    return "]" + "=" * (cursor - start - 1) + "]"


def strip_lua_comments_and_strings(source: str) -> str:
    """Blank Lua comments and string literals while preserving positions/newlines."""
    executable: list[str] = []
    line_comment = False
    long_close: str | None = None
    quote: str | None = None
    escaped = False
    index = 0
    while index < len(source):
        current = source[index]
        if line_comment:
            if current == "\n":
                line_comment = False
                executable.append("\n")
            else:
                executable.append(" ")
            index += 1
            continue
        if long_close is not None:
            if source.startswith(long_close, index):
                executable.append(" " * len(long_close))
                index += len(long_close)
                long_close = None
            else:
                executable.append("\n" if current == "\n" else " ")
                index += 1
            continue
        if quote is not None:
            executable.append("\n" if current == "\n" else " ")
            if escaped:
                escaped = False
            elif current == "\\":
                escaped = True
            elif current == quote:
                quote = None
            index += 1
            continue
        if source.startswith("--", index):
            close = _long_bracket_close(source, index + 2)
            if close is not None:
                opener_length = len(close) + 2
                executable.append(" " * opener_length)
                index += opener_length
                long_close = close
            else:
                executable.append("  ")
                index += 2
                line_comment = True
            continue
        close = _long_bracket_close(source, index)
        if close is not None:
            executable.append(" " * len(close))
            index += len(close)
            long_close = close
            continue
        if current in ("'", '"'):
            executable.append(" ")
            quote = current
            index += 1
            continue
        executable.append(current)
        index += 1
    return "".join(executable)


def declares_stage_gate(executable: str) -> bool:
    if "stage = function" in executable:
        return True
    for match in re.finditer(r"stage\s*=\s*([A-Za-z_][A-Za-z0-9_]*)", executable):
        gate = re.escape(match.group(1))
        if re.search(rf"(?:local\s+)?function\s+{gate}\s*\(", executable):
            return True
    return False


def probe_contract_errors(source: str) -> list[str]:
    executable = strip_lua_comments_and_strings(source)
    errors: list[str] = []
    if "ProbeRuntime.run({" not in executable:
        errors.append("must delegate to ProbeRuntime.run")
    if not declares_stage_gate(executable):
        errors.append("must declare a semantic stage gate")
    if "hooks = {" not in executable:
        errors.append("must declare deferred hooks")
    for forbidden in FORBIDDEN_PROBE_OPERATIONS:
        if forbidden in executable:
            errors.append(f"must not own {forbidden}")
    return errors


def find_probe_violations(probe_root: Path) -> list[str]:
    """Recursively return one diagnostic for every namespaced probe violation."""
    runtime = probe_root / "probe_runtime.lua"
    violations: list[str] = []
    for probe in sorted(probe_root.rglob("*.lua")):
        if not probe.is_file() or probe == runtime:
            continue
        errors = probe_contract_errors(probe.read_text(encoding="utf-8"))
        if errors:
            relative = probe.relative_to(probe_root).as_posix()
            violations.append(f"{relative}: {'; '.join(errors)}")
    return violations


def _lua_54_or_skip(test: unittest.TestCase) -> str:
    requested = os.environ.get("LUA_BIN", "lua5.4")
    executable = shutil.which(requested)
    if executable is None:
        test.skipTest(f"Lua executable is unavailable on PATH: {requested}")
    version = subprocess.run(
        [executable, "-v"], check=False, capture_output=True, text=True
    )
    banner = (version.stdout + version.stderr).strip()
    test.assertEqual(0, version.returncode, banner)
    test.assertRegex(banner, r"^Lua 5\.4(?:\.\d+)?\b")
    print(f"Lua contract interpreter: {executable} ({banner})")
    return executable


class ProbeEnumerationContractTests(unittest.TestCase):
    def test_nested_probe_violation_is_enumerated(self) -> None:
        # Break caught: replacing recursive discovery with a top-level glob lets
        # a nested probe reclaim hook/lifecycle ownership without detection.
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "probe_runtime.lua").write_text("return {}\n", encoding="utf-8")
            nested = root / "examples" / "bad_probe.lua"
            nested.parent.mkdir()
            nested.write_text("event.onmemoryexecute(function() end, 1)\n", encoding="utf-8")

            violations = find_probe_violations(root)
            self.assertEqual(1, len(violations))
            self.assertTrue(violations[0].startswith("examples/bad_probe.lua:"))
            self.assertIn("event.onmemoryexecute", violations[0])

    def test_every_namespaced_probe_uses_declarative_runtime_contract(self) -> None:
        self.assertTrue(PROBE_ROOT.is_dir(), f"missing probe namespace: {PROBE_ROOT}")
        probes = [path for path in PROBE_ROOT.rglob("*.lua") if path != RUNTIME]
        self.assertTrue(probes, "the probe namespace needs a contract example")
        self.assertEqual([], find_probe_violations(PROBE_ROOT))

    def test_long_strings_and_comments_cannot_spoof_contract(self) -> None:
        executable = strip_lua_comments_and_strings(
            """--[=[ ProbeRuntime.run({ stage = function hooks = { client.exit() ]=]
local decoy = [==[ event.onmemoryexecute client.exit() ]==]
ProbeRuntime.run({ stage = function() return true end, hooks = {} })
client.exit()
"""
        )
        self.assertNotIn("event.onmemoryexecute", executable)
        self.assertEqual(executable.index("ProbeRuntime.run"), executable.rindex("ProbeRuntime.run"))
        self.assertIn("client.exit()", executable)


class ProbeRuntimeContractTests(unittest.TestCase):
    def test_shared_runtime_owns_probe_lifecycle(self) -> None:
        source = RUNTIME.read_text(encoding="utf-8")
        executable = strip_lua_comments_and_strings(source)
        required = (
            "emu.limitframerate(false)",
            "client.speedmode(6400)",
            "client.invisibleemulation(true)",
            "config.stage()",
            "config.hooks",
            "event.onmemoryexecute",
            "event.onmemorywrite",
            "event.unregisterbyname",
            "outfile:flush()",
            "outfile:close()",
            "client.exit)",
            "movie.mode() ==",
            "config.continueAfterMovie",
            "config.onFrame",
            "movieFinished",
        )
        for token in required:
            with self.subTest(token=token):
                self.assertIn(token, executable)
        self.assertLess(executable.index("config.stage()"), executable.index("event.onmemoryexecute"))

    def test_shared_runtime_cleans_up_and_preserves_original_failures(self) -> None:
        lua = _lua_54_or_skip(self)
        harness = ROOT / "testing" / "lua" / "probe_runtime_contract_test.lua"
        process = subprocess.run(
            [lua, str(harness), str(RUNTIME)], capture_output=True, text=True, check=False
        )
        self.assertEqual(0, process.returncode, process.stdout + process.stderr)


class FastWrapperContractTests(unittest.TestCase):
    def test_fast_wrapper_delegates_one_shot_initialization_to_recorder(self) -> None:
        generator = (ROOT / "bizhawk" / "prepare_bizhawk_fast_lua.ps1").read_text(encoding="utf-8")
        launcher = (ROOT / "bizhawk" / "run_bizhawk_lua.bat").read_text(encoding="utf-8")
        for token in (
            "dofile(target)",
            "OGGF_BIZHAWK_PROBE_RUNTIME",
            "probe_runtime.lua",
            "$validatedSource",
            "$env:OGGF_BIZHAWK_PROBE_RUNTIME",
            'Join-Path $PSScriptRoot "probes\\probe_runtime.lua"',
            "[IO.Path]::IsPathRooted",
        ):
            with self.subTest(token=token):
                self.assertIn(token, generator)
        self.assertNotIn("pcall(client.invisibleemulation, true)", generator)
        self.assertNotIn("event.onframestart(apply_openggf_fast_headless", generator)
        self.assertIn("OGGF_BIZHAWK_PROBE_RUNTIME", launcher)
        self.assertIn("%~dp0probes\\probe_runtime.lua", launcher)

    def test_windows_validator_accepts_nested_probe_and_ignores_long_bracket_decoys(self) -> None:
        pwsh = shutil.which("pwsh")
        if pwsh is None:
            self.skipTest("PowerShell is unavailable on PATH")
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            nested = root / "nested" / "probe.lua"
            nested.parent.mkdir()
            nested.write_text(
                """--[=[ client.invisibleemulation(false) ]=]
local decoy = [==[ client.SetSoundOn(true) ]==]
local runtimePath = assert(os.getenv("OGGF_BIZHAWK_PROBE_RUNTIME"))
local ProbeRuntime = dofile(runtimePath)
ProbeRuntime.run({
    stage = function() return true end,
    hooks = {{ address = 0x123456, callback = function(context) context.finish() end }}
})
""",
                encoding="utf-8",
            )
            wrapper = root / "wrapper.lua"
            environment = os.environ.copy()
            environment["OGGF_BIZHAWK_PROBE_RUNTIME"] = str(RUNTIME)
            process = subprocess.run(
                [
                    pwsh,
                    "-NoLogo",
                    "-NoProfile",
                    "-File",
                    str(ROOT / "bizhawk" / "prepare_bizhawk_fast_lua.ps1"),
                    "-LuaScript",
                    str(nested),
                    "-WrapperPath",
                    str(wrapper),
                ],
                env=environment,
                capture_output=True,
                text=True,
                check=False,
            )
            self.assertEqual(0, process.returncode, process.stdout + process.stderr)
            self.assertIn(str(nested.resolve()), wrapper.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
