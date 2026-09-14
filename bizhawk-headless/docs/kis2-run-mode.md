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

KiS2 named runs use the exact-ROM-gated dynamic-art profile. Level segments
also observe the unchanged S2 Nemesis PLC RAM format: sixteen six-byte source/
destination records at `$F680`, with prepared patterns remaining at `$F6F8`.
The projector reads actual ROM pointers and headers. SS segments advertise
only their observed dynamic-art capability. Plain KiS2 recording retains its
unaudited compatibility behavior. Stock S2 retains its default identity bytes.

## Art-transfer prerequisite

The original candidate at `3841a5d` omitted dynamic-art evidence and was
correctly rejected by the consumer chain validator. That omission cannot be
repaired by metadata edits: the movie must be recaptured with these observers.
The v5 event shape is unchanged; its explicit owners now include `knuckles`
and `ss-knuckles`, bound to the corresponding ROM profile.

- Normal `LoadSonicDynPLC` at `$317414` (direct-D0 entry `$31741A`, pilot caller
  `$333D9E`) reads DPLC `$14BD0A` and raw art `$1200E0..$14005F`. Each source
  byte is converted through the ROM's 256-byte table at `$317540`; all runs
  concatenate into the fixBugs=0 `$FFF100..$FFF5FF` staging region.
- Its success path tail-jumps from `$317538` to QueueDMATransfer `$301158`.
  One request with RAM source `$FFF100`, VRAM destination `$F000` and total
  converted byte length is validated against both ROM-derived runs and actual
  RAM bytes at the accepted queue return `$3011B4`. Only a verified command
  slot advance emits submission and closes that decision. Queue-full closes
  without submission; suppressed/empty decisions close at `$31753E`.
- SS decision `$32CCF0` and return `$32CD38` use DPLC `$32D728` with standard
  tile offsets shifted by five. Each accepted run must use exactly `$FF0000`
  plus that decoded byte offset, the decoded length and advancing VRAM bank
  beginning `$5CA0`. SS emits submission at `$32CD38` after its calls return.
- Both owners complete at ProcessDMAQueue `$3011B6`. Existing global FIFO,
  lag forwarding, terminal and run-gap carry logic remains authoritative.
  No observer writes RAM or supplies engine gameplay state.

Focused prerequisite validation with explicit BizHawk and KiS2 paths:
`test.sh --filter KiS2 --jobs 1` passed 10 tests, zero skips; stock
`--filter S2DynamicArtObserver --jobs 1` passed 21 synthetic tests, zero skips.
These cover exact identity, conversion corruption, source/destination/length
rejection, accepted tail closure, queue-full/empty/suppressed paths, pilot
run-gap carry, SS offsets and synthetic level/SS/level metadata.
Real movie recapture and consumer validation are separate delivery checks.

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
