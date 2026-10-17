# Native headless GPGX trace harness

Records canonical Sonic 1 / 2 / 3&K physics traces by driving BizHawk 2.11's
Genesis Plus GX core directly under Mono — no EmuHawk, no X11, no Lua.

It exists because the Lua recorders in [`../bizhawk/`](../bizhawk/) need a real
EmuHawk window and a display, fail silently under `--chromeless`, and cannot be
run headlessly on this Linux box. This harness produces **byte-identical** output
to those recorders, proven by permanent ROM-backed differential gates, and is the
supported capture path on Linux for every game.

| Recorder | Native? |
|---|---|
| S1 standard, complete-run, run mode | yes — gated |
| S2 all three modes, complete-run | yes — gated |
| KiS2 plain and full run | yes — chip observer profile, without native load audit; [contract](docs/kis2-run-mode.md) |
| S3K standard (both profiles) | yes — gated |
| S3K complete-run (level / bonus / special-stage) | yes — gated |

**This harness is the preferred capture path**, and the intended direction is that
the Lua recorders are retired rather than kept at feature parity. It is roughly
1,300-2,800 fps against Lua's ~840, genuinely headless, fails loudly where Lua
under `--chromeless` swallows errors into a silent no-output run, and is the only
one of the two with a test suite.

**This harness is also the canonical fixture-publication authority.** Establish
recorder correctness before publication from named ROM/disassembly semantics,
behavioral and unit tests, and independent review. Existing fixture vectors and
Lua byte parity are optional corroboration, not publication prerequisites.

Never make a capture certify itself. Freeze the candidate's literal hashes,
lengths, versions, inventories, counts, ordering, and ranges; tests must not
derive their expected values dynamically from the same invocation. Obtain
explicit user approval for the exact bytes and deltas before installing them.

Keep the Lua recorders runnable for optional differential evidence and ad-hoc
debugging. The `event.onmemoryexecute` / `onmemorywrite` families live only there,
and a twenty-line throwaway script beats adding a `.cs` to two non-SDK csproj
files for something you intend to delete within the hour. Frozen and
unmaintained Lua is fine; it is not the fixture publisher.

## Requirements

- **Mono 6.12** with `xbuild` on `PATH`, plus Mono's Roslyn compiler at
  `/usr/lib/mono/msbuild/Current/bin/Roslyn/csc.exe`. The build fails closed
  unless that compiler is version `3.9.0-6.21124.20 (db94f4cc)` with SHA-256
  `81e98ade50f3e4127237128211778bd6ebe0c3998c9cc2f5eb44f3196a0297f8`.
  The projects are non-SDK `.csproj` and the source is **C# 7.x** — newer
  language features will not compile. `build.sh` always uses this Roslyn path,
  deterministic PE/MVID generation, and a canonical source path map; direct
  ambient `mcs`/`xbuild` compilation is rejected by the project contract. Its
  response file is created under `obj/` for one build and removed on exit.
  `test.sh` and `run.sh` both rebuild through this contract before execution.
- A **BizHawk 2.11 distribution**. `common-env.sh` defaults `BIZHAWK_HOME` to the
  checkout-local `.dependencies/BizHawk-2.11-linux-x64`, validates that it is an existing
  absolute path, and runs the shared exact-version/runtime-capability preflight
  before checking the required DLLs under `dll/`. Install or select it using
  [`../docs/install-bizhawk-2.11.md`](../docs/install-bizhawk-2.11.md).
- **User-supplied ROMs**, passed by environment variable and SHA-1 verified:
  `S1_ROM_PATH`, `S2_ROM_PATH`, `S3K_ROM_PATH`. No ROM is committed to this repo.

## Build, run, test

```bash
./build.sh                       # xbuild both projects into bin/Release
./run.sh <args>                  # exec mono against the harness executable
./test.sh [options]              # the differential + unit suite
./test.sh --no-gates             # ~4s: everything except the ROM-backed gates
./test.sh --game s3k             # just the S3K gates
./test.sh --jobs 1               # sequential, the debugging path
```

To prove the pinned production and test executables are independent of checkout
location, run two clean builds under a new durable output root:

```bash
./verify-deterministic-build.sh \
  --bizhawk-home /abs/path/to/BizHawk-2.11-linux-x64 \
  --output /absolute/external/scratch/deterministic-headless-build
```

The output root must not already exist. One copied checkout includes spaces in
its path and hostile ambient compiler properties. The verifier byte-compares
both executables and PDBs, proves direct ambient `xbuild` is rejected, and runs
the strict S2 capability binding against each production assembly. It retains
the two build roots and direct-build failure log as durable evidence.

`test.sh` skips cleanly when a ROM variable or the BizHawk distribution is
absent, and fails loudly when one is present but wrong (it re-verifies the ROM
SHA-1 rather than trusting the path).

A full run is minutes, not seconds, because the ROM-backed gates replay entire
movies — about 1.4 million frames of real Genesis emulation per run. The runner
therefore executes them **in parallel by default**, and takes selectors so you
can run a slice instead of all of it.

| Option | Meaning |
|---|---|
| `--filter <substr>` | Case-insensitive substring of the test name |
| `--game s1\|s2\|s3k` | Tests tagged with that game. **Untagged tests are excluded** |
| `--movie <substr>` | Tests replaying a matching BK2 movie, e.g. `--movie s3k-complete-sonic-tails`. **Untagged tests are excluded** |
| `--gates-only` / `--no-gates` | The ROM-backed differential gates, or everything else |
| `--jobs <n>` | Worker threads, default 8. `--jobs 1` is sequential and reproduces the pre-parallel output exactly |
| `--slowest <n>` | Slowest-test report size. Default 10 in parallel, 0 (off) at `--jobs 1` |
| `--timings <path>`, `--update-timings` | Read / rewrite the recorded timings used to order the queue |
| `--help` | The above, from the runner itself |

Selectors combine and intersect. `--game` and `--movie` select on tags rather
than on names, so a test that declares neither is excluded by them — use
`--filter` for name-based selection. Exit codes: **0** all passed, **1** a test
failed, **2** the selection matched nothing, **3** a malformed command line.

### What bounds a parallel run

Measured on a 32-core box: the full suite is **957 s sequential and 383 s at the
default `--jobs 8`**, 372 passed / 0 failed / 0 skipped either way, with an
identical per-test outcome set. `--jobs 4` measures 388 s — both are within
noise of the floor below, which is the point.

**One gate sets the floor.** The 466,334-row S3K complete-run capture is **379 s**
on its own, and no amount of concurrency makes a full run shorter than it — the
383 s above is that gate plus ~4 s of serial-tagged tests. That is why the queue is ordered **longest first**, from the recorded
`tests/test-timings.tsv` (refreshed by `--update-timings`, and falling back to
each test's static estimate when the file is missing): start that gate late and
the parallelism evaporates behind it.

**Nothing else throttles.** Each capture is a flat ~231 MB resident whatever the
movie length — that floor is the emulator core, ROM and framebuffers, not the
recording — so on a box with GBs free, memory does not bind and the scheduler
does not model it. The default of 8 sits well inside the 32 cores here and well
above the point where extra workers stop helping; raise or lower it freely with
`--jobs`.

Gate scratch lives under `.scratch/` in this directory, **not** `/tmp`, and each
gate deletes its own root in a finally block, so peak usage is what is
concurrently running (2.7 GB observed at `--jobs 8`, and 0 once the run ends)
rather than what the run has ever produced. `/tmp` is
frequently a RAM-backed tmpfs: running four gates against one filled it and
three captures failed with ENOSPC — correctly, because a full disk and a
recorder that stopped early are indistinguishable to a byte gate.

## Diagnostic rendered frames

`GpgxHost` separately implements `IGpgxVideoCaptureHost`. Ordinary `Advance()`
keeps the existing video-disabled trace behavior. `AdvanceWithVideo()` performs
exactly one normal core frame with video enabled and sound disabled; it does not
select a recorder, change controllers or write emulator RAM. After it returns,
`CapturePublishedFrame()` copies that completed frame's ARGB pixels, dimensions,
lag state and the exposed VRAM/CRAM/VSRAM domains. A frame advanced without video
or a failed advancing step is rejected as stale. State loading invalidates the
rendered generation even when it restores the same completed-frame number. Each snapshot owns its arrays;
mutating a snapshot cannot change the emulator or another snapshot.

This port was added for the independently specified S3K ending diagnostic
comparison task (2026-10-14). It is an observation/capture capability, not fixture
publication or a new trace hook. Use explicit ROM, runtime, inputs and external
output roots. Runtime domain names and sizes fail closed: VRAM 65,536 bytes, CRAM 128 bytes and
the pinned GPGX VSRAM backing 128 bytes (80 bytes are active Genesis VSRAM). The
snapshot also records each domain's exposed endianness. Dimensions come from
the video provider; no assumed crop, resize or frame offset is applied.

Samples are taken after the completed hardware frame, outside core advancement.
Frame-end VRAM/CRAM and RAM's prepared SAT/palette must not be substituted for
the pixels drawn earlier in that frame. A scene comparator must record its native
producer/publication and input ledger independently. Matching a phase, current
RAM coordinate or completed-frame count alone is not pixel parity. Existing
movie sync settings, timing differences and any declared one-time setup retain
their own provenance; reference data never supplies later gameplay state.

Bounded checks are registered with the existing host tests:
`test.sh --filter GpgxHost --jobs 1` covers dispatch flags, stale/failed generations,
owned copies and a serial real-S1 render-vs-ordinary full-RAM comparison. Supply
an explicit original `S1_ROM_PATH` and the official `BIZHAWK_HOME`; the real case
reports a skip when that ROM is absent. These checks qualify the port, not a scene
capture or a complete trace-fixture sweep.

## Exact S3K Kosinski service measurements

`S3kExactKosMeasurementObserver` is an optional, read-only diagnostic capability.
It does not replace `HardwareTimingEventEngine`, the canonical v5 ledger, or the
legacy `load-time-measurement-v1` stream. The separately versioned schema2/v2
stream records actual direct FIFO service opportunities, not elapsed hardware
frames. Its fixed instruction-byte checks run before any callback registration.

Default callbacks are module-child post-enqueue `$1B46`, actual occupied-head
service `$1C1A`, and committed retirement `$1CE4` before the remaining FIFO shifts.
An explicit `includeDirectSubmissions` option adds post-enqueue `$1BEE`; a return
from there to `$1B46` is skipped so a module child is not observed twice. Only
normal Level mode service returning to `$651C` or title-card service returning
to `$62D8` is classified, and both caller JSR opcodes are verified from supplied
ROM bytes. Any other service caller contaminates its head sample. Waiting jobs
receive no units. Resumed heads retain their original source/destination; consumed
cursors and decremented count are verified at retirement. Unobserved submissions
are labeled `head_service_censored` and cannot supply a runtime profile cost.

`--mode load-time --exact-kos-v2` attaches this observer to the same whole-movie
load-time run, with direct-submission provenance on, and publishes its stream as
`load_time_measurements_v2.jsonl` beside `load_time_measurements.jsonl`. Both files go
through the existing staged, no-replace publication set (a failed capture publishes
neither), with the same ordinary-exception behaviour as the other text outputs; it is
not a crash-atomic or durable multi-file guarantee. The switch adds no hook or RAM range beyond the
observer's fixed set and changes nothing in the v1 stream. It is rejected in every
other mode, and is diagnostic input for context recovery, not a runtime budget.

Supply explicit ROM/movie SHA identities, fixture name and raw hardware-frame
clock; dispose the returned registration scope. Call `Reset()` only for the
actual session/reset boundary: pending samples abort rather than emit a cost.
Snapshots, gameplay RAM, controllers, VDP domains and trace sync points are never
written by this observer. Caller PC, source/fingerprint and exact FIFO facts own
classification; scene, fixture name and frame number cannot select behavior.

The registered `--filter ExactKosMeasurement --jobs 1` checks exercise same-frame
service, waiting, interrupted descriptor retention, consumed cursors, caller/hook
revision gates, aborts, contamination and provenance validation. Existing
`--filter HardwareTiming --jobs 1` controls keep legacy behavior separately
qualified. Actual-native interrupted service and independent head/end observation
must corroborate a profile publication; pure test vectors alone do not grant a
cost. No measurement stream is a canonical trace fixture or replay input.

## Capturing a trace

```bash
TRACECHASER_ROOT=/absolute/TraceChaser
INPUT_REPOSITORY_ROOT=/absolute/OpenGGF
FIXTURE_ROOT="$INPUT_REPOSITORY_ROOT/src/test/resources/traces"
BIZHAWK_HOME=/absolute/TraceChaser/.dependencies/BizHawk-2.11-linux-x64 \
"$TRACECHASER_ROOT/bizhawk-headless/run.sh" \
  --tracechaser-root "$TRACECHASER_ROOT" \
  --input-repository-root "$INPUT_REPOSITORY_ROOT" \
  --fixture-root "$FIXTURE_ROOT" \
  --mode trace \
  --rom "$S3K_ROM_PATH" \
  --movie /abs/path/to/movie.bk2 \
  --output /abs/path/to/output-dir \
  --trace-profile aiz_end_to_end
```

The game is auto-detected from the ROM's SHA-1; there is no `--game` flag.

| Flag | Meaning |
|---|---|
| `--mode smoke\|trace` | `smoke` is a short diagnostic run; `trace` is a full recording |
| `--rom`, `--movie`, `--output` | ROM, BK2 movie, destination. `--output` must **not** already exist |
| `--trace-profile <name>` | Per-game capture profile (see the specs in `docs/`) |
| `--gameplay-segment <n>` | S2 only — selects one segment of a multi-segment movie |
| `--run-id <id>` | Run mode: emits `run_manifest.json` and per-segment directories |
| `--effective-movie-length <n>` | Run mode only — overrides the movie-length signal |
| `--load-queue-state` | Trace mode only — records complete per-frame physical load-queue diagnostics and advertises `load_queue_state_per_frame`; off by default so legacy differential fixtures remain byte-identical |
| `--no-compress` | Trace mode only: publish the payloads uncompressed (compression is the default) |
| `--compress` | States the default explicitly; mutually exclusive with `--no-compress` |
| `--compress-threshold <bytes>` | Size floor for compressing a payload (default 1048576) |
| `--max-frames <n>`, `--bk2-frame-offset <n>` | Smoke mode only |

Output is published all-or-nothing: files are staged and only linked into
`--output` once the whole capture succeeds, so a failed run never leaves a
half-written trace behind.

### Capturing a diagnostic final-PCM window

`--final-pcm-window` records the sync sound provider's final 44.1 kHz PCM for
the first `--frames` rows of one movie, under that movie's own SyncSettings
(FM chip and audio filter included). It applies each row, advances with sound
enabled and drains the frame's samples. It registers no callbacks and writes no
emulator state. It is comparison evidence for OpenGGF's opt-in
native-compatible PCM stage, not a fixture publisher.

```bash
BIZHAWK_HOME=/absolute/BizHawk-2.11-linux-x64 bash -c \
  'source bizhawk-headless/common-env.sh && exec mono bizhawk-headless/bin/Release/BizHawk.Headless.Gpgx.exe \
     --final-pcm-window --rom /absolute/s3k.gen --movie /absolute/movie.bk2 \
     --output /absolute/scratch/window --frames 2400 \
     --tracechaser-root /absolute/TraceChaser --input-repository-root /absolute/OpenGGF \
     --fixture-root /absolute/OpenGGF/src/test/resources/traces'
```

The three root flags are required and go through the same producer boundary as
the other capture paths: after symlink resolution, `--output` must lie outside
both checkouts (so never under the fixture root), and the fixture root must
belong to the consumer checkout. Every flag may appear once. `--output` must be
absent or an empty real directory, never a symbolic link.

It receives `pcm.s16` (interleaved little-endian stereo), `frames.tsv` (row,
lag flag, stereo frames, cumulative) and, last, `metadata.json`.

`metadata.json` is the completion marker:
- it is written only after both data files are flushed to disk and closed;
- `pcm.s16` is then re-hashed from disk and must match the streamed digest and
  the expected length;
- it is published by temporary file and rename, and the success line
  (`FINAL_PCM_WINDOW … pcm_sha256= core_sha256=`) is printed after it.

A directory without `metadata.json` is an incomplete run, whatever else it holds.
Failures can leave partial data files but never a marker.

The marker records:
- ROM SHA-1 and movie SHA-256;
- the recorded FM chip and filter settings, and counts;
- `pcm_sha256` and `frames_tsv_sha256`;
- the core file WaterboxHost actually loads (`gpgx.wbx.zst` when present, else
  `gpgx.wbx`, under the validated `BIZHAWK_HOME/dll`) with its size and SHA-256,
  which must be unchanged at the end of the run;
- the managed BizHawk version and the `BizHawk.Emulation.Cores.dll` SHA-256.

These record input identity. They are not a host attestation.

### Draining the 68000 CPU ledger (stall ABI v2)

`--m68k-ledger-window` needs a core built with 0001-0005. It starts from an explicit ingress savestate and its
provenance JSON (`--ingress-state`, `--ingress-provenance`), advances `--frames` frames with no input, and drains the
native ledger only between FrameAdvance calls.

- **Ingress.** The provenance must match the ROM, the loaded core, the movie and the state's SHA-256. Its region
  (`ntsc` or `pal`) is applied the way the producer applied it: `pal` forces Europe on the movie's SyncSettings.
  `ingress_kind` must be `declared_setup`, the only kind accepted until an authentic ingress producer exists. The
  ingress is produced outside TraceChaser; authentic movie-only coverage is a separate campaign.
- **Durability.** Every output stream and savestate is flushed to disk before the completion marker. The marker
  records each file's SHA-256 and the running host's SHA-256.
- **Options.** `--ledger off` gives the non-perturbation control. On the S3K ROM only, two options use
  `Level_frame_counter` as a frame label: `--save-state-when-label-exceeds` and `--audio-label-window`.
- **Boundary.** The three root flags and the output rules are the same as `--final-pcm-window`.
- **Outputs:**
  - `intervals.jsonl`: one row per closed VInt-to-VInt interval. The first row is the partial interval opened by
    enable and is marked `complete: false`;
  - `interval-slots.csv`: per-PC count and MCLK by context;
  - `accepts.jsonl`, `frames.jsonl`, `audio-window.s16` and savestates;
  - `metadata.json`, written last as the completion marker.
- **Fails closed** on any native flag, a gap, an empty or unjoined interval, more than one VInt in a frame, or an
  input-identity mismatch. MCLK conservation is checked separately by the external checker.

### Capturing a pinned complete-audio raw stream

The production-owned S2/S3K audio validation path uses a separate, closed raw
producer command. It selects exactly one reviewed capture runner (`s2` or
`s3k`); it accepts no trace profile, callback, address, capture bound, or test
filter. The service manifest and capability fixture below are TraceChaser
observer evidence, not an OpenGGF complete-run manifest.

```bash
BIZHAWK_HOME=/absolute/BizHawk-2.11-linux-x64 \
"/absolute/TraceChaser/bizhawk-headless/run-complete-audio.sh" \
  --complete-audio-game s2 \
  --rom /absolute/Sonic2Rev01.gen \
  --movie /absolute/sonic-2-sonic-tails-complete-emeralds.bk2 \
  --service-manifest /absolute/gpgx-audio-service-manifests-v1.json \
  --capability /absolute/gpgx-audio-capability-v1.json \
  --output /absolute/external/s2-complete-audio-raw.jsonl
```

Use `--complete-audio-game s3k` with the locked-on ROM and its pinned movie,
but omit `--capability`: the pinned S3K runner/profile has no capability input,
and the command rejects one rather than accepting an unused file. S2 requires
an existing absolute ROM, movie, service manifest, and capability file. S3K
requires the first three existing absolute files. For both games, `--output`
must be an absolute file name that does not already exist. The dedicated Linux
launcher accepts only this closed complete-audio argument surface, verifies the
fixed built harness assembly, clears `DISPLAY`, and executes it through Mono;
it neither builds nor accepts generic producer boundary roots.
The selected runner streams into a sibling staging file, closes and validates
the raw capture, then atomically creates the destination without replacement.
On a short or otherwise failed capture, neither a partial destination nor a
replacement is published. The S3K pinned runner continues to validate its
reviewed service-manifest profile and does not accept caller-configured
observation behavior.

### S1 ending credits demos

The eight Sonic 1 ending demos are captured without a BK2 movie. The harness
presses Start only to leave the title screen, redirects that level entry into
the ROM's credits flow by writing `f_demo = 0`, `v_creditsnum = 0`, and
`v_gamemode = GM_Credits`, then clears external input permanently. Each trace
row reads the ROM-held controller byte; directions are preserved, A/B/C become
the engine jump bit, and Start is ignored. The ROM—not the harness—selects
demo identities and any LZ restore/water state.

```bash
TRACECHASER_ROOT=/absolute/TraceChaser
INPUT_REPOSITORY_ROOT=/absolute/OpenGGF
FIXTURE_ROOT="$INPUT_REPOSITORY_ROOT/src/test/resources/traces"
BIZHAWK_HOME=/absolute/TraceChaser/.dependencies/BizHawk-2.11-linux-x64 \
"$TRACECHASER_ROOT/bizhawk-headless/run.sh" \
  --tracechaser-root "$TRACECHASER_ROOT" \
  --input-repository-root "$INPUT_REPOSITORY_ROOT" \
  --fixture-root "$FIXTURE_ROOT" \
  --mode trace --rom "$S1_ROM_PATH" \
  --trace-profile credits_demo --credits-target all \
  --output /absolute/external/credits-v5-candidate
```

`--credits-target` is `all` or `0` through `7`; a single target is diagnostic
only. `--movie`, run/segment arguments, and compression overrides are rejected
for this profile. The candidate root must not exist and must never be a
canonical fixture path. All selected directories are staged as one no-replace,
forced-compression transaction; only the publication workflow may install them.

For the one-time credits migration evidence, pass
`--credits-raw-observations <path>` and the required printable-ASCII
`--credits-raw-observation-id <id>` together with `--credits-target all`.
The JSONL sidecar is streamed from raw host reads outside both the candidate and
installed fixture roots, sealed only after candidate publication, and is never
replaced. A failed capture or seal leaves the final sidecar absent; a seal
failure quarantines the already-published scratch candidate.

S1/S2 player-art audit is mandatory. Standalone captures arm with an empty
submitted ledger. S1 may carry only an unpublished staging preparation; it
receives no transfer id or manifest descriptor until a verified VBlank probe
promotes the final replacement payload. S2 continuous named runs may carry
accepted FIFO work submitted in the represented `run_gap`; the next manifest
segment records the exact immutable initial descriptors and ledger
fingerprint, and the matching completion keeps its id and `run_gap` origin.
Manifest fields validate recorder continuity only and never seed engine state.

The two payloads **stream** into their staging files as the capture produces
them, for every run-mode capture (S1, S2 and S3K) — nothing holds a segment,
let alone a run, in memory. Plain trace mode still buffers, because the
`level_gated_reset_aware` profile can throw an armed recording away and has to
be able to.

### Output contract

- `physics.csv` — per-frame physics rows
- `aux_state.jsonl` — per-frame auxiliary events
- `metadata.json` — capture identity, profile, offsets, versions
- `run_manifest.json` — run mode only: segment inventory and transitions

Every production output uses the sole v5 envelope:

```json
"recorder": "native-bizhawk-headless",
"recorder_version": "3.0",
"trace_schema": 5
```

Provenance is opaque and never selects parser/replay behavior.
`lua_script_version` is removed, not renamed. Ordinary level rows are fixed at
42 columns; special stages are fixed by game/profile. Timing uses one
module-plus-direct grammar, and every run manifest includes
`dynamic_art_gap_transitions` (including empty).

Validate and compare a complete scratch fleet before publication:

```bash
python3 ../traces/validate_trace_v5.py /absolute/external/v5-candidate/traces
python3 ../traces/compare_trace_v5_candidates.py \
  /absolute/OpenGGF/src/test/resources/traces \
  /absolute/external/v5-candidate/traces \
  --mode credits-20-to-42 --output /absolute/external/v5-candidate-report.json
```

The comparator never installs output. Detailed capture-matrix, credits
raw-host-evidence, candidate-root replay, and exact-byte approval instructions
are consumer-owned; see [`../docs/migration-from-openggf.md`](../docs/migration-from-openggf.md).

**The two payloads are gzipped at publication by default**, landing as
`physics.csv.gz` and `aux_state.jsonl.gz` once they reach `--compress-threshold`
(default 1 MiB). `metadata.json` and `run_manifest.json` are never compressed,
matching the committed fixture layout. Below the threshold a payload keeps its
plain name.

Compression happens *inside* the same all-or-nothing publication, and for a
streamed payload it happens *during* it: the bytes are written **through** a
gzip stream into a `.gz` staging file, so the uncompressed form never exists on
disk at all. A complete-run capture that used to stage 2.84 GB now stages
roughly a tenth of that.

The verify-before-destroy guarantee is preserved exactly rather than traded
away for the streaming. The plaintext is SHA-256'd and counted on its way into
the compressor; when the file closes, the finished gzip is decompressed and
compared against those values by hash **and** length before it joins the
publication set. A payload that turns out to be below the threshold is expanded
back to its plain name by that same verifying decompression, so the threshold
rule is unchanged. A buffered payload (plain trace mode) takes the original
route: gzip to a second staging file, decompress, compare against the source,
then discard the source.

Either way a verification failure publishes nothing at all — no final is
linked, not even for a payload that compressed cleanly. Each compressed payload
is reported on stdout after publication commits.

The default is on because the risk is a **commit**, not disk space. A full
complete-run `aux_state.jsonl` measures ~254 MB raw against ~12 MB gzipped
(~21x); uncompressed it is past GitHub's 100 MB per-file hard limit, so it cannot
be pushed at all. An opt-in flag fails exactly when a human installing a fixture
forgets it. Pairing the default with the 1 MiB threshold makes the harness and
the repo's commit policy (`.githooks/validate-policy.sh` — same two name
patterns, same threshold) agree by construction. A repo-level guard,
`TestTraceFixtureCompressionGuard`, enforces the same rule on the fixture tree
whatever produced the file.

Output is deterministic: the gzip carries no timestamp (`gzip -n` equivalent), so
the same capture compresses to the same bytes and a fixture commit shows no noise
diff. Streaming does not weaken that — a streamed payload's `.gz` is
byte-identical to the bulk-compressed one for the same content, pinned by a test,
because nothing is flushed through the deflater mid-stream and the container
therefore cannot depend on the caller's write pattern. Container bytes could not
affect the gates in any case; they hash decompressed content.

**`--no-compress`** opts out, for consumers that read a capture by its plain name
and never commit it. Every ROM-backed differential gate here passes it: they
capture into a temp directory and compare raw bytes.

[`../traces/compress-traces.ps1`](../traces/compress-traces.ps1) implements the
same semantics for scratch output from the **Windows Lua diagnostic route**,
whose recorders still write uncompressed output. That output is corroborative
only and must never be installed as a canonical fixture.

## Publishing a canonical fixture

1. Establish the native recorder's correctness from named ROM/disassembly
   semantics, behavioral and unit tests, and independent review. Treat existing
   fixture vectors and Lua parity as optional corroboration.
2. Capture with the unchanged reviewed native implementation into scratch.
3. Record literal SHA-256 digests, byte lengths, metadata versions, segment
   inventories, row/event counts, ordering, ranges, and the named cause of every
   byte-level delta. Stop on any unexplained delta.
4. Obtain explicit user approval for those exact candidate bytes and reported
   deltas, then install the native output byte-for-byte with no hand edits.
5. Re-run native gates and repository fixture guards, then measure and record
   replay-frontier movement.

Publication tests assert the frozen literal evidence; they never rerun capture
to derive expectations. Until the exact-byte approval in Step 4, committed
fixtures remain read-only ground truth.

## The differential gates

Each gate captures with this harness and compares against a committed fixture:
`physics.csv`, `aux_state.jsonl` and `run_manifest.json` by raw sha256 with **zero
normalization**, and `metadata.json` line-for-line with only `recording_date` and
an exactly-pinned version line permitted to differ. Fixtures are decompressed into
the test's temp directory and hashed there — the gates never modify them.

These gates provide strong regression and cross-implementation evidence, but
they do not let the recorder certify a newly proposed fixture. Treat a
pre-publication gate failure as a defect in the recorder or its proposed
contract, never as a reason to relax the comparison or silently replace a
fixture.

## S2/S3K native audio observer gates

These gates run against a separately built and installed GPGX core carrying the
audio observer; see
[`native/gpgx-audio-observer/README.md`](native/gpgx-audio-observer/README.md)
for how to build and install one. Supply the reviewed ROMs and complete BK2s by
absolute path; the capability fixture rejects the wrong movie name, SHA-256, or
row count before opening the core.

```bash
export BIZHAWK_HOME=/absolute/observer-install
export OPENGGF_GPGX_Z80_CAPABILITY=1
export S2_ROM_PATH=/absolute/sonic2-rev01.gen
export S2_BK2_PATH=/absolute/sonic-2-sonic-tails-complete-emeralds.bk2
export S3K_ROM_PATH=/absolute/sonic3k-locked-on.gen
export S3K_BK2_PATH=/absolute/s3k-knuckles-complete-superemeralds.bk2
./test.sh --filter GpgxZ80AudioCapabilityTests --jobs 1
```

Add `OPENGGF_GPGX_Z80_PERFORMANCE=1` for the bounded three-lane performance
gate, or `OPENGGF_GPGX_Z80_FULL_RUN=1` for duplicate complete-movie event
digests. These gates stream aggregates only. They do not publish ROM, movie,
core, or reconstructive event payloads.

The streaming `CaptureFrame(..., consumer)` overload validates and projects the
frame transactionally while reusing its exact-count drain buffer; it does not
retain a `LastCapture`. Call `CaptureCanonicalFrame(...)` when the immutable raw
events, begin-ordered services, snapshots, reset lifecycle, and projected chip
writes must remain available after the synchronous consumer boundary.

At a complete-movie cutoff, call `CaptureCutoffFrontier()` before cleanup. The
immutable frontier preserves the outer-to-inner open stack, completed
descendants withheld in global begin order, their exact owned chip/snapshot
data, YM latches, and arm epoch. Record the normalized terminal Z80 state with
it, then call the internal cutoff cleanup. Task 9 must serialize this as a
canonical store/comparator record; `DiscardCutoffState()` is cleanup only and
must never stand in for terminal semantic publication.

## Specs

`docs/` holds the byte-level porting contracts — RAM maps, format strings,
emission order, profile predicates, publication and manifest layout, per-fixture
permitted deltas:

| Game | Specs |
|---|---|
| S1 | `s1-trace-recorder-behavior.md`, `s1-complete-run-behavior.md`, `s1-run-mode-behavior.md` |
| S2 | `s2-trace-recorder-behavior.md`, `s2-run-mode-behavior.md` (§11 = complete-run) |
| S3K standard | `s3k-trace-recorder-behavior.md`, `s3k-aux-events.md`, `s3k-profiles-and-hooks.md` |
| S3K complete-run | `s3k-complete-run-behavior.md`, `s3k-completerun-profiles.md`, `s3k-run-publication.md` |

Where sources disagree, resolve the behavior against the ROM/disassembly and
update the spec, implementation, or optional Lua corroboration accordingly.

## Layout

```
src/Bk2/          BK2 movie reader (input log, header, sync settings)
src/Bootstrap/    BizHawk installation discovery, ROM identity/SHA-1 validation
src/Core/         GpgxHost — the emulator core wrapper and controller
src/Recording/    Per-game capture runners, CSV/aux/metadata writers, publisher
src/Program.cs    CLI entry point and per-game dispatch
tests/            Dependency-free console runner: TestMain (registry, test
                  metadata), TestOptions (CLI), TestRunner (scheduling and
                  buffered output), TestConsoleRouter, TestTimings,
                  TestScratch, AssertEx
.scratch/         Per-test capture scratch, created and deleted per gate
docs/             Byte-level porting specs
fixtures/         Small synthetic inputs for unit tests
```

## Contributing

Read the [root agent guidance](../CLAUDE.md) before changing anything here — it
carries the constraints that are expensive to discover by trial: the two
`.csproj` files that both need editing, the test registry that silently drops
unregistered classes, the ignored dependency/build roots that must not receive
source files, and which kinds of test cannot run beside anything else.
