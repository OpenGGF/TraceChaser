# Changelog

## Unreleased

- Record Sonic CD lost V-INTs. `--scd-trace` now always writes
  `vint_service_timing.jsonl`: one `{"event":"vint_not_serviced","raw_frame":N}`
  per trace row whose VBlank produced no observed entry at `VInterrupt`, empty
  when every row was serviced. Entry is counted by one fixed, opcode-checked
  execute hook resolved from the stage program's own MMD header and installed
  vector, never from counters or the lag flag; two entries in one frame fail
  the capture. The v5 validator checks the stream's grammar and rejects an
  event on a row that ran a main loop, or on a game that does not declare it.

- Add a scratch `--scd-trace` producer for Sonic CD (USA) stages. It boots the
  user-supplied disc and pinned BIOS in Mega CD mode, replays a power-on BK2
  and writes 42-column physics rows, S1-style aux events with per-row
  `lag_state`, and `game: "scd"` metadata. The v5 validator and its
  conformance pack accept `scd` with the ordinary 42-column level layout. An
  independent player-reference comparison script and a BK2 schedule composer
  are included.

- Observe S1 Nemesis PLC arming at the accepted pattern-count write instead
  of predicting it from RunPLC entry. Interrupts before the actual eligibility
  tests or count write can no longer omit an arm or stamp it on an earlier row.

- Record opt-in S1 ending, credits-font and credits-demo initialization timing
  through the existing ordered phase contract, with real ROM identities and
  fail-closed guards around clock-sensitive callbacks. Include the terminal
  lag-admission census for these complete runs; default captures are unchanged.

- Record opt-in S3K initialization timing for real fresh-load phases after the
  seeded bootstrap, with ROM-backed identities, explicit serviced/masked rows,
  reviewed object cuts and fail-closed publication of incomplete loads. Its
  companion records actual queue readiness during unsegmented represented loads,
  without changing the existing interstitial provenance stream. Both dedicated
  companions use gzip by default even below the ordinary payload threshold.

- Record `level_entry_placement_source` in S3K complete-run level/bonus and
  S2/KiS2 level metadata: the ROM's start-table, star-post or special-stage
  placement branch, latched at the first PreLevel frame of each load, with the
  raw `last_star_post_hit` byte from the same observation.
- Record the ROM's Saved/Saved2 restore blocks and carried counters as
  `level_entry_ram` at the same boundary (S3K and S2/KiS2), and give S3K
  manifest transitions the shared `gap_admission_runs` census of the
  unrecorded rows between segments.
- Give S1 run-mode manifest transitions the same `gap_admission_runs` census.
- Add the full 16.16 camera longs (fraction included) to `level_entry_ram`.
  The same S3K first-PreLevel observation also exports the native Act3 flag,
  ring word and timer long before title/ScreenEvents consume them; absent
  observations remain absent and schema v5 remains the sole contract.
- Sample S3K manifest terminal mode after original movie input exhaustion;
  omit unsupported modes and diagnostic capture bounds without changing rows.
- Add a source-locked, passive constructor-reset prefix diagnostic through the
  first S3K VDP status read, with ordered register/clock/device observations,
  fail-closed provenance checks, exact loaded managed-constructor byte qualification
  and matched observer-on/off controls.

## v0.1.0 — 2026-08-29

Initial source-only TraceChaser release for the OpenGGF 0.6 trace workflow.

### Compatibility

- OpenGGF range: the 0.6 `trace_schema: 5` line beginning at extraction base
  `88530afdf331fb152f88a4d14adb8f93f2299ff6` through the OpenGGF 0.6 release.
  This release makes no compatibility claim for pre-v5 consumers or later
  OpenGGF release lines without new compatibility evidence.
- Trace contract: schema v5 metadata, physics, auxiliary state, hardware
  timing, and run manifests. Legacy trace envelopes are intentionally rejected.
- Emulator: exactly official BizHawk 2.11 (`2.11.0.0`). BizHawk 2.11.1 and
  newer versions are unsupported because they do not preserve every Lua
  capability required by these workflows.

### Included

- Native BizHawk/GPGX recorders for Sonic 1, Sonic 2, and Sonic 3 & Knuckles,
  including named complete runs and special-stage detours.
- Lua and RetroArch capture utilities, diagnostic probes, trace-v5 validation,
  comparison, compression, inventory, conformance, and publication tooling.
- Locked, verified acquisition guidance for official BizHawk 2.11. BizHawk
  itself and native build products are not distributed here.

### Validation

The release candidate reproduced the six immutable OpenGGF extraction captures
byte-for-byte: 87 segments, 943,995 physics rows, 19,786,916 auxiliary rows,
1,442 timing rows, 309 files, and 265,398,284 stored bytes. Literal and
normalized comparison reported zero differences, and all six deterministic
inventory artifacts were byte-identical to the extraction evidence. Full
commands, identities, host/toolchain details, and per-capture results are in
[the v0.1.0 capture record](docs/validation/v0.1.0-capture.md).

### Tested host and toolchain

- Linux x86_64 (CachyOS rolling; kernel `7.2.0-1-cachyos`)
- Mono `6.12.0`, xbuild `14.0`, Roslyn C# compiler
  `3.9.0-6.21124.20 (db94f4cc)`
- Python `3.14.7` and Lua `5.4.9` for local release validation
- Source-only CI additionally pins Ubuntu 24.04, Python 3.12, Lua 5.4,
  PowerShell 7.4.7, Mono 6.12, and ripgrep 14

### Retained limitations

- The release is source-only. It contains no BizHawk archive or binaries,
  native build output, ROM, BK2 movie, capture payload, or OpenGGF fixture.
- Native capture release evidence is Linux x86_64/Mono evidence. Windows
  launchers and PowerShell contracts are source-tested but were not used for
  the six release captures.
- ROMs, BK2 movies, canonical fixtures, and durable capture storage remain
  user/consumer supplied and must stay outside the TraceChaser source tree.
- Exact BizHawk 2.11 is required; there is no 2.11.1 or later fallback.
- TraceChaser does not make OpenGGF a build or runtime dependency. OpenGGF
  consumes a reviewed immutable commit through an optional submodule.
