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
