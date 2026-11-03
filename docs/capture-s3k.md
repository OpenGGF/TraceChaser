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

Complete-run level and bonus metadata also carry
`level_entry_placement_source`: which block the ROM used to place the player
for that segment's own load. `Get_LevelSizeStart` (`loc_1BE46`,
`sonic3k.asm:38186-38192`) reads the start table when `Last_star_post_hit`
(`$FE2A`) is clear (`start_table`). Otherwise `Load_Starpost_Settings` restores
the `Saved_*` block (`starpost_restore`: star posts, scripted checkpoints such
as AIZ1 `loc_1C522`, and every bonus-stage exit) or, when
`Special_bonus_entry_flag` (`$FE48`) is set, the `Saved2_*` block
(`special_stage_restore`, `loc_2D2C2`: both special-stage result exits,
`sonic3k.asm:12154-12157` and `loc_9D02` 12709-12712, and leaving the HPZ
arena). The value names the placement block only: `start_table` does not mean an
intro plays (a bonus-stage load skips it in `SpawnLevelMainSprites`).

Both bytes are latched at the first `Game_mode` `$8C` frame of each load:
`Level:` sets bit 7 before its fade (`sonic3k.asm:7536-7561`), and the load later
clears the flag (`sonic3k.asm:61836`) and masks the `$80` a star-post-less
special-stage return carries (`sonic3k.asm:7913`). The field describes the load
that preceded the segment's arm, not later death reloads inside the segment.
An absent key means unknown: either no load of the arming zone was observed
since the previous arm, or the fixture predates the field. Special-stage
metadata never carries it. It is observed ROM state for consumers that take the
same branch; it selects nothing in the recorder.

The same observation also publishes `last_star_post_hit`: the raw
`Last_star_post_hit` byte the load read, as a decimal integer immediately after
the source. Bit 7 is kept, so a special-stage return shows the results screen's
`$80` (for example 128 with no star post, 131 after post 3). A bonus-stage exit
shows the star post it restored, which the post keeps hit (`loc_4A024`). The
manifest's `starpost_bonus` `last_star_post_hit` is sampled after bonus entry
zeroes the byte (`loc_2D4CA`) and is not a substitute.

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

The opt-in `--initialization-timing` capability for S3K complete runs writes a
run-root `initialization_timing.jsonl` stream. Rows use absolute zero-based BK2
indices across segment gaps. It observes ordered `hold`, `resume`, and `vint_0`
outcomes for independently fingerprinted display clear, data decode, setup and
initial assembly. It exports no clocks, CPU state or decoded gameplay values.
The pre-trace seeded bootstrap is excluded and the first represented fresh load
has ordinal one. Unsupported ordinary interrupts or unproved initial-object
service placements fail capture. Gumball's after-seed and Slots' placement
cuts are source-derived. A between-callback interruption can also commute to
completed object work only when the saved loop boundary is valid and every
subsequent actual callback, including new children, has a reviewed independent
closure. Unknown callbacks abort the entire staged capture; CPU/slot observations
never enter the timing stream. A cap during a load or provisional callback tail
rejects publication. For fixture candidates use `--compress-threshold 0` so this
small companion is compressed by the native publisher too. This capability is under qualification;
its output does not become a canonical fixture without independent review and
explicit approval of the exact native bytes.
