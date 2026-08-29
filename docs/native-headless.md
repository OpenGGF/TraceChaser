# Native headless capture

The native harness is the supported Linux producer for publishable trace-v5
captures. It drives BizHawk 2.11's GPGX core through Mono without EmuHawk, Lua,
or a display. It never supplies ROMs, movies, or an OpenGGF checkout: pass every
input explicitly and keep output in external scratch.

## Prerequisites

Install and preflight the exact dependency described in
[Install BizHawk 2.11](install-bizhawk-2.11.md). The build also requires Mono
6.12, `xbuild`, and the pinned Roslyn compiler documented by
[`bizhawk-headless/README.md`](../bizhawk-headless/README.md). Set the applicable
ROM variable only for tests; captures take `--rom` directly.

Define absolute paths from the TraceChaser root:

```bash
TRACECHASER_ROOT=/absolute/TraceChaser
INPUT_REPOSITORY_ROOT=/absolute/OpenGGF
FIXTURE_ROOT="$INPUT_REPOSITORY_ROOT/src/test/resources/traces"
BIZHAWK_HOME=/absolute/BizHawk-2.11-linux-x64
export TRACECHASER_ROOT INPUT_REPOSITORY_ROOT FIXTURE_ROOT BIZHAWK_HOME
```

`INPUT_REPOSITORY_ROOT` and `FIXTURE_ROOT` are protected read-only boundaries,
not discovery hints. The output must be a new path outside both repositories.

## Standard capture

```bash
"$TRACECHASER_ROOT/bizhawk-headless/run.sh" \
  --tracechaser-root "$TRACECHASER_ROOT" \
  --input-repository-root "$INPUT_REPOSITORY_ROOT" \
  --fixture-root "$FIXTURE_ROOT" \
  --mode trace \
  --rom /absolute/roms/game.gen \
  --movie /absolute/movies/route.bk2 \
  --output /absolute/scratch/candidate
```

The ROM SHA-1 selects S1, S2, or S3K; there is no game flag. Capture defaults
to deterministic gzip above the one-MiB threshold. Use `--load-queue-state`
when the intended contract requires per-frame load-queue diagnostics. Never
add a profile merely to match one movie; use a profile the recorder already
models.

## Complete and named runs

`--trace-profile complete_run` captures auto-segmented output without forcing a
run id. `--run-id ID` records the named run and emits `run_manifest.json` even
when no detour occurred. They are mutually exclusive. S2 and S3K can use
`--effective-movie-length N` only in named-run mode when reproducing a reviewed
capture-time movie-length signal.

```bash
"$TRACECHASER_ROOT/bizhawk-headless/run.sh" \
  --tracechaser-root "$TRACECHASER_ROOT" \
  --input-repository-root "$INPUT_REPOSITORY_ROOT" \
  --fixture-root "$FIXTURE_ROOT" \
  --mode trace --rom /absolute/roms/game.gen \
  --movie /absolute/movies/complete.bk2 \
  --run-id reviewed-route \
  --output /absolute/scratch/reviewed-route
```

Publication is deliberately separate. Continue with
[Validate, compare, and publish](validate-compare-publish.md).

## Tests

`bizhawk-headless/test.sh --no-gates` runs native source/unit tests. A full run
uses the exact verified runtime and whichever correctly hashed `S1_ROM_PATH`,
`S2_ROM_PATH`, and `S3K_ROM_PATH` variables are supplied. Absent inputs may
skip ROM-backed gates; a present but wrong input fails. See the harness README
for selectors, exit codes, deterministic-build verification, and current
resource bounds.
