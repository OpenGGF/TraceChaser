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

Level metadata (plain and run mode) carries `level_entry_placement_source`:
`start_table` when `Last_star_pole_hit` (`$FE30`) is clear at
`LevelSizeLoad` (`s2.asm:14774-14792`), otherwise `starpost_restore`
(`Obj79_LoadData`, `s2.asm:44776-44812`; a special-stage return re-enters
through the same star-pole save). The byte is latched at the first
`Game_Mode` `$8C` frame of each load (`Level:` sets bit 7 before its fade,
`s2.asm:4757-4765`). An absent key means unknown: no load of the arming zone was
observed since the previous arm (a skipped or reset-discarded arm still consumes
its load), or the fixture predates the field. KiS2 uses the same RAM address
through the S2 recorder; no KiS2 disassembly is pinned to confirm it separately.
The same observation publishes `last_star_post_hit`, the raw
`Last_star_pole_hit` byte the load read (decimal, after the source), and
`level_entry_ram`: the star-pole block `Obj79_LoadData` reads (`Saved_*`
$FE31-$FE58, checked against the encoded routine at $1F35E) plus `Life_count`,
`Extra_life_flags`, `Ring_count` and `Timer`.

Only add `--effective-movie-length N` when reproducing a frozen, independently
recorded session signal. It is not a convenient truncation flag.

The dedicated S2 special-stage Lua workflow is retained for producer research;
ordinary and complete-run v5 publication uses the native segmenter. Validate
the whole run root so manifest membership and order are checked together.

## The request window's two transfer sites

`sndDriverInput` makes two stores into Z80 RAM and the observer watches both.
The SFX store, `move.b d0,zVar.Queue0(a1,d1.w)` inside `.loop`
(`docs/s2disasm/s2.asm:1317-1326`), sits at PC `$10D6`. The music store,
`move.b d0,zVar.QueueToPlay(a1)` at `.isNotPauseCommand` which the
disassembly labels `loc_10C0` (`:1302-1304`), sits at PC `$10C0`. Both are
fixed in `gpgx-audio-service-manifest-s2-request-v3.json` and opcode-checked
at load; neither is caller-selectable.

Every transfer in the payload carries a `site` of `sfx` or `music`, and the
sink and extractor validate each site against its own rules. An `sfx` record
keeps every guarantee it always had: a queue slot of 0 to 3, PC `$10D6`, a
strictly increasing native ordinal, and a reviewed marker owner. A `music`
record carries the reserved slot 4, PC `$10C0`, and no correlation at all,
because that store emits no native action-7 marker; its row is observed and
its service is not.

`D1` is a queue slot only at the SFX store, where `.loop` sets it. At the
music store `:1294-1295` leave it holding the pause-check residue of
`move.b d0,d1` and `subi.b #MusID_Pause,d1`, so it is not read there.

Watching only the SFX store made every music request invisible, and with it
any sound a caller routes through `PlayMusic` rather than `PlaySound` — the
ring-milestone check at `s2.asm:25913-25914` is one such caller. A capture
that saw nine songs load recorded none of them.

The payload version is `openggf.s2-complete-run-audio-raw.v4` and the transfer
schema `openggf.s2-preconsumption-request-transfer.v2`. There is no
backwards-compatible read of the older shape: the `site` field is required.
