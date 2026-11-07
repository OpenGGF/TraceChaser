# Capture Sonic 1

Use the [native headless workflow](native-headless.md) with the Sonic 1 World
REV01 ROM (SHA-1 `69E102855D4389C3FD1A8F3DC7D193F8EEE5FE5B`). TraceChaser
does not provide the ROM or BK2 movie.

## Standard level capture

Omit `--trace-profile`. The recorder waits for ordinary gameplay and derives
the BK2 offset and level identity from the run:

```bash
"$TRACECHASER_ROOT/bizhawk-headless/run.sh" \
  --tracechaser-root "$TRACECHASER_ROOT" \
  --input-repository-root "$INPUT_REPOSITORY_ROOT" \
  --fixture-root "$FIXTURE_ROOT" \
  --mode trace --rom /absolute/roms/sonic1-rev01.gen \
  --movie /absolute/movies/s1-level.bk2 \
  --output /absolute/scratch/s1-level
```

## Complete game and detours

Use `--trace-profile complete_run` for an unnamed complete pass, or replace it
with `--run-id NAME` for a named run manifest. The segmenter records level and
special-stage transitions from one movie and disambiguates repeated segment
directories. Do not combine the two selectors.

Run-mode manifest transitions carry `gap_admission_runs`, the shared S2/S3K
census: the main-loop admission (BizHawk lag flag) of each movie row strictly
between the source segment's last recorded row and the destination segment's
first recorded row, run-length encoded and alternating from non-lag.

```bash
"$TRACECHASER_ROOT/bizhawk-headless/run.sh" \
  --tracechaser-root "$TRACECHASER_ROOT" \
  --input-repository-root "$INPUT_REPOSITORY_ROOT" \
  --fixture-root "$FIXTURE_ROOT" \
  --mode trace --rom /absolute/roms/sonic1-rev01.gen \
  --movie /absolute/movies/s1-complete.bk2 \
  --trace-profile complete_run \
  --output /absolute/scratch/s1-complete
```

## Ending initialization timing

For an approved complete-run publication, add `--initialization-timing` to the
run-mode command. The staged run gains `initialization_timing.jsonl.gz`, using
the existing ordered `hold`, `resume` and `vint_0` contract. Actual GM_Ending,
credits-font setup and credits-demo GM_Level supply independent load/phase
ordinals and ROM-backed operation fingerprints. No native counters, positions,
RNG state or decoder progress enter this stream. Cold bootstrap is excluded.

The same opt-in adds root `terminal_admission_runs`: alternating non-lag/lag
run lengths covering the rows after the final segment to original movie EOF.
Only its first run may be zero. It complements transition `gap_admission_runs`
and selects the existing lag loop during the terminal tail. Active prepared
initialization continuations retain their own ordered service authority.
Without the option, legacy output stays unchanged.

The World REV01 observer checks every fixed entrypoint opcode. Ordinary
interrupts inside setup, unknown initial callback owners/routines, a Basaran
that reaches its VBlank-sensitive branch, reached LZ setup clock reads, and
unfinished loads reject the staged capture. Ending initial-object interrupts
are still unsupported. Reviewed credits callbacks may commute zero-routine
services only because their selected paths do not read the VBlank clock and
that interrupt does not change gameplay RNG or the level counter. This is not
an audio-chip ordering guarantee or general callback-timing support. A new
unsupported callback needs evidence, not an inferred delay.

Compare all physics/aux payloads and the unedited BK2 against the publication
baseline, validate v5, and inspect the harness outcome before publishing.

## Ending credits demos

The native `credits_demo` profile needs no BK2. Capture all eight ROM-owned
demos for publication; a single target `0` through `7` is diagnostic only.

```bash
"$TRACECHASER_ROOT/bizhawk-headless/run.sh" \
  --tracechaser-root "$TRACECHASER_ROOT" \
  --input-repository-root "$INPUT_REPOSITORY_ROOT" \
  --fixture-root "$FIXTURE_ROOT" \
  --mode trace --rom /absolute/roms/sonic1-rev01.gen \
  --trace-profile credits_demo --credits-target all \
  --output /absolute/scratch/s1-credits
```

Credits output is always compressed and must remain outside the installed
fixture root. Raw-host migration evidence is a specialist, one-time workflow;
follow the exact paired `--credits-raw-observations` and
`--credits-raw-observation-id` contract in the harness README.

Lua and stable-retro S1 producers remain useful for diagnostics, but native v5
is the publication authority. Validate every result before comparison.
