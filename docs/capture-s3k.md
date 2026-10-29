# Capture Sonic 3 & Knuckles

Use the [native headless workflow](native-headless.md) with the locked-on S3&K
ROM (SHA-1 `CFBF98C36C776677290A872547AC47C53D2761D6`).

## Standard capture profiles

`gameplay_unlock` is the general default. `aiz_end_to_end` owns the reviewed AIZ
arming/tail semantics, while `level_gated_reset_aware` discards earlier level
attempts and publishes the selected completed capture. Choose the semantic
profile before recording; do not rename arbitrary profile strings into support.

```bash
"$TRACECHASER_ROOT/bizhawk-headless/run.sh" \
  --tracechaser-root "$TRACECHASER_ROOT" \
  --input-repository-root "$INPUT_REPOSITORY_ROOT" \
  --fixture-root "$FIXTURE_ROOT" \
  --mode trace --rom /absolute/roms/sonic3k-locked-on.gen \
  --movie /absolute/movies/s3k-aiz.bk2 \
  --trace-profile aiz_end_to_end \
  --load-queue-state \
  --output /absolute/scratch/s3k-aiz
```

S3K standard output includes `hardware_timing.jsonl`. Load-queue state is an
explicit capture capability, not an engine-state replacement.

## Complete game and detours

Use `--trace-profile complete_run` for an unnamed segment collection or
`--run-id NAME` for a named manifest. The recorder follows levels, bonus stages,
and special stages in one movie and publishes all discovered members as one
no-replace transaction.

When a named or detour manifest is emitted after every original BK2 input
row has advanced the core, it includes `expected_movie_end_mode` sampled from
the final frame's `Game_mode` RAM byte (`$F600`), before segment finalization.
The supported observations are `level` (`$0C` / PreLevel `$8C`) and
`title_screen` (`$04`). The ROM's `GameLoop` / `GameModes` dispatcher
(`sonic3k.asm:433-467`) distinguishes Save Screen `$4C` from Level; the
segmentation low-nibble family is not an endpoint classifier. Other modes
leave the optional field absent. Any effective-movie-length override also
leaves it absent, including an override equal to the real input count.

This observation authenticates the mode at original input exhaustion only.
`complete_run`, a movie name, and an observed `level` endpoint do not establish
campaign completion. Recorder correctness and independent review still precede
authoritative fixture use; existing manifests are never filled in by hand.

```bash
"$TRACECHASER_ROOT/bizhawk-headless/run.sh" \
  --tracechaser-root "$TRACECHASER_ROOT" \
  --input-repository-root "$INPUT_REPOSITORY_ROOT" \
  --fixture-root "$FIXTURE_ROOT" \
  --mode trace --rom /absolute/roms/sonic3k-locked-on.gen \
  --movie /absolute/movies/s3k-complete.bk2 \
  --run-id s3k-reviewed-run \
  --output /absolute/scratch/s3k-complete
```

The native port intentionally rejects environment knobs for hook-only event
families, altered poll windows, or early stopping that it does not model. Use a
Lua probe for investigation rather than weakening publication semantics.
