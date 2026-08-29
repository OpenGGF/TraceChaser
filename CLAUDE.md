# Guidance for AI agents — TraceChaser

This guidance has a byte-identical root mirror. Keep `AGENTS.md` and
`CLAUDE.md` in sync.

## Project scope and repository safety

TraceChaser owns OpenGGF's emulator-facing trace producers and analysis tools.
It does not own OpenGGF's Java replay consumers or canonical trace fixture
corpus. Read [README.md](README.md) for the repository boundary and workflow
documentation status.

1. Never commit ROMs, BK2 movies, BizHawk distributions, copied upstream source
   trees, generated builds, caches, raw traces, scratch captures, generic logs,
   or uncurated output.
2. ROMs, movies, emulator installations, OpenGGF inputs, and output roots must
   be supplied explicitly. Do not discover them by reaching into an OpenGGF
   checkout or by embedding a machine-local absolute path.
3. Write capture output to durable scratch storage outside both repositories.
   Never write directly into OpenGGF's canonical fixtures. Installing or
   replacing a canonical fixture requires explicit user approval.
4. Run both policy scanners before committing. `testing/repository_policy.py`
   checks the Git index; `testing/history_audit.py` checks every reachable Git
   object. Do not weaken their shared predicates or the exact root-license and
   Zstandard-notice content exceptions.
5. Raw and gzip conformance fixtures are permitted only below `contracts/v5/`
   when the bounded artifact manifest pins their exact path, stored size and
   SHA-256, plus logical size and SHA-256 for deterministic gzip. The history
   scanner enforces that relationship independently in every commit.
6. Keep this file and `CLAUDE.md` byte-identical. Repository-wide guidance lives
   only at the root; do not add nested agent-policy copies.

## Native headless GPGX trace harness

The remaining guidance covers what is specific to, and expensive to rediscover
in, `bizhawk-headless/`. Read
[`bizhawk-headless/README.md`](bizhawk-headless/README.md) before changing the
harness.

## What this code is for

It reproduces the Lua trace recorders in `bizhawk/` byte-for-byte so traces can
be captured headlessly on Linux. Every capability is locked by a differential gate
that replays a real movie against a committed fixture.

**This is the preferred capture path**, and the intended direction is that the Lua
recorders are retired rather than kept at feature parity. Do not add work here whose
only justification is matching a Lua capability nobody uses.

**The native harness is also the fixture-publication authority, but recorder
correctness must be established before publication.** Review the implementation
against ROM/disassembly-backed semantic invariants, exercise it with behavioral
and unit tests, and obtain independent code review before treating a capture as
authoritative. Cross-implementation vectors and Lua byte parity are valuable
corroboration when available, not publication prerequisites.

Lua remains a **scratch-only** corroboration and diagnostic path — frozen and
unmaintained is fine — rather than a publisher. It can provide optional
differential evidence for recorder changes and remains the substrate for ad-hoc
hook-driven debugging (see below).

## Hard rules

1. **Never modify the consumer-owned canonical fixture tree in place.** Those
   external fixtures are read-only ground truth. A failing gate means production code is
   wrong. Do not relax a comparison, widen a normalization, or regenerate a
   fixture to make a gate pass. Regenerating canonical fixtures is a **user
   decision** — ask.
2. **Never make a native recorder certify its own correctness.** Establish its
   semantic contract from the ROM/disassembly, behavioral/unit tests, available
   cross-implementation vectors, and independent code review before publication.
   After capture, record and pin the resulting digests, lengths, event inventory,
   ordering, and ranges as immutable publication evidence. Tests must not make a
   bad capture pass by dynamically deriving expectations from that same
   invocation. Fixture replacement still requires explicit user approval, and
   the installed files must be the exact gated native output with no hand edits.
3. **C# 7.x only.** Mono 6.12 + `xbuild`, non-SDK `.csproj`. Newer syntax will
   not compile.
4. **Every new `.cs` file must be hand-added to BOTH
   `bizhawk-headless/BizHawk.Headless.Gpgx.csproj` and
   `bizhawk-headless/BizHawk.Headless.Gpgx.Tests.csproj`.** There is no globbing.
5. **Every new test class must be registered in `TestMain.BuildRegistry()`.** The
   runner is a plain registry, not NUnit — an unregistered test silently never
   runs, and the suite still reports green.
6. **Verify new harness files are tracked.** Ignore rules deliberately exclude
   local dependencies and generated artifacts. A source file placed inside an
   ignored build or dependency root can build and test locally while remaining
   absent from the commit. Verify with `git status --short`,
   `git show --stat HEAD`, and `git ls-files bizhawk-headless`.
7. **Check for an existing untracked file before creating one.** Writing a "new"
   doc over untracked work has already happened here once.

## The runner runs tests in parallel

`bizhawk-headless/test.sh` defaults to `--jobs 8`. What that costs you to know:

- **`--jobs 1` is the debugging path** and reproduces the pre-parallel runner
  exactly: registration order, unbuffered writes, no timing report. Any time a
  parallel result looks strange, re-run the case at `--jobs 1` before believing
  it. A test that passes only at `--jobs 1` is a defect worth reporting, not a
  scheduling detail to route around.
- **Output is buffered per test and flushed as one block.** `PASS `/`FAIL `/`SKIP `
  keep their exact shapes and streams; do not change them, and do not add output
  that starts with those prefixes. The slowest-N report and the summary line
  start with `  ` or `---` so a `grep '^PASS '` count is unaffected.
- **Anything that mutates process-global state must run alone.** Three families
  exist today:
  1. **File descriptors 1 and 2.** The CLI wraps its capture in
     `NativeStandardOutputSilencer`, which `dup2()`s `/dev/null` onto both for
     the whole process. Anything another thread writes in that window is not
     interleaved, it is **destroyed**. This is how it was found: a parallel run
     reported 34 of 352 tests and still exited 0. `TraceCliTests` and
     `EndToEndTests` drive the CLI in-process and are therefore registered
     through `RegisterSerial` in `TestMain.BuildRegistry()` — **marked by class,
     not by case**, so a case added to either later inherits the constraint
     instead of silently eating the suite's output.
  2. **The environment block.** The `BootstrapTests` / `TraceCliTests` cases that
     set or clear `S1_ROM_PATH`, `S3K_ROM_PATH` or an `OGGF_*` variable. A
     capture child started in that window inherits the block —
     `OGGF_TRACE_STOP_FRAME` would silently truncate a concurrent gate's capture.
  3. **The in-process emulator core.** The two `GpgxHostTests` cases that
     `GpgxHost.Open`; two live waterbox cores in one process is not a supported
     configuration.

  Serial tests run alone, before the parallel phase; today that phase costs about
  four seconds. **Look for this hazard whenever a test writes to `Environment`,
  constructs a `GpgxHost`, or calls `Program.Run`** — none of them announce
  themselves, and what they produce looks like a flaky gate or a truncated run
  rather than a scheduling bug.
- **New ROM-backed gates should carry metadata**: `game:`, `movie:`,
  `kind: TestKind.Gate` and `estimatedSeconds:`. All are optional C# defaults,
  so no other registration needs touching. `estimatedSeconds` chooses start
  order and nothing else — it is never an assertion and never a timeout.
  **Do not add a memory weight or a per-test resource budget.** Captures are a
  flat ~231 MB regardless of movie length (the emulator core, not the
  recording), so a scheduler that modelled RAM would be modelling a constraint
  that does not exist, and the next person would trust it.
- **Gate scratch goes under `.scratch/`, never `Path.GetTempPath()`.** Use
  `TestScratch.CreateRootPath(prefix)` and delete the root in a finally block. A
  single capture materializes up to ~1.6 GB and `/tmp` is frequently a RAM-backed
  tmpfs; four gates against one produced ENOSPC inside three captures. The gates
  reported that correctly — a full disk and a recorder that stopped early are
  indistinguishable to a byte gate — but it is not a failure worth reproducing.
- **A run that loses a test cannot exit 0.** `TestRunner` asserts that every
  selected test produced a result and prints `RUNNER ERROR: …` and exits 1 if
  not, and a worker whose execution wrapper throws records a failure rather than
  dying. Both exist because the first parallel run here reported 34 of 352 tests
  and exited 0. Do not remove them for being untestable in the ordinary path;
  they are the check that the ordinary path is what happened.
- Timings live in `bizhawk-headless/tests/test-timings.tsv`, refreshed with
  `--update-timings`.
  They are a scheduling hint: a stale, partial or deleted file changes start
  order and nothing else.

## Verifying your work

- Run the full suite from the repository root:
  `BIZHAWK_HOME=<abs> S1_ROM_PATH=… S2_ROM_PATH=… S3K_ROM_PATH=… bizhawk-headless/test.sh`.
  Report the counts you actually observed. `--filter <substr>` while iterating,
  but finish on a full run.
- `--no-gates` is the sub-minute tier for iterating on unit-level code;
  `--gates-only`, `--game s1|s2|s3k` and `--movie <substr>` narrow to the
  ROM-backed work. `--game` and `--movie` select on tags, so an **untagged test
  is excluded** by them — use `--filter` for name-based selection.
- Gates skip when a ROM or the BizHawk distribution is missing, and fail when one
  is present but wrong. A "green" suite with everything skipped proves nothing —
  check the skip count, not just the failure count.
- For Java-side trace tests in OpenGGF, always use `mvn test`, never
  `mvn surefire:test`: the latter does not compile, and a stale `target/classes`
  has produced a measured 3-vs-14 failure-count discrepancy in OpenGGF.
- Trace report basenames collide across test classes (`s3k_aiz1_report.json` is
  written by both the standard and the completerun AIZ class), and batching
  classes perturbs counts through shared singletons. **Run one class per
  invocation and clear OpenGGF's `target/trace-reports` between runs** when the
  numbers matter.

## Regenerating a fixture (when the user has approved it)

The publication contract, in order:

1. Before publication, establish recorder correctness against named
   ROM/disassembly semantics, behavioral and unit tests, cross-implementation
   vectors where available, and independent code review. This may validate an
   existing candidate produced by the unchanged reviewed code. Lua byte parity is
   optional corroboration; it is neither the authority nor a publication
   prerequisite.
2. Capture the publication candidate with the native harness into scratch, never
   directly into the explicit consumer fixture root or any preserved
   capture tree.
3. Before copying, record and freeze the candidate's SHA-256 digests, byte
   lengths, metadata versions, segment inventories, row/event counts, canonical
   ordering, and range checks. **Categorise every byte-level delta against a named
   cause**, mechanically over whole files rather than by sampling. Anything
   unexplained stops publication.
4. Obtain explicit user approval for the exact candidate and reported deltas.
   Copy the gated native files byte-for-byte; never edit an event, timestamp, or
   metadata field by hand. Publication tests must assert the frozen literal
   expectations, not derive them by rerunning native capture.
5. Re-run the native gates against the installed fixture, plus fixture load,
   schema, compression, and reference-closure guards. These are regression
   checks after publication, not the independent authority for Step 1.
6. Measure the trace-replay frontiers **before and after** and record movement in
   OpenGGF's `docs/status/trace-frontier-log.md`. A fixture correction can
   unmask a latent engine bug; expect one and investigate it rather than
   weakening the fixture.

## Diagnostic hooks are deliberately not ported

The S3K Lua recorders carry ~61 `event.onmemoryexecute` / `onmemorywrite` registrations
each, behind `OGGF_TRACE_ENABLE_DIAGNOSTIC_HOOKS`. This harness implements **none** of
them, and that is a decision rather than a gap —
`bizhawk-headless/docs/s3k-profiles-and-hooks.md` §2.4 records the reasoning,
and `bizhawk-headless/tests/S3KHookAbsenceTests.cs` pins it to the fixture bytes.

Do not "helpfully" add general M68K exec/memory-write callback support. Two
address-filtered hardware-timing observers are permitted exceptions, and nothing else is.

The first is the S3K hardware-timing submission observer at `M68K BUS` PC `0x001B46`,
immediately after `Process_Kos_Module_Queue` returns from `Queue_Kos`. It may mirror or
stage direct-FIFO submission lifecycle only.

The second is the S1 PLC arming observer at `M68K BUS` PC `0x0015E4`, the entry of
`RunPLC` (`sonic.asm:1379`) in Sonic 1 World REV01. An entry-PC observation is required
rather than per-frame RAM sampling because the routine DESTROYS the head identity:
`move.l a0,(v_plc_buffer).w` (`sonic.asm:1405`) writes the pointer back already advanced
past the Nemesis header, so no later sample can recover the descriptor that was armed,
and the arm predicate itself (`v_plc_buffer` non-zero, `v_plc_patternsleft` zero) is only
true on entry. It records readiness edges into the per-segment `hardware_timing.jsonl`
and nothing else.

Both are observers. Neither may select a trace sync point, mutate emulation state, carry
a gameplay value, or enable any diagnostic-hook output. Each one's Mono delegate must
remain strongly rooted while registered and be deterministically unregistered when
capture ends. Behavioral tests, ROM/disassembly evidence, independent review, and
corrected-candidate differentials gate these exceptions.

No other fixture contains hook output, so broader callback support would be the only
significant surface here with no differential coverage, in a harness whose value depends
on reviewed, test-backed capture correctness. Hook-enabled diagnostic captures are also
what has previously breached git's file-size limits.

If a frontier genuinely needs hook-derived data, use a **one-off throwaway Lua script** on
the Lua route and delete it afterwards. The division is: **this harness validates, the Lua
recorders diagnose.**

**A hook must never decide when a trace syncs.** Hook-derived per-level sync points were
used for AIZ and CNZ (S3) and rejected as hydration in another guise — the sync point is the
beginning of the level load, and a trace that only lines up because a per-level hook says so
is hiding an engine bug, not proving its absence.

Re-enabling hooks for a fixture capture invalidates fixtures: the gates assert both the
absence of the deferred families and the unpopulated shape of hook-enriched records (the 9
AIZ `aiz_handoff_terrain_state` skeletons must keep `sonic_floor_seen:false`). That failure
is the designed signal to build the callback surface — not something to work around.

## Things that have bitten people here

- **A recorder reading a dead RAM address silently props up a trace frontier.**
  Two separate S3K constants pointed at the wrong RAM (`Debug_placement_mode` and
  `Life_count`) and produced constant columns that looked plausible. Check that a
  column's *shape* is ROM-plausible — monotonic where it should be, stalling only
  on lag frames — not merely that it is non-constant.
- **The six recorders are copy-paste siblings.** They each carry their own ROM
  address constants, so a fix to one does not propagate. When you find a defect in
  one, audit all six; see `bizhawk/SHARED_MODULE_HANDOFF.md`.
- **Stop conditions are evaluated POST-advance**, in the Lua's `on_frame_end`
  source order. Getting this wrong was independently introduced in both the S1 and
  the S2 port.
- **Row input columns index by `bk2_frame_offset + trace_row`**, not by the
  last-applied emulator frame. The two agree only while every frame produces a
  row, so the bug hides until a mid-segment excursion.
- **Payloads are gzipped at publication by default** (1 MiB threshold), because
  an uncompressed complete-run aux stream is past GitHub's per-file limit and
  cannot be pushed. `--no-compress` opts out and every ROM-backed gate passes it,
  since gates compare raw bytes in a temp directory and commit nothing. Do not
  gzip by hand: hand compression is where the spurious binary diffs come from —
  different gzip implementations produce different container bytes for identical
  content. `TestTraceFixtureCompressionGuard` (Java, `mvn test`) fails the build
  if an uncompressed payload appears under the consumer's canonical fixture root
  regardless of which tool wrote it.
- **A streamed payload is compressed on the way to disk**, so the uncompressed
  form never exists there. Verify-before-destroy still holds and is the reason
  that code is trustworthy: the plaintext is hashed incrementally as it is
  written, and the finished gzip is decompressed and compared against that hash
  and length before the file can publish. If you touch this, do not "simplify"
  by dropping the round trip, and do not introduce a mid-stream flush through
  the deflater — a streamed `.gz` must stay byte-identical to the bulk-compressed
  one, which `TracePayloadCompressorTests` asserts directly.
- **Run mode streams; plain trace mode buffers.** Every run-mode runner (S1, S2,
  S3K) writes rows straight into staged files through an `IRunSegmentSink` /
  `IS3KCompleteRunSegmentSink`, because no armed run segment is ever discarded.
  Only the profiles that genuinely throw an armed recording away — S2 plain
  `level_gated_reset_aware`, S3K standard `level_gated_reset_aware` — buffer, and
  that is the only reason buffering is acceptable. Re-buffering a run-mode
  segment cost 1.5 GB peak RSS on the S2 complete-emeralds pass before this
  split; do not reintroduce it.
