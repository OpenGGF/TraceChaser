# Capture Sonic 2

Use the [native headless workflow](native-headless.md) with the Sonic 2 World
REV01 ROM (SHA-1 `8BCA5DCEF1AF3E00098666FD892DC1C2A76333F9`).

## Standard level capture

The default profile is `gameplay_unlock`. Use
`--trace-profile level_gated_reset_aware --gameplay-segment N` only when a
multi-segment movie needs a reviewed later gameplay segment.

```bash
"$TRACECHASER_ROOT/bizhawk-headless/run.sh" \
  --tracechaser-root "$TRACECHASER_ROOT" \
  --input-repository-root "$INPUT_REPOSITORY_ROOT" \
  --fixture-root "$FIXTURE_ROOT" \
  --mode trace --rom /absolute/roms/sonic2-rev01.gen \
  --movie /absolute/movies/s2-route.bk2 \
  --trace-profile gameplay_unlock \
  --output /absolute/scratch/s2-route
```

For a later segment, add the level-gated profile and the zero-based
`--gameplay-segment` together. Segment selection is S2-only and cannot be used
with named-run mode.

## Complete and named runs

Use `--run-id NAME` for a complete auto-segmented run with a mandatory
`run_manifest.json`:

```bash
"$TRACECHASER_ROOT/bizhawk-headless/run.sh" \
  --tracechaser-root "$TRACECHASER_ROOT" \
  --input-repository-root "$INPUT_REPOSITORY_ROOT" \
  --fixture-root "$FIXTURE_ROOT" \
  --mode trace --rom /absolute/roms/sonic2-rev01.gen \
  --movie /absolute/movies/s2-complete.bk2 \
  --run-id s2-reviewed-run \
  --output /absolute/scratch/s2-complete
```

Only add `--effective-movie-length N` when reproducing a frozen, independently
recorded session signal. It is not a convenient truncation flag.

The dedicated S2 special-stage Lua workflow is retained for producer research;
ordinary and complete-run v5 publication uses the native segmenter. Validate
the whole run root so manifest membership and order are checked together.
