import os
import subprocess
import tempfile
import unittest
from pathlib import Path

from testing.test_audio_lua_contracts import lua_54_or_skip


ROOT = Path(__file__).resolve().parents[1]
HARNESS = ROOT / "testing" / "lua" / "plc_timing_probe_contract_test.lua"
OLD_HARNESS = ROOT / "bizhawk" / "diagnostics" / "plc_timing_probe_contract_test.lua"
PROBES = (
    ROOT / "bizhawk" / "diagnostics" / "s1_plc_timing_probe.lua",
    ROOT / "bizhawk" / "diagnostics" / "s2_plc_timing_probe.lua",
)


def configure_probe_environment(output: Path) -> dict[str, str]:
    environment = os.environ.copy()
    environment.update(
        {
            "OGGF_PLC_PROBE_OUTPUT": str(output),
            "OGGF_PLC_PROBE_FLUSH_EACH_EVENT": "1",
            "OGGF_PLC_CONSUMER_HOOKS": "ready_gate@118",
            "OGGF_PLC_BUFFER_RAM": "1000",
            "OGGF_PLC_DEST_RAM": "1100",
            "OGGF_PLC_LEFT_RAM": "1102",
            "OGGF_PLC_GAME_MODE_RAM": "1104",
            "OGGF_PLC_INTERRUPT_HANDLER_RAM": "1105",
            "OGGF_PLC_LAG_HANDLER": "0",
            "OGGF_PLC_ADD_ENTRY": "101",
            "OGGF_PLC_ADD_POST": "115",
            "OGGF_PLC_REPLACE_BEGIN": "102",
            "OGGF_PLC_REPLACE_POST": "103",
            "OGGF_PLC_CLEAR_BEGIN": "104",
            "OGGF_PLC_CLEAR_POST": "105",
            "OGGF_PLC_PREPARE_BEGIN": "106",
            "OGGF_PLC_PREPARE_END": "107",
            "OGGF_PLC_FULL_SERVICE_PRE": "108",
            "OGGF_PLC_PARTIAL_SERVICE_POST": "109",
            "OGGF_PLC_SMALL_SERVICE_PRE": "110",
            "OGGF_PLC_POP_PRE": "111",
            "OGGF_PLC_POP_POST": "112",
            "OGGF_PLC_VINT_DISPATCH": "113",
            "OGGF_PLC_HBLANK_DEFERRED_ENTRY": "114",
        }
    )
    return environment


class PlcHarnessOwnershipTests(unittest.TestCase):
    def test_behavioral_harness_is_test_owned(self) -> None:
        self.assertTrue(HARNESS.is_file())
        self.assertFalse(OLD_HARNESS.exists())

    def test_both_probes_remain_present_for_structural_enumeration(self) -> None:
        self.assertEqual(2, len(PROBES))
        for probe in PROBES:
            with self.subTest(probe=probe.name):
                self.assertTrue(probe.is_file())


class PlcProbeBehaviorContractTests(unittest.TestCase):
    def test_both_state_machines_handle_empty_partial_and_completing_calls(self) -> None:
        lua = lua_54_or_skip(self)
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for probe in PROBES:
                with self.subTest(probe=probe.name):
                    output = root / f"{probe.name}.jsonl"
                    process = subprocess.run(
                        [lua.executable, str(HARNESS), str(probe)],
                        env=configure_probe_environment(output),
                        capture_output=True,
                        text=True,
                        check=False,
                    )
                    console = process.stdout + process.stderr
                    self.assertEqual(0, process.returncode, console)
                    self.assertIn("PLC_PROBE_CONTRACT_OK", console)
                    self.assertTrue(output.is_file())
                    self.assertGreater(output.stat().st_size, 0)


if __name__ == "__main__":
    unittest.main()
