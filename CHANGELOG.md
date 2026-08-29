# Changelog

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
