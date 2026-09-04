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
