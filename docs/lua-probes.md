# Lua recorders and probes

Lua is the exploratory path for event hooks, memory-write observation, and
small diagnostics that the native recorder does not model. It is not the v5
publication authority. Use exact BizHawk 2.11 because later releases remove Lua
functionality these scripts require.

## Launch a namespaced probe

Install BizHawk and follow [Scratch and security](scratch-and-security.md).
Create a fresh external work/output directory, then launch a probe with explicit
input boundaries:

```bash
mkdir -p /absolute/scratch/probe-work /absolute/scratch/probe-result
export BIZHAWK_HOME=/absolute/BizHawk-2.11-linux-x64
export OGGF_INPUT_REPOSITORY_ROOT=/absolute/OpenGGF
export OGGF_WORKDIR=/absolute/scratch/probe-work
export OGGF_OUT=/absolute/scratch/probe-result/example-stage.log
bizhawk/run_bizhawk_lua.sh \
  bizhawk/probes/example_stage_probe.lua \
  /absolute/movies/route.bk2 \
  /absolute/roms/game.gen
```

The checked-in example waits for S3K AIZ1 and records its first reviewed
`Process_Sprites` hook. Replace it with another actual recursively namespaced
file below `bizhawk/probes/` as needed. `OGGF_OUT` is the exact file opened by
`ProbeRuntime`; its parent must already exist. The launcher passes that file
through the producer/consumer external-output alias guard before BizHawk starts,
hashes the movie bytes it actually passes to EmuHawk, clears inherited
common-module overrides, and supplies the repository-owned `probe_runtime.lua`.

On Linux, EmuHawk still requires a reachable X display even with chromeless
rendering. Use a trusted Xvfb/display configuration. Set
`BIZHAWK_ALLOW_SLOW_LUA=1` for a visible first-run diagnostic when a Lua parse
or load error would otherwise be hard to see.

## Probe contract

Every namespaced probe delegates lifecycle ownership to
`ProbeRuntime.run({...})`, declares a semantic stage gate and deferred hooks,
and remains read-only. A probe must not take over speed/display cleanup, write
emulator memory, inject joypad state, create savestates, or own output files.
The recursive policy in `testing/test_probe_contract.py` enumerates all nested
Lua files, so moving a probe into a subdirectory does not escape review.

The PLC timing probes are executable state machines. Their test-owned harness
is `testing/lua/plc_timing_probe_contract_test.lua`; direct execution requires
the address/environment contract encoded by the Python test. The audio
contracts below `bizhawk/audio/` are also executable with Lua 5.4 and have no
ROM or emulator dependency.

## Legacy recorder diagnostics

The six retained gameplay Lua recorders derive their shared
`bizhawk/lib/oggf_trace_common.lua` module from their own installed path and
ignore caller attempts to replace it. They are useful for differential evidence
and predecessor investigations. Publish current v5 candidates with the native
harness, then compare them independently.
