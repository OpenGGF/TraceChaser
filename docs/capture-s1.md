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
