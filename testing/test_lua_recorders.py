import json
import os
import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
BIZHAWK = ROOT / "bizhawk"


def _source(name: str) -> str:
    return (BIZHAWK / name).read_text(encoding="utf-8")


def assert_canonical_s3k_input_wrapper(script: str) -> None:
    wrapper_start = script.find("local function bk2_input_mask(")
    wrapper_end = script.find("local function write_aux(", wrapper_start)
    if wrapper_start < 0 or wrapper_end <= wrapper_start:
        raise AssertionError("s3k recorder must retain the shared BK2 input wrapper")
    canonical = """local function bk2_input_mask(fallback_raw, trace_row)
    return C.bk2_input_mask(
        fallback_raw, trace_row, bk2_frame_offset, 0)
end
"""
    actual = script[wrapper_start:wrapper_end]
    if re.sub(r"\s+", "", canonical) != re.sub(r"\s+", "", actual):
        raise AssertionError("input wrapper must contain exactly one canonical zero-adjustment call")


class S3kInputWrapperMutationTests(unittest.TestCase):
    def test_alternate_adjusted_call_is_rejected(self) -> None:
        mutated = """local function bk2_input_mask(fallback_raw, trace_row)
    local shifted = C.bk2_input_mask(fallback_raw, trace_row, bk2_frame_offset, -1)
    return C.bk2_input_mask(fallback_raw, trace_row, bk2_frame_offset, 0)
end
local function write_aux()
end
"""
        with self.assertRaises(AssertionError):
            assert_canonical_s3k_input_wrapper(mutated)


class AnimationRecorderContractTests(unittest.TestCase):
    def test_all_gameplay_recorders_emit_symmetric_animation_columns(self) -> None:
        recorders = (
            "s1_trace_recorder.lua",
            "s1_complete_run_recorder.lua",
            "s2_trace_recorder.lua",
            "s3k_trace_recorder.lua",
            "s3k_complete_run_recorder.lua",
        )
        for name in recorders:
            source = _source(name)
            for token in (
                '"recorder": "lua-bizhawk-diagnostic"',
                '"recorder_version": "3.0"',
                '"trace_schema": 5',
                "player_animation_id",
                "player_mapping_frame",
                "sidekick_animation_id",
                "sidekick_mapping_frame",
                "life_count",
                "ADDR_LIFE_COUNT",
                "0xFE12",
            ):
                with self.subTest(recorder=name, token=token):
                    self.assertIn(token, source)

    def test_recorders_read_native_animation_and_displayed_mapping_bytes(self) -> None:
        s1 = _source("s1_trace_recorder.lua")
        s2 = _source("s2_trace_recorder.lua")
        s3k = _source("s3k_trace_recorder.lua")
        for token in ("OFF_ANIM_FRAME_DISP  = 0x1A", "OFF_ANIM_ID          = 0x1C"):
            self.assertIn(token, s1)
            self.assertIn(token, s2)
        self.assertIn("OFF_ANIM_ID           = 0x20", s3k)
        self.assertIn("mapping_frame = mainmemory.read_u8(base + 0x22)", s3k)

    def test_s3k_recorders_support_physics_animation_only_regeneration(self) -> None:
        for name in ("s3k_trace_recorder.lua", "s3k_complete_run_recorder.lua"):
            source = _source(name)
            for token in (
                "OGGF_TRACE_ENABLE_DIAGNOSTIC_HOOKS",
                "physics_animation_aux_without_diagnostic_hooks",
                "LIGHTWEIGHT_REGEN = not DIAGNOSTIC_HOOKS_ENABLED",
                "if LIGHTWEIGHT_REGEN then",
            ):
                with self.subTest(recorder=name, token=token):
                    self.assertIn(token, source)

    def test_s3k_recorder_metadata_omits_retired_replay_phase_controls(self) -> None:
        source = _source("s3k_trace_recorder.lua")
        for retired in (
            "pre_level_intro_prefix",
            "sidekick_seed_frame_prelude",
            "pre_trace_osc_frames",
        ):
            self.assertNotIn(retired, source)
        for token in (
            '"trace_profile"',
            '"bk2_frame_offset"',
            '"recorder": "lua-bizhawk-diagnostic"',
            '"recorder_version": "3.0"',
            '"trace_schema": 5',
            "OGGF_TRACE_ENABLE_DIAGNOSTIC_HOOKS",
            "OGGF_TRACE_QUIET",
            "LIGHTWEIGHT_REGEN = not DIAGNOSTIC_HOOKS_ENABLED",
            "if LIGHTWEIGHT_REGEN then",
        ):
            self.assertIn(token, source)

    def test_s3k_recorder_uses_canonical_bk2_offset_for_every_profile(self) -> None:
        assert_canonical_s3k_input_wrapper(_source("s3k_trace_recorder.lua"))

    def test_s1_complete_run_disambiguates_repeated_segment_directories(self) -> None:
        source = _source("s1_complete_run_recorder.lua")
        for token in (
            "function next_segment_dir_token(base_token)",
            'local dir_token = next_segment_dir_token("ss")',
            "local dir_token = next_segment_dir_token(start_zone_name .. tostring(start_act + 1))",
            '"recorder_version": "3.0"',
        ):
            self.assertIn(token, source)

    def test_s1_complete_run_can_capture_focused_final_zone_rng_calls(self) -> None:
        source = _source("s1_complete_run_recorder.lua")
        for token in (
            "OGGF_S1_RNG_CALL_RANGE",
            "ADDR_RANDOM_NUMBER = 0x0029AC",
            "event.onmemoryexecute(S1_RNG_CALLS.record_hit, ADDR_RANDOM_NUMBER)",
            "S1_RNG_CALLS.flush()",
            "rng_call_per_frame",
            "OGGF_TRACE_SOURCE_BK2",
            '"recorder_version": "3.0"',
        ):
            self.assertIn(token, source)


class RecorderCounterAddressContractTests(unittest.TestCase):
    def test_sonic1_uses_disassembly_backed_execution_counters(self) -> None:
        source = _source("s1_trace_recorder.lua")
        self.assertIn("local ADDR_FRAMECOUNT      = 0xFE04", source)
        self.assertIn("local ADDR_VBLA_WORD       = 0xFE0E", source)

    def test_sonic2_uses_disassembly_backed_execution_counters(self) -> None:
        source = _source("s2_trace_recorder.lua")
        self.assertIn("local ADDR_FRAMECOUNT      = 0xFE04", source)
        self.assertIn("local ADDR_VBLA_WORD       = 0xFE0E", source)

    def test_sonic3k_uses_disassembly_backed_execution_counters(self) -> None:
        source = _source("s3k_trace_recorder.lua")
        self.assertIn("local ADDR_FRAMECOUNT       = 0xFE04", source)
        self.assertIn("local ADDR_VBLA_WORD        = 0xFE0E", source)
        self.assertIn("local ADDR_LAG_FRAME_COUNT  = 0xF628", source)

    def test_sonic3k_complete_run_uses_the_same_execution_counters(self) -> None:
        source = _source("s3k_complete_run_recorder.lua")
        self.assertIn("local ADDR_FRAMECOUNT       = 0xFE04", source)
        self.assertIn("local ADDR_VBLA_WORD        = 0xFE0E", source)
        self.assertIn("local ADDR_LAG_FRAME_COUNT  = 0xF628", source)


class S2SpecialStageRecorderContractTests(unittest.TestCase):
    def test_recorder_declares_bounded_rev01_recurring_pass_and_control_hooks(self) -> None:
        source = _source("s2_ss_trace_recorder.lua")
        for token in (
            "local PC_READ_JOYPADS_RETURN = 0x1156",
            "local VINT_S2SS_READ_JOYPADS_RETURN_PC = 0x88E",
            "local CTRL_2_READ_COMPLETE_A0 = 0xF608",
            "local PC_S2SS_POST_RUN_OBJECTS = 0x52B2",
            "s2ss_recurring_post_run_objects",
            "s2ss_input_sample",
            "event.unregisterbyname",
            '"type":"run_objects_end"',
            '"type":"control_state"',
            "ADDR_SPECIAL_STAGE_STARTED",
            "first_eligible_frame",
            "pass_sequence",
            "completion_cursor_frame",
            "input_sample_frame",
            "input_sample_bk2_frame",
            "previous_input_sample_frame",
            "previous_input_sample_bk2_frame",
            "input_sample_sequence",
            "started_at_input_sample",
            "latest_input_sample.started_at_input_sample == 0",
            "vint_s2ss_read_joypads",
            "p1_held",
            "previous_p1_held",
            "mainmemory.read_u8(ADDR_SPECIAL_STAGE_STARTED) == 0",
            "prev_check_rings_flag == 0 and check_rings_flag ~= 0",
            "last_nonlag_trace_frame",
            "publish_pending_finish_pass",
            '"observed_frame":%d,"type":"stage_finished"',
            '"type":"results_started"',
            "C.require_external_output_dir()",
        ):
            with self.subTest(token=token):
                self.assertIn(token, source)
        self.assertNotIn("PC_RUN_OBJECTS_END", source)
        self.assertEqual(2, source.count("event.onmemoryexecute("))
        self.assertNotIn('"type":"stage_finished","slot"', source)
        self.assertIn("OGGF_TRACE_OUTPUT_DIR", _source("record_s2_level_select_traces.ps1"))

    def test_workflow_validates_required_special_stage_aux_families(self) -> None:
        source = _source("record_s2_level_select_traces.ps1")
        for token in (
            "control_state",
            "run_objects_end",
            "stage_finished",
            "checkpoint",
            "message_state",
            "$multiPassObservationFrames",
            "$delayedRunObjectsPassCount",
            "vint_s2ss_read_joypads",
            "$previousP1Held",
            "$terminalFinishPassCount",
            "$stageFinishedFrame",
            "$stageFinishedEvents.Count -ne 1",
            "$resultsStartedEvent",
            "$firstEligibleAtOrAfterCompletion",
            "$previousInputBk2Index",
            "-le 2900",
        ):
            with self.subTest(token=token):
                self.assertIn(token, source)

    def test_workflow_builds_scratch_below_explicit_external_output(self) -> None:
        source = _source("record_s2_level_select_traces.ps1")
        for token in (
            "[string]$OutputRoot",
            "[string]$InputRepositoryRoot",
            'Join-Path $PSScriptRoot "assert_external_output.ps1"',
            "-InputRepositoryRoot $inputRepositoryFullPath -OutputRoot $OutputRoot",
            '(Join-Path $outputFullPath ".capture-work")',
        ):
            self.assertIn(token, source)
        self.assertNotIn('Join-Path $bizhawkToolsDir "trace_output"', source)


class BizHawkLuaToolingContractTests(unittest.TestCase):
    def test_linux_tooling_pins_recorder_compatible_211(self) -> None:
        ignore = (ROOT / ".gitignore").read_text(encoding="utf-8")
        fetch = _source("fetch_bizhawk_2_11_linux.sh")
        preflight = _source("preflight_bizhawk_2_11.sh")
        lock = json.loads(
            (ROOT / "dependencies" / "bizhawk-2.11-linux-x64.lock.json").read_text(
                encoding="utf-8"
            )
        )
        launcher = _source("run_bizhawk_lua.sh")
        readme = _source("README.md")
        self.assertIn(".dependencies/", ignore)
        self.assertEqual("2.11", lock["release"]["version"])
        self.assertEqual("BizHawk-2.11-linux-x64.tar.gz", lock["release"]["archive_name"])
        self.assertEqual(
            "cdaf9650d880bae660d63a388430f630b8d8a96b1ba59ebf0e0195a645c3bab8",
            lock["release"]["sha256"],
        )
        self.assertIn(
            "client.invisibleemulation",
            {capability["api"] for capability in lock["lua_capabilities"]},
        )
        self.assertIn('bizhawk_2_11.py" acquire', fetch)
        self.assertIn('bizhawk_2_11.py" preflight', preflight)
        self.assertTrue(os.access(BIZHAWK / "fetch_bizhawk_2_11_linux.sh", os.X_OK))
        self.assertTrue(os.access(BIZHAWK / "preflight_bizhawk_2_11.sh", os.X_OK))
        self.assertIn(".dependencies/BizHawk-2.11-linux-x64", launcher)
        self.assertNotIn(".dependencies/BizHawk-*-linux-x64", launcher)
        self.assertNotIn(".dependencies/BizHawk-2.11.1-linux-x64", launcher)
        self.assertNotIn("/opt/bizhawk", launcher)
        self.assertIn("BizHawk 2.11", readme)
        self.assertIn("2.11.1", readme)
        self.assertIn("client.invisibleemulation", readme)


if __name__ == "__main__":
    unittest.main()
