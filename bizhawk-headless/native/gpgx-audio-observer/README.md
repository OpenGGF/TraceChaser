# GPGX buffered audio observer

This directory pins an observation-only patch for BizHawk 2.11's GPGX
Waterbox core. It records bounded, tokenized service, FM, PSG, reset, and Z80
RAM snapshot events without changing chip writes, emulated cycles, CPU results,
or savestated state. The observer is disabled until explicitly configured.

The supported managed integration is `REFLECTION` against the exact stock
BizHawk assemblies from the separately locked official Linux runtime described
in [`../../../docs/install-bizhawk-2.11.md`](../../../docs/install-bizhawk-2.11.md).
This directory's `source-lock.json` is consumed only by the native observer
source/build workflow; it is not the runtime archive-install lock and the two
locks must not be combined. No patched managed DLL is built or shipped.
The native API reports ABI v5 while continuing to accept exact legacy v1,
v2, v3, and v4 configurations. Events remain little-endian, 32 bytes each, with a fixed
capacity of 65,536. ABI v2 added profile-gated pre-arm filtering and a one-shot
publication-epoch transition: prepublication frames are fully validated and
drained without aging continuation budgets, then a drained, proof-armed READY
boundary resets only publication ages while preserving active tokens and chip
latches. ABI v3 adds two narrowly configured direct-parent-close actions.
Action 8 always atomically snapshots and closes the direct parent below the
current top, compacts the stack, and emits the adjacent `SERVICE_PROMOTE` event
that proves the surviving child's effective parent/depth change. Action 9 first
applies the same direct-work-RAM M68K return-address predicate as action 5. A
listed return emits the existing exact KEEP observation marker and leaves the
stack unchanged; an unlisted return performs the same atomic close and
promotion as action 8. Both actions require exact expected-child and
direct-parent kinds; there is no wildcard alternative. Action 10 emits the
existing retry marker bound to the direct parent beneath a typed async top,
without allocating, snapshotting, closing, promoting, or mutating the stack.
It is valid only as the sole paired override for a source-identical ordinary
begin; the override wins exactly when its declared direct parent is present,
and otherwise the ordinary begin remains authoritative. Begin ancestry remains
immutable; managed and stored diagnostics retain the bounded transition
history, while producer-neutral semantic records retain the effective ancestry
without depending on native event coordinates. Action 11 reserves one future
service begin while an exact non-child-bearing root blocker remains active.
Every matching callback emits marker value 4 without changing the stack. The
reservation may remain on the origin blocker, or one exact configured action-4
tail may atomically emit the origin END and successor BEGIN before rebinding
its current owner. Action 12 alone consumes the reservation and emits the
target as a child of that exact current owner. Repeated callbacks coalesce by
immutable origin token, hook, opcode proof, target kind, A7, and return
identity; neither transfer nor consumption is fitted to a recorded retry
count.

ABI v4 gives action-7 M68K observation markers one exact contemporaneous A7
sample: `payload_length` is 4 and `payload[0..3]` stores the full register in
little-endian order; `payload[4..7]` stays zero. ABI v1-v3 action-7 markers and
every non-action-7 marker retain zero payload length and bytes. The sample is
taken at the reviewed instruction boundary after the managed execute callback
and before opcode execution, without mutating emulated state.

ABI v5 adds action 13, `SNAPSHOT_AT_PC`, the one parent-independent
observation. Every other action attaches to the service stack, which is shared
across processors: the active service at an M68K instruction is whichever Z80
service happens to be on top. A boundary the M68K can reach from anywhere
therefore has no stable parent, and claiming one would record a lifecycle that
did not happen.

Action 13 is selected regardless of the active service. It pushes and pops
nothing, declares no service kind and no expected active kind, and emits one
marker with value 5 carrying the active service token, or zero at root, plus
its declared snapshot ranges. Declaring no ranges is the marker-only form.
Configuration requires an M68K hook with no service kind, no expected active
kind, no flags, a valid range slice or none at all, and no other hook at its
instruction, so exactly one hook is always selected without needing an
alternative per reachable active kind. The manifest owns the PC, opcode and
ranges; no caller may select an address or a value, and the action never writes
emulated state.

`selftest/snapshot_at_pc_harness.c` proves this on the real M68K core: the
action fires under an active service and at root, carries that service's token,
emits its snapshot bytes, leaves the stack untouched so the surrounding pop
still happens exactly once, and raises no fault. Six configuration negatives
cover the ABI gate, a claimed service kind, a claimed parent, a Z80 hook, a
flag, and a second hook at the same PC.

`gpgx_audio_trace_first_fault` returns a read-only, packed 16-byte snapshot of
the first runtime fault in the configured session: stable reason, source CPU,
instruction-start PC, active kind/depth, and continuation count/limit. It does
not append an event or mutate emulation/observer state. Frame abort preserves
the diagnostic; disable ends the session and clears it. Reason values are:

| Value | Reason |
|---:|---|
| 0 | none |
| 1 | token allocation |
| 2 | snapshot capture |
| 3 | service stack |
| 4 | service transition |
| 5 | hook/opcode proof |
| 6 | proof-arm transition |
| 7 | chip-write ownership |
| 8 | event capacity |
| 9 | continuation limit |

## What is pinned, and what is not

Pinned are the inputs this project controls: the source commits in
`source-lock.json`, the clang packages `prepare-toolchain.sh` unpacks, and
`0001-buffer-z80-audio-events.patch`.

Not pinned is the host. The build does not lock the identity of system
utilities, does not reject an ambient environment, and does not verify a chained
recipe digest. An earlier revision did all three, and the result was that a
routine package upgrade, which moved six of those utilities at once, failed the
build closed while detecting nothing about the artefact.

Provenance is therefore an output rather than a gate. `build-observer.sh`
records the patch, core and observer hashes plus the ABI into `identity.json`
beside the build and into `artifact-lock.json` here;
`install-observer.sh` checks that the core it is installing still matches those
recorded values, and the managed harness checks the installed `identity.json`
rather than literals frozen into its own source.

The capability fixture avoids a self-referential executable hash without
weakening it. Its S2 profile authenticates a raw-byte template SHA-256 after
requiring exactly one lowercase-hex
`task8_harness_executable_sha256` field and replacing only that 64-byte value
with ASCII zeroes. Every other byte remains identity-sensitive. The field is
then independently required to equal the SHA-256 of the production
`BizHawk.Headless.Gpgx` executable. Java metadata continues to pin the complete,
unnormalized capability-file SHA-256.

## Building and installing

Create the toolchain once. The package directory is caller-supplied and must
contain the filenames and bytes listed by `prepare-toolchain.sh`, which checks
each package SHA-256 before publishing the toolchain.

```bash
observer=/absolute/TraceChaser/bizhawk-headless/native/gpgx-audio-observer
native=/absolute/external/audio-parity/native
packages=/absolute/path/to/locked-package-input
stock=/absolute/path/to/BizHawk-2.11-linux-x64

"$observer/prepare-toolchain.sh" \
  --source "$native/source" \
  --packages "$packages" \
  --output "$native/toolchain"
```

Then build. The script applies the patch to a staged copy, runs the native
selftests, builds emulibc and gpgx with the pinned clang under a clean
`env -i PATH=/usr/bin:/bin`, checks the resulting ELF, and writes
`identity.json`.

```bash
"$observer/build-observer.sh" \
  --source "$native/source" \
  --toolchain "$native/toolchain" \
  --output "$native/build"
```

Add `--reproduce` to build twice and compare. Note that reproducibility is
same-path: the prepared musl sysroot wrappers carry the absolute path they were
configured at, so `--reproduce` builds both copies at one fixed staging path
rather than claiming a path-independence the toolchain does not provide.

Install beside, never over, the stock distribution:

```bash
"$observer/install-observer.sh" \
  --build "$native/build" \
  --stock "$stock" \
  --output "$native/install"
```

Every output destination must be absent and should live under an explicit
external root; neither the source checkout nor the harness `.scratch/` is an
output tree. The installation carries the literal patch, `identity.json`,
`artifact-lock.json`, this README, and verbatim notices alongside the core.
Genesis Plus GX's license prohibits commercial use and requires complete
corresponding source for modified distributions; read the installed
`GPGX-LICENSE.txt` in full before redistributing.

To run the native selftests alone against an already-patched tree:

```bash
"$observer/selftest/run.sh" /absolute/patched-source /absolute/toolchain /absolute/absent-scratch
```

Seven harnesses run, ending with `snapshot-at-pc-harness`. Any failure aborts.
