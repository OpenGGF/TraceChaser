# Install and verify BizHawk 2.11

TraceChaser supports the official **BizHawk 2.11 Linux x64** runtime exactly.
BizHawk 2.11.1, older releases, later releases, and installations whose version
cannot be read are rejected before capture or native build startup.

TraceChaser does not redistribute BizHawk. The runtime archive, extracted
distribution, ROMs, movies, and generated builds remain local, ignored inputs.

## The two 2.11 locks are separate

TraceChaser has two locks with different inputs and consumers. They must not be
combined or substituted for one another.

| Lock | Consumer | What it authenticates |
|---|---|---|
| [`dependencies/bizhawk-2.11-linux-x64.lock.json`](../dependencies/bizhawk-2.11-linux-x64.lock.json) | The Lua/EmuHawk recorders and probes, plus the stock managed assemblies and GPGX core used by `bizhawk-headless/` | The official Linux runtime archive URL, archive SHA-256, exact managed version, required runtime layout, and Lua API capabilities used by the retained recorders and probes |
| [`bizhawk-headless/native/gpgx-audio-observer/source-lock.json`](../bizhawk-headless/native/gpgx-audio-observer/source-lock.json) | The reproducible native GPGX audio-observer source/build workflow only | Exact BizHawk, Genesis Plus GX, and musl source commits, Git objects, critical source-file hashes, and stock identities |

The native source lock is not an installer lock and does not authenticate a
downloaded release archive. The runtime archive lock does not authenticate or
replace source used for an observer rebuild. The observer workflow documents
its own consumers and build inputs in
[`bizhawk-headless/native/gpgx-audio-observer/README.md`](../bizhawk-headless/native/gpgx-audio-observer/README.md).

## Locked official Linux archive

The reviewed runtime lock contains:

```text
version: 2.11
archive: BizHawk-2.11-linux-x64.tar.gz
official URL: https://github.com/TASEmulators/BizHawk/releases/download/2.11/BizHawk-2.11-linux-x64.tar.gz
SHA-256: cdaf9650d880bae660d63a388430f630b8d8a96b1ba59ebf0e0195a645c3bab8
default install: .dependencies/BizHawk-2.11-linux-x64
```

No reviewed Windows archive hash is part of this lock. Do not infer one from
the Linux asset or describe an arbitrary Windows distribution as lock-verified.

Python 3 and Mono's `monodis` must be available on `PATH`. Native-headless work
also requires the Mono/xbuild toolchain documented in
[`bizhawk-headless/README.md`](../bizhawk-headless/README.md).

## Acquire into `.dependencies`

From the TraceChaser root, opt in to the official download:

```bash
bizhawk/fetch_bizhawk_2_11_linux.sh
```

The installer downloads only the locked official URL. All downloaded and
extracted bytes are staged below this checkout's ignored `.dependencies/`
directory. It hashes the complete archive before extraction, checks the staged
layout, exact managed versions, and Lua capabilities, and only then publishes
the absent final directory. It never replaces an existing destination.

For an archive already downloaded by the user, use the offline path:

```bash
bizhawk/fetch_bizhawk_2_11_linux.sh \
  --archive /absolute/input/BizHawk-2.11-linux-x64.tar.gz
```

This form performs no download. The supplied archive must match the same
official SHA-256. A wrong hash fails before the extractor is called.

## Select an explicit user installation

An existing installation may live outside TraceChaser. Validate it read-only:

```bash
bizhawk/preflight_bizhawk_2_11.sh \
  --bizhawk-home /absolute/existing/BizHawk-2.11-linux-x64
```

On success, select the same absolute path for the native or Lua workflow:

```bash
export BIZHAWK_HOME=/absolute/existing/BizHawk-2.11-linux-x64
```

The Linux Lua launcher and native `common-env.sh` run this preflight before
starting EmuHawk or building the native harness. They do not copy the explicit
installation into `.dependencies/` and do not write into it during preflight.
EmuHawk itself uses portable configuration beside its executable, so a later
Lua capture needs a user installation that is writable by that user.

The version diagnostic preserves both values, for example:

```text
unsupported BizHawk managed version in EmuHawk.exe: detected raw='Version: 2.11.1.0'; expected raw='Version: 2.11.0.0'
```

An unparseable version reports the unmodified detected text and the same raw
expected value. A missing Lua registration reports the API plus its raw missing
and expected marker values.

## Lua capability boundary

The runtime lock covers every BizHawk Lua API referenced by the six retained
recorders and the shared/namespaced probe path. The source-only suite derives
that inventory from executable Lua (comments and strings are excluded), so a
new API reference requires an explicit reviewed lock update. The current 30
APIs cover:

- client lifecycle, sound, speed, pause, and `client.invisibleemulation`;
- emulator frame, lag, register, and frame-rate functions;
- memory-execute/write registration and both unregister forms;
- joypad reads;
- main-memory and named-domain reads; and
- movie identity, input, load, length, and mode functions.

This is static installation preflight, not a capture. It reads the exact managed
assembly versions and Lua registration markers without starting EmuHawk, loading
a ROM, requiring a display, or creating capture output.

## Updating a lock

A future BizHawk version is a reviewed dependency change, not a compatible
range expansion. Update the runtime archive lock only after verifying the
official asset URL/hash, exact managed versions, complete recorder/probe Lua API
inventory, native stock runtime compatibility, and representative captures.
Update the GPGX source lock only through its reproducible observer workflow.
Changing either lock never implies that the other changed.
