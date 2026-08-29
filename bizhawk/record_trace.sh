#!/usr/bin/env bash
# record_trace.sh — Linux mirror of record_trace.bat.
#
# Records a Sonic 1 trace by replaying a BK2 through s1_trace_recorder.lua under
# the reusable Linux launcher. The Lua script auto-detects zone/act from RAM and
# writes trace_output/ (physics.csv, aux_state.jsonl, metadata.json) into the
# working directory (override with OGGF_WORKDIR).
#
# Usage:  record_trace.sh <rom_path> <bk2_path> <external_output_dir>
#
# See run_bizhawk_lua.sh for the environment knobs (BIZHAWK_HOME, DISPLAY,
# software-GL, --luaconsole) and the KNOWN BLOCKER note about command-line BK2
# loading hanging on the BizHawk 2.11.1 + mono build.
set -euo pipefail

if [ "$#" -ne 3 ]; then
	echo "Usage: $(basename "$0") <rom_path> <bk2_path> <external_output_dir>" >&2
	echo "  rom_path   Path to Sonic 1 REV01 ROM" >&2
	echo "  bk2_path   Path to BK2 movie file" >&2
	exit 2
fi

HERE=$(cd "$(dirname "$0")" && pwd)
export OGGF_TRACE_OUTPUT_DIR=$3
export OGGF_WORKDIR=$3
exec "$HERE/run_bizhawk_lua.sh" "$HERE/s1_trace_recorder.lua" "$2" "$1"
