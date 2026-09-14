# KiS2 full-run capture

The 2026-09-14 KiS2 full-run task extends the existing S2 RAM recorder to the
verified 3,407,872-byte lock-on dump, SHA-1
`6CD0537A3AEE0E012BB86D5837DDFF9342595004`. Use `--mode trace --run-id <id>`
with explicit producer, consumer, fixture, ROM, movie and output paths. The
movie's actual length owns termination; KiS2 refuses effective-length overrides,
standalone SS mode, segment selection and load-audit requests.

## ROM evidence

The optional KiS2 disassembly (`c336fed`, gameRevision=3, fixBugs=0) explains the
shipped code. Runtime capture uses the supplied ROM, never the disassembly.
`S2RunCaptureRunner.CaptureKis2` checks its exact SHA-1 before any advance.

| Observer | KiS2 bus PC | Owning code |
| --- | --- | --- |
| final ReadJoypads RTS | `$300E52` | s2.asm 1466 onward; A0 ends at `$FFFFF608` |
| accepted V-int caller return | `$300592` | Vint_S2SS, s2.asm 942–946 |
| pre-start RunObjects return | `$304CE0` | special-stage first loop, s2.asm 7040 onward |
| recurring RunObjects return | `$304D58` | special-stage main loop, s2.asm 7087–7094 |

Literal byte anchors independently checked against the user ROM:

```text
300E18 +60: 41f8f60443f900a100036102544912bc00004e714e711011e508020000c012bc00404e714e7112110201003f800146001210b10110c0c20010c14e75
30058E +8: 6100088861000258
304CDA +16: 4eb90031288a4eb900312e906100c6ac
304D52 +12: 4eb90031288a4a38db86660c
```

The recurring observer precedes `tst.b (SS_Check_Rings_flag).w` at `$DB86`.
The set flag exits before another RunObjects pass, preserving the existing
terminal pending-pass contract. The earlier initialization pass is excluded,
as in stock S2. Hooks observe only; they never change capture arm timing or RAM.

Obj09 remains at `$B000` (`$304BE6: 11fc0009b000`). Its initializer at `$32CB3E`
and s2.constants.asm 144–157, 2120–2185 preserve the object/shared RAM layout.
Existing `sonic_*` columns mean the main-player slot, now Knuckles. P2 fields
remain actual observations of the absent second character. Results detection
scans Obj6F identities and does not assume the stock secondary-results slot.

## Identity and capabilities

Level, SS and manifest files retain the S2 v5 schema. Every segment identifies
Knuckles alone and carries the full KiS2 dump checksum. The manifest checksum
also names KiS2; it must not claim the stock S2 CRC. The run recorder samples
post-return rings: KiS2 restores checkpoint rings whereas stock S2 clears them.
No transition value is synthesized or copied into engine gameplay.

The stock S2 dynamic-art/load profile is incompatible with the chip program.
KiS2 deliberately omits that audit, its metadata capabilities and timing files,
as the existing plain KiS2 recorder does. This limitation is explicit in metadata;
full gameplay capture is not evidence of hardware-timing coverage. Stock S2
keeps its mandatory audit and byte-compatible default identity paths.

Validation covers exact ROM identity rejection, metadata throughout a synthetic
level/SS/level round trip, both fixed SS observer PCs, restored ring observations,
and strict CLI options. Independent ROM/code review precedes candidate publication.

## Current chain blocker

The captured gameplay candidate is not yet a publishable OpenGGF v5 chain.
`TraceRunManifest.DynamicArtRunValidator` requires the dynamic-art capability
for the S2 schema; omission is a hard failure, not a switch to guessed timing.
Do not weaken that validator or advertise an audit that was not observed.

A follow-up ROM audit identifies the prerequisite implementation:

- Normal `LoadSonicDynPLC` at `$317414` (direct-D0 entry `$31741A`, pilot caller
  `$333D9E`) converts and concatenates all S&K DPLC runs into `$FFF100` RAM,
  using table `$317540`, then submits one combined DMA to `$F000` VRAM.
- DPLC table `$14BD0A`, raw art `$1200E0`; success tail-jumps from `$317538` to
  QueueDMATransfer `$301158`, bypassing the no-work return at `$31753E`.
  Queue accepted return is `$3011B4`; processing begins at `$3011B6`.
- SS wrapper `$32CCC6`, shared decision `$32CCF0` and return `$32CD38` use
  DPLC `$32D728` with tile offsets shifted by five, unlike stock S2's custom
  shift-by-one encoding; decompressed source RAM is `$FF0000`.
- The current producer and consumer owner sets omit Knuckles. Adding this
  support requires an explicit shared owner and request contract, aggregation,
  RAM-source validation and tail-call completion tests. It is not a PC-only
  profile substitution. The existing v5 event shape may suffice; no schema
  revision has yet been justified.

## Validation of the initial candidate implementation

`test.sh --filter KiS2 --jobs 1` passed four focused tests with explicit BizHawk
and KIS2_ROM_PATH inputs. The full native suite was attempted with all three
stock ROMs and the consumer fixture root supplied. It reported 765 passes,
64 failures and four skips before stalling; after ten minutes without output
it was terminated, so this is incomplete validation, not a full-suite pass.
Failures include unresolved legacy fixture paths and audio observer prerequisites.
The stock `S2RunCaptureRunner emits the special-stage aux event stream` failure
was checked on unchanged `9fd957b`: the same `input_sample_frame:1` expectation
fails, with the other 14 stock run tests passing. Other broad failures have not
been individually attributed. Repository and history policy scanners passed.

The native candidate completed 36 segments and 248,042 rows. All rows' input
masks match the original BK2; metadata identities, frame ranges and counts pass.
The first full-run segment's complete physics and aux streams match an unchanged
plain KiS2 capture after the documented CRLF/LF formatting difference. The
consumer chain correctly rejects the absent dynamic-art capability, as described
above. Capture output remains outside both repositories; no canonical publication
or claim of chain parity has been made.
