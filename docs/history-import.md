# History import provenance

TraceChaser was filtered locally from OpenGGF before any TraceChaser public
remote existed. No GitHub repository was created or contacted during this
import. The source boundary was the exact OpenGGF commit
`88530afdf331fb152f88a4d14adb8f93f2299ff6`.

## Tool and reviewed inputs

`git filter-repo --version` printed `a40bce548d2c`. The pinned upstream 2.47.0
script used for the import had SHA-256
`67447413e273fc76809289111748870b6f6072f08b17efe94863a92d810b7d94`.

The history-only path map is [../history-filter-paths.tsv](../history-filter-paths.tsv).
It has 352 unique entries and SHA-256
`e367865593c837fe375d6513412bff8539d1c196e2e4d78411fd7b091df183ad`.
It was generated solely from rows whose `history_disposition` was `move`,
using only `old_path` and `history_new_path`. It contains no Java source and
does not consume either cutover column.

The private exact-literal replacement file stayed outside Git under
`$TRACECHASER_WORK_ROOT/private/history-replace-text.txt`; its SHA-256 was
`966f0d612e1d32525a7c38285ba6db55186093408565a10e6f391fc705190eb7`.
The complete enumeration contained 14 literals: three dependency path
defaults, nine output path defaults, and two workspace path defaults. The
tracked [../history-redactions.tsv](../history-redactions.tsv) records only
each old literal's SHA-256, neutral replacement, affected paths and commit
count, classification, and the authorizing OpenGGF plan commit
`3fd4813b978fce198b5e24be2f71c26f28d37c87`. It contains no original private
literal. A closed-world scan over every blob in the preserved unredacted
candidate found 45 affected blob versions across 15 paths. Replacing the exact
14-literal union in memory and re-enumerating the same object set produced zero
remaining machine-path matches. None of the literals was a credential, URL,
ROM/BK2 payload, binary/build artifact, or other prohibited category.

The two intentionally excluded generated diagnostic logs were exactly:

- `tools/bizhawk/diag_aiz2_djf_probe_output.txt`
- `tools/bizhawk/diag_aiz2_monitor_solid_output.txt`

The diagnostic evidence allowlist was empty.

## Literal clone and ref-normalization commands

The shell already defined `OPENGGF_FEATURE_ROOT` as the absolute OpenGGF
feature checkout, `TRACECHASER_WORK_ROOT` as a newly proven-absent external
work root, `TRACECHASER_ROOT=$TRACECHASER_WORK_ROOT/TraceChaser`, and
`TRACECHASER_EXTRACTION_BASE=88530afdf331fb152f88a4d14adb8f93f2299ff6`.
The literal clone command was:

```bash
git clone --no-local "$OPENGGF_FEATURE_ROOT" "$TRACECHASER_ROOT"
```

The clone was normalized before filtering with these literal commands:

```bash
git -C "$TRACECHASER_ROOT" switch --detach "$TRACECHASER_EXTRACTION_BASE"
git -C "$TRACECHASER_ROOT" switch -c main
git -C "$TRACECHASER_ROOT" branch -D feature/ai-tracechaser-extraction-design
git -C "$TRACECHASER_ROOT" remote remove origin
git -C "$TRACECHASER_ROOT" for-each-ref --format='delete %(refname)' refs/tags | git -C "$TRACECHASER_ROOT" update-ref --stdin
```

Immediately before filtering, `refs/heads/main` was the only local branch and
resolved to the exact extraction base; no remote or tag remained.

## Literal filter command

The reviewed command below was generated directly from the checked-in path
map, with one explicit `--path` and `--path-rename` pair per entry, then
executed as written. The only additional transformation was the reviewed exact
literal replacement file described above.

```bash
git filter-repo --force \
  --path 'LICENSE' --path-rename 'LICENSE:LICENSE' \
  --path 'src/test/resources/audio/parity/s1/normalization-contract-v1.json' --path-rename 'src/test/resources/audio/parity/s1/normalization-contract-v1.json:contracts/audio/normalization-contract-v1.json' \
  --path 'src/test/resources/bizhawk/probe_runtime_contract_test.lua' --path-rename 'src/test/resources/bizhawk/probe_runtime_contract_test.lua:testing/lua/probe_runtime_contract_test.lua' \
  --path 'src/test/resources/bizhawk/s1_audio_parity_contract_test.lua' --path-rename 'src/test/resources/bizhawk/s1_audio_parity_contract_test.lua:testing/lua/s1_audio_parity_contract_test.lua' \
  --path 'src/test/resources/bizhawk/s1_gameplay_audio_timeline_contract_test.lua' --path-rename 'src/test/resources/bizhawk/s1_gameplay_audio_timeline_contract_test.lua:testing/lua/s1_gameplay_audio_timeline_contract_test.lua' \
  --path 'tools/bizhawk-headless/AGENTS.md' --path-rename 'tools/bizhawk-headless/AGENTS.md:bizhawk-headless/AGENTS.md' \
  --path 'tools/bizhawk-headless/BizHawk.Headless.Gpgx.csproj' --path-rename 'tools/bizhawk-headless/BizHawk.Headless.Gpgx.csproj:bizhawk-headless/BizHawk.Headless.Gpgx.csproj' \
  --path 'tools/bizhawk-headless/BizHawk.Headless.Gpgx.Tests.csproj' --path-rename 'tools/bizhawk-headless/BizHawk.Headless.Gpgx.Tests.csproj:bizhawk-headless/BizHawk.Headless.Gpgx.Tests.csproj' \
  --path 'tools/bizhawk-headless/build.sh' --path-rename 'tools/bizhawk-headless/build.sh:bizhawk-headless/build.sh' \
  --path 'tools/bizhawk-headless/CLAUDE.md' --path-rename 'tools/bizhawk-headless/CLAUDE.md:bizhawk-headless/CLAUDE.md' \
  --path 'tools/bizhawk-headless/common-env.sh' --path-rename 'tools/bizhawk-headless/common-env.sh:bizhawk-headless/common-env.sh' \
  --path 'tools/bizhawk-headless/docs/s1-complete-run-behavior.md' --path-rename 'tools/bizhawk-headless/docs/s1-complete-run-behavior.md:bizhawk-headless/docs/s1-complete-run-behavior.md' \
  --path 'tools/bizhawk-headless/docs/s1-run-mode-behavior.md' --path-rename 'tools/bizhawk-headless/docs/s1-run-mode-behavior.md:bizhawk-headless/docs/s1-run-mode-behavior.md' \
  --path 'tools/bizhawk-headless/docs/s1-trace-recorder-behavior.md' --path-rename 'tools/bizhawk-headless/docs/s1-trace-recorder-behavior.md:bizhawk-headless/docs/s1-trace-recorder-behavior.md' \
  --path 'tools/bizhawk-headless/docs/s2-run-mode-behavior.md' --path-rename 'tools/bizhawk-headless/docs/s2-run-mode-behavior.md:bizhawk-headless/docs/s2-run-mode-behavior.md' \
  --path 'tools/bizhawk-headless/docs/s2-trace-recorder-behavior.md' --path-rename 'tools/bizhawk-headless/docs/s2-trace-recorder-behavior.md:bizhawk-headless/docs/s2-trace-recorder-behavior.md' \
  --path 'tools/bizhawk-headless/docs/s3k-aux-events.md' --path-rename 'tools/bizhawk-headless/docs/s3k-aux-events.md:bizhawk-headless/docs/s3k-aux-events.md' \
  --path 'tools/bizhawk-headless/docs/s3k-complete-run-behavior.md' --path-rename 'tools/bizhawk-headless/docs/s3k-complete-run-behavior.md:bizhawk-headless/docs/s3k-complete-run-behavior.md' \
  --path 'tools/bizhawk-headless/docs/s3k-completerun-profiles.md' --path-rename 'tools/bizhawk-headless/docs/s3k-completerun-profiles.md:bizhawk-headless/docs/s3k-completerun-profiles.md' \
  --path 'tools/bizhawk-headless/docs/s3k-profiles-and-hooks.md' --path-rename 'tools/bizhawk-headless/docs/s3k-profiles-and-hooks.md:bizhawk-headless/docs/s3k-profiles-and-hooks.md' \
  --path 'tools/bizhawk-headless/docs/s3k-run-publication.md' --path-rename 'tools/bizhawk-headless/docs/s3k-run-publication.md:bizhawk-headless/docs/s3k-run-publication.md' \
  --path 'tools/bizhawk-headless/docs/s3k-trace-recorder-behavior.md' --path-rename 'tools/bizhawk-headless/docs/s3k-trace-recorder-behavior.md:bizhawk-headless/docs/s3k-trace-recorder-behavior.md' \
  --path 'tools/bizhawk-headless/fixtures/ghz1-header.txt' --path-rename 'tools/bizhawk-headless/fixtures/ghz1-header.txt:bizhawk-headless/fixtures/ghz1-header.txt' \
  --path 'tools/bizhawk-headless/fixtures/ghz1-input-prefix.txt' --path-rename 'tools/bizhawk-headless/fixtures/ghz1-input-prefix.txt:bizhawk-headless/fixtures/ghz1-input-prefix.txt' \
  --path 'tools/bizhawk-headless/fixtures/ghz1-sync-settings.json' --path-rename 'tools/bizhawk-headless/fixtures/ghz1-sync-settings.json:bizhawk-headless/fixtures/ghz1-sync-settings.json' \
  --path 'tools/bizhawk-headless/fixtures/gpgx-audio-capability-v1.json' --path-rename 'tools/bizhawk-headless/fixtures/gpgx-audio-capability-v1.json:bizhawk-headless/fixtures/gpgx-audio-capability-v1.json' \
  --path 'tools/bizhawk-headless/fixtures/gpgx-audio-service-manifests-v1.json' --path-rename 'tools/bizhawk-headless/fixtures/gpgx-audio-service-manifests-v1.json:bizhawk-headless/fixtures/gpgx-audio-service-manifests-v1.json' \
  --path 'tools/bizhawk-headless/fixtures/s1-audio-service-manifest-v1.json' --path-rename 'tools/bizhawk-headless/fixtures/s1-audio-service-manifest-v1.json:bizhawk-headless/fixtures/s1-audio-service-manifest-v1.json' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-lab/0001-trace-ym-write-cycles.patch' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-lab/0001-trace-ym-write-cycles.patch:bizhawk-headless/native/gpgx-audio-lab/0001-trace-ym-write-cycles.patch' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-lab/build-representative-ledger.sh' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-lab/build-representative-ledger.sh:bizhawk-headless/native/gpgx-audio-lab/build-representative-ledger.sh' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-lab/capture-ym-write-timing.sh' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-lab/capture-ym-write-timing.sh:bizhawk-headless/native/gpgx-audio-lab/capture-ym-write-timing.sh' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-lab/unowned-chip-write-selftest.c' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-lab/unowned-chip-write-selftest.c:bizhawk-headless/native/gpgx-audio-lab/unowned-chip-write-selftest.c' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/0001-buffer-z80-audio-events.patch' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/0001-buffer-z80-audio-events.patch:bizhawk-headless/native/gpgx-audio-observer/0001-buffer-z80-audio-events.patch' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/0002-s3k-audio-parity-events.patch' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/0002-s3k-audio-parity-events.patch:bizhawk-headless/native/gpgx-audio-observer/0002-s3k-audio-parity-events.patch' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/0003-s3k-chip-pcm-events.patch' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/0003-s3k-chip-pcm-events.patch:bizhawk-headless/native/gpgx-audio-observer/0003-s3k-chip-pcm-events.patch' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/artifact-lock.json' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/artifact-lock.json:bizhawk-headless/native/gpgx-audio-observer/artifact-lock.json' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/build-core.sh' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/build-core.sh:bizhawk-headless/native/gpgx-audio-observer/build-core.sh' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/build-recipe.json' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/build-recipe.json:bizhawk-headless/native/gpgx-audio-observer/build-recipe.json' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/fetch-source.sh' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/fetch-source.sh:bizhawk-headless/native/gpgx-audio-observer/fetch-source.sh' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/install-core.sh' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/install-core.sh:bizhawk-headless/native/gpgx-audio-observer/install-core.sh' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/managed-nuget-manifest.json' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/managed-nuget-manifest.json:bizhawk-headless/native/gpgx-audio-observer/managed-nuget-manifest.json' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/managed-toolchain-lock.json' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/managed-toolchain-lock.json:bizhawk-headless/native/gpgx-audio-observer/managed-toolchain-lock.json' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/notices/zstd-LICENSE' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/notices/zstd-LICENSE:bizhawk-headless/native/gpgx-audio-observer/notices/zstd-LICENSE' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/prepare-managed-inputs.sh' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/prepare-managed-inputs.sh:bizhawk-headless/native/gpgx-audio-observer/prepare-managed-inputs.sh' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/prepare-toolchain.sh' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/prepare-toolchain.sh:bizhawk-headless/native/gpgx-audio-observer/prepare-toolchain.sh' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/README.md' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/README.md:bizhawk-headless/native/gpgx-audio-observer/README.md' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/reproduce-stock-core.sh' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/reproduce-stock-core.sh:bizhawk-headless/native/gpgx-audio-observer/reproduce-stock-core.sh' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/reproduce-stock-managed.sh' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/reproduce-stock-managed.sh:bizhawk-headless/native/gpgx-audio-observer/reproduce-stock-managed.sh' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/reproduce-stock-pair.sh' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/reproduce-stock-pair.sh:bizhawk-headless/native/gpgx-audio-observer/reproduce-stock-pair.sh' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/s3k-parity-artifact-lock.json' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/s3k-parity-artifact-lock.json:bizhawk-headless/native/gpgx-audio-observer/s3k-parity-artifact-lock.json' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/s3k-pcm-artifact-lock.json' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/s3k-pcm-artifact-lock.json:bizhawk-headless/native/gpgx-audio-observer/s3k-pcm-artifact-lock.json' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/secure-runtime.sh' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/secure-runtime.sh:bizhawk-headless/native/gpgx-audio-observer/secure-runtime.sh' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/arming_harness.c' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/arming_harness.c:bizhawk-headless/native/gpgx-audio-observer/selftest/arming_harness.c' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/cpu_boundary_harness.c' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/cpu_boundary_harness.c:bizhawk-headless/native/gpgx-audio-observer/selftest/cpu_boundary_harness.c' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/emulibc.h' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/emulibc.h:bizhawk-headless/native/gpgx-audio-observer/selftest/emulibc.h' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/harness.c' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/harness.c:bizhawk-headless/native/gpgx-audio-observer/selftest/harness.c' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/m68k_boundary_harness.c' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/m68k_boundary_harness.c:bizhawk-headless/native/gpgx-audio-observer/selftest/m68k_boundary_harness.c' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/matrix_harness.c' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/matrix_harness.c:bizhawk-headless/native/gpgx-audio-observer/selftest/matrix_harness.c' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/run.sh' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/run.sh:bizhawk-headless/native/gpgx-audio-observer/selftest/run.sh' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/s3k_parity_harness.c' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/s3k_parity_harness.c:bizhawk-headless/native/gpgx-audio-observer/selftest/s3k_parity_harness.c' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/s3k_pcm_harness.c' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/s3k_pcm_harness.c:bizhawk-headless/native/gpgx-audio-observer/selftest/s3k_pcm_harness.c' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/s3k_pcm_replay_harness.c' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/s3k_pcm_replay_harness.c:bizhawk-headless/native/gpgx-audio-observer/selftest/s3k_pcm_replay_harness.c' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/s3k-parity-run.sh' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/s3k-parity-run.sh:bizhawk-headless/native/gpgx-audio-observer/selftest/s3k-parity-run.sh' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/s3k-pcm-run.sh' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/s3k-pcm-run.sh:bizhawk-headless/native/gpgx-audio-observer/selftest/s3k-pcm-run.sh' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/shared.h' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/shared.h:bizhawk-headless/native/gpgx-audio-observer/selftest/shared.h' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/wrap_harness.c' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/selftest/wrap_harness.c:bizhawk-headless/native/gpgx-audio-observer/selftest/wrap_harness.c' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/source-lock.json' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/source-lock.json:bizhawk-headless/native/gpgx-audio-observer/source-lock.json' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/task7-build-recipe.json' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/task7-build-recipe.json:bizhawk-headless/native/gpgx-audio-observer/task7-build-recipe.json' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/toolchain-lock.json' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/toolchain-lock.json:bizhawk-headless/native/gpgx-audio-observer/toolchain-lock.json' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/TRUST.md' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/TRUST.md:bizhawk-headless/native/gpgx-audio-observer/TRUST.md' \
  --path 'tools/bizhawk-headless/native/gpgx-audio-observer/verify-inputs.sh' --path-rename 'tools/bizhawk-headless/native/gpgx-audio-observer/verify-inputs.sh:bizhawk-headless/native/gpgx-audio-observer/verify-inputs.sh' \
  --path 'tools/bizhawk-headless/README.md' --path-rename 'tools/bizhawk-headless/README.md:bizhawk-headless/README.md' \
  --path 'tools/bizhawk-headless/run.sh' --path-rename 'tools/bizhawk-headless/run.sh:bizhawk-headless/run.sh' \
  --path 'tools/bizhawk-headless/src/Audio/CompleteRunAudioObserver.cs' --path-rename 'tools/bizhawk-headless/src/Audio/CompleteRunAudioObserver.cs:bizhawk-headless/src/Audio/CompleteRunAudioObserver.cs' \
  --path 'tools/bizhawk-headless/src/Audio/GpgxAudioServiceManifest.cs' --path-rename 'tools/bizhawk-headless/src/Audio/GpgxAudioServiceManifest.cs:bizhawk-headless/src/Audio/GpgxAudioServiceManifest.cs' \
  --path 'tools/bizhawk-headless/src/Audio/GpgxAudioTraceEvent.cs' --path-rename 'tools/bizhawk-headless/src/Audio/GpgxAudioTraceEvent.cs:bizhawk-headless/src/Audio/GpgxAudioTraceEvent.cs' \
  --path 'tools/bizhawk-headless/src/Audio/GpgxAudioTraceNative.cs' --path-rename 'tools/bizhawk-headless/src/Audio/GpgxAudioTraceNative.cs:bizhawk-headless/src/Audio/GpgxAudioTraceNative.cs' \
  --path 'tools/bizhawk-headless/src/Audio/IGpgxAudioTraceApi.cs' --path-rename 'tools/bizhawk-headless/src/Audio/IGpgxAudioTraceApi.cs:bizhawk-headless/src/Audio/IGpgxAudioTraceApi.cs' \
  --path 'tools/bizhawk-headless/src/Audio/S1CompleteRunAudioReferenceCapture.cs' --path-rename 'tools/bizhawk-headless/src/Audio/S1CompleteRunAudioReferenceCapture.cs:bizhawk-headless/src/Audio/S1CompleteRunAudioReferenceCapture.cs' \
  --path 'tools/bizhawk-headless/src/Audio/S2AudioObserverProfile.cs' --path-rename 'tools/bizhawk-headless/src/Audio/S2AudioObserverProfile.cs:bizhawk-headless/src/Audio/S2AudioObserverProfile.cs' \
  --path 'tools/bizhawk-headless/src/Audio/S3kAudioObserverProfile.cs' --path-rename 'tools/bizhawk-headless/src/Audio/S3kAudioObserverProfile.cs:bizhawk-headless/src/Audio/S3kAudioObserverProfile.cs' \
  --path 'tools/bizhawk-headless/src/Bk2/Bk2Frame.cs' --path-rename 'tools/bizhawk-headless/src/Bk2/Bk2Frame.cs:bizhawk-headless/src/Bk2/Bk2Frame.cs' \
  --path 'tools/bizhawk-headless/src/Bk2/Bk2Movie.cs' --path-rename 'tools/bizhawk-headless/src/Bk2/Bk2Movie.cs:bizhawk-headless/src/Bk2/Bk2Movie.cs' \
  --path 'tools/bizhawk-headless/src/Bk2/Bk2Reader.cs' --path-rename 'tools/bizhawk-headless/src/Bk2/Bk2Reader.cs:bizhawk-headless/src/Bk2/Bk2Reader.cs' \
  --path 'tools/bizhawk-headless/src/Bootstrap/BizHawkInstallation.cs' --path-rename 'tools/bizhawk-headless/src/Bootstrap/BizHawkInstallation.cs:bizhawk-headless/src/Bootstrap/BizHawkInstallation.cs' \
  --path 'tools/bizhawk-headless/src/Bootstrap/LinuxPathEntry.cs' --path-rename 'tools/bizhawk-headless/src/Bootstrap/LinuxPathEntry.cs:bizhawk-headless/src/Bootstrap/LinuxPathEntry.cs' \
  --path 'tools/bizhawk-headless/src/Bootstrap/NativeStandardOutputSilencer.cs' --path-rename 'tools/bizhawk-headless/src/Bootstrap/NativeStandardOutputSilencer.cs:bizhawk-headless/src/Bootstrap/NativeStandardOutputSilencer.cs' \
  --path 'tools/bizhawk-headless/src/Bootstrap/RomIdentity.cs' --path-rename 'tools/bizhawk-headless/src/Bootstrap/RomIdentity.cs:bizhawk-headless/src/Bootstrap/RomIdentity.cs' \
  --path 'tools/bizhawk-headless/src/Core/GpgxAudioObserverAdapter.cs' --path-rename 'tools/bizhawk-headless/src/Core/GpgxAudioObserverAdapter.cs:bizhawk-headless/src/Core/GpgxAudioObserverAdapter.cs' \
  --path 'tools/bizhawk-headless/src/Core/GpgxHost.AudioObserver.cs' --path-rename 'tools/bizhawk-headless/src/Core/GpgxHost.AudioObserver.cs:bizhawk-headless/src/Core/GpgxHost.AudioObserver.cs' \
  --path 'tools/bizhawk-headless/src/Core/GpgxHost.cs' --path-rename 'tools/bizhawk-headless/src/Core/GpgxHost.cs:bizhawk-headless/src/Core/GpgxHost.cs' \
  --path 'tools/bizhawk-headless/src/Core/GpgxS3kAudioParityDepartures.cs' --path-rename 'tools/bizhawk-headless/src/Core/GpgxS3kAudioParityDepartures.cs:bizhawk-headless/src/Core/GpgxS3kAudioParityDepartures.cs' \
  --path 'tools/bizhawk-headless/src/Core/IGpgxHost.cs' --path-rename 'tools/bizhawk-headless/src/Core/IGpgxHost.cs:bizhawk-headless/src/Core/IGpgxHost.cs' \
  --path 'tools/bizhawk-headless/src/Core/IMainRamWriter.cs' --path-rename 'tools/bizhawk-headless/src/Core/IMainRamWriter.cs:bizhawk-headless/src/Core/IMainRamWriter.cs' \
  --path 'tools/bizhawk-headless/src/Core/MutableController.cs' --path-rename 'tools/bizhawk-headless/src/Core/MutableController.cs:bizhawk-headless/src/Core/MutableController.cs' \
  --path 'tools/bizhawk-headless/src/Core/NoFirmwareProvider.cs' --path-rename 'tools/bizhawk-headless/src/Core/NoFirmwareProvider.cs:bizhawk-headless/src/Core/NoFirmwareProvider.cs' \
  --path 'tools/bizhawk-headless/src/Core/RomAsset.cs' --path-rename 'tools/bizhawk-headless/src/Core/RomAsset.cs:bizhawk-headless/src/Core/RomAsset.cs' \
  --path 'tools/bizhawk-headless/src/Program.cs' --path-rename 'tools/bizhawk-headless/src/Program.cs:bizhawk-headless/src/Program.cs' \
  --path 'tools/bizhawk-headless/src/Recording/DynamicArtCaptureRowBuffer.cs' --path-rename 'tools/bizhawk-headless/src/Recording/DynamicArtCaptureRowBuffer.cs:bizhawk-headless/src/Recording/DynamicArtCaptureRowBuffer.cs' \
  --path 'tools/bizhawk-headless/src/Recording/DynamicArtRomProfile.cs' --path-rename 'tools/bizhawk-headless/src/Recording/DynamicArtRomProfile.cs:bizhawk-headless/src/Recording/DynamicArtRomProfile.cs' \
  --path 'tools/bizhawk-headless/src/Recording/DynamicArtTransferState.cs' --path-rename 'tools/bizhawk-headless/src/Recording/DynamicArtTransferState.cs:bizhawk-headless/src/Recording/DynamicArtTransferState.cs' \
  --path 'tools/bizhawk-headless/src/Recording/HardwareTimingEventEngine.cs' --path-rename 'tools/bizhawk-headless/src/Recording/HardwareTimingEventEngine.cs:bizhawk-headless/src/Recording/HardwareTimingEventEngine.cs' \
  --path 'tools/bizhawk-headless/src/Recording/LazyOpenTextWriter.cs' --path-rename 'tools/bizhawk-headless/src/Recording/LazyOpenTextWriter.cs:bizhawk-headless/src/Recording/LazyOpenTextWriter.cs' \
  --path 'tools/bizhawk-headless/src/Recording/LoadQueueStateEvent.cs' --path-rename 'tools/bizhawk-headless/src/Recording/LoadQueueStateEvent.cs:bizhawk-headless/src/Recording/LoadQueueStateEvent.cs' \
  --path 'tools/bizhawk-headless/src/Recording/LoadQueueStateProjector.cs' --path-rename 'tools/bizhawk-headless/src/Recording/LoadQueueStateProjector.cs:bizhawk-headless/src/Recording/LoadQueueStateProjector.cs' \
  --path 'tools/bizhawk-headless/src/Recording/NoReplacePublisher.cs' --path-rename 'tools/bizhawk-headless/src/Recording/NoReplacePublisher.cs:bizhawk-headless/src/Recording/NoReplacePublisher.cs' \
  --path 'tools/bizhawk-headless/src/Recording/RunManifestWriter.cs' --path-rename 'tools/bizhawk-headless/src/Recording/RunManifestWriter.cs:bizhawk-headless/src/Recording/RunManifestWriter.cs' \
  --path 'tools/bizhawk-headless/src/Recording/RunSegmentSink.cs' --path-rename 'tools/bizhawk-headless/src/Recording/RunSegmentSink.cs:bizhawk-headless/src/Recording/RunSegmentSink.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S1AuxEventEngine.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S1AuxEventEngine.cs:bizhawk-headless/src/Recording/S1AuxEventEngine.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S1CompleteRunMetadataWriter.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S1CompleteRunMetadataWriter.cs:bizhawk-headless/src/Recording/S1CompleteRunMetadataWriter.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S1CreditsDemoCaptureRunner.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S1CreditsDemoCaptureRunner.cs:bizhawk-headless/src/Recording/S1CreditsDemoCaptureRunner.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S1CreditsDemoCatalog.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S1CreditsDemoCatalog.cs:bizhawk-headless/src/Recording/S1CreditsDemoCatalog.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S1CreditsDemoCollectionSink.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S1CreditsDemoCollectionSink.cs:bizhawk-headless/src/Recording/S1CreditsDemoCollectionSink.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S1CreditsDemoMetadataWriter.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S1CreditsDemoMetadataWriter.cs:bizhawk-headless/src/Recording/S1CreditsDemoMetadataWriter.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S1CreditsDemoRawHostEvidence.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S1CreditsDemoRawHostEvidence.cs:bizhawk-headless/src/Recording/S1CreditsDemoRawHostEvidence.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S1DynamicArtObserver.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S1DynamicArtObserver.cs:bizhawk-headless/src/Recording/S1DynamicArtObserver.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S1InputMask.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S1InputMask.cs:bizhawk-headless/src/Recording/S1InputMask.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S1PlcHardwareTimingObserver.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S1PlcHardwareTimingObserver.cs:bizhawk-headless/src/Recording/S1PlcHardwareTimingObserver.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S1Ram.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S1Ram.cs:bizhawk-headless/src/Recording/S1Ram.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S1RunCaptureRunner.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S1RunCaptureRunner.cs:bizhawk-headless/src/Recording/S1RunCaptureRunner.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S1RunManifestWriter.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S1RunManifestWriter.cs:bizhawk-headless/src/Recording/S1RunManifestWriter.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S1SmokeRecorder.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S1SmokeRecorder.cs:bizhawk-headless/src/Recording/S1SmokeRecorder.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S1SpecialStageCsvWriter.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S1SpecialStageCsvWriter.cs:bizhawk-headless/src/Recording/S1SpecialStageCsvWriter.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S1SpecialStageMetadataWriter.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S1SpecialStageMetadataWriter.cs:bizhawk-headless/src/Recording/S1SpecialStageMetadataWriter.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S1TraceCaptureRunner.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S1TraceCaptureRunner.cs:bizhawk-headless/src/Recording/S1TraceCaptureRunner.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S1TraceCsvWriter.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S1TraceCsvWriter.cs:bizhawk-headless/src/Recording/S1TraceCsvWriter.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S1TraceMetadataWriter.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S1TraceMetadataWriter.cs:bizhawk-headless/src/Recording/S1TraceMetadataWriter.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S2AuxEventEngine.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S2AuxEventEngine.cs:bizhawk-headless/src/Recording/S2AuxEventEngine.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S2CompleteAudioCaptureRunner.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S2CompleteAudioCaptureRunner.cs:bizhawk-headless/src/Recording/S2CompleteAudioCaptureRunner.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S2CompleteAudioRawSink.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S2CompleteAudioRawSink.cs:bizhawk-headless/src/Recording/S2CompleteAudioRawSink.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S2DynamicArtObserver.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S2DynamicArtObserver.cs:bizhawk-headless/src/Recording/S2DynamicArtObserver.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S2Ram.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S2Ram.cs:bizhawk-headless/src/Recording/S2Ram.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S2RunCaptureRunner.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S2RunCaptureRunner.cs:bizhawk-headless/src/Recording/S2RunCaptureRunner.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S2RunManifestWriter.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S2RunManifestWriter.cs:bizhawk-headless/src/Recording/S2RunManifestWriter.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S2SpecialStageAuxEventEngine.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S2SpecialStageAuxEventEngine.cs:bizhawk-headless/src/Recording/S2SpecialStageAuxEventEngine.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S2SpecialStageCaptureRunner.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S2SpecialStageCaptureRunner.cs:bizhawk-headless/src/Recording/S2SpecialStageCaptureRunner.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S2SpecialStageCsvWriter.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S2SpecialStageCsvWriter.cs:bizhawk-headless/src/Recording/S2SpecialStageCsvWriter.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S2SpecialStageMetadataWriter.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S2SpecialStageMetadataWriter.cs:bizhawk-headless/src/Recording/S2SpecialStageMetadataWriter.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S2SpecialStageRunObjectsObserver.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S2SpecialStageRunObjectsObserver.cs:bizhawk-headless/src/Recording/S2SpecialStageRunObjectsObserver.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S2TraceCaptureRunner.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S2TraceCaptureRunner.cs:bizhawk-headless/src/Recording/S2TraceCaptureRunner.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S2TraceCsvWriter.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S2TraceCsvWriter.cs:bizhawk-headless/src/Recording/S2TraceCsvWriter.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S2TraceMetadataWriter.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S2TraceMetadataWriter.cs:bizhawk-headless/src/Recording/S2TraceMetadataWriter.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S2Zones.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S2Zones.cs:bizhawk-headless/src/Recording/S2Zones.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S3KAuxEventEngine.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S3KAuxEventEngine.cs:bizhawk-headless/src/Recording/S3KAuxEventEngine.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S3kCompleteAudioCaptureRunner.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S3kCompleteAudioCaptureRunner.cs:bizhawk-headless/src/Recording/S3kCompleteAudioCaptureRunner.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S3kCompleteAudioRawSink.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S3kCompleteAudioRawSink.cs:bizhawk-headless/src/Recording/S3kCompleteAudioRawSink.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S3KCompleteRunCaptureRunner.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S3KCompleteRunCaptureRunner.cs:bizhawk-headless/src/Recording/S3KCompleteRunCaptureRunner.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S3KCompleteRunMetadataWriter.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S3KCompleteRunMetadataWriter.cs:bizhawk-headless/src/Recording/S3KCompleteRunMetadataWriter.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S3KCompleteRunSegmenter.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S3KCompleteRunSegmenter.cs:bizhawk-headless/src/Recording/S3KCompleteRunSegmenter.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S3KRam.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S3KRam.cs:bizhawk-headless/src/Recording/S3KRam.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S3KRunManifestWriter.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S3KRunManifestWriter.cs:bizhawk-headless/src/Recording/S3KRunManifestWriter.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S3KSpecialStageCsvWriter.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S3KSpecialStageCsvWriter.cs:bizhawk-headless/src/Recording/S3KSpecialStageCsvWriter.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S3KStagedSegmentSink.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S3KStagedSegmentSink.cs:bizhawk-headless/src/Recording/S3KStagedSegmentSink.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S3KTraceCaptureRunner.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S3KTraceCaptureRunner.cs:bizhawk-headless/src/Recording/S3KTraceCaptureRunner.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S3KTraceCsvWriter.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S3KTraceCsvWriter.cs:bizhawk-headless/src/Recording/S3KTraceCsvWriter.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S3KTraceMetadataWriter.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S3KTraceMetadataWriter.cs:bizhawk-headless/src/Recording/S3KTraceMetadataWriter.cs' \
  --path 'tools/bizhawk-headless/src/Recording/S3KZoneTokens.cs' --path-rename 'tools/bizhawk-headless/src/Recording/S3KZoneTokens.cs:bizhawk-headless/src/Recording/S3KZoneTokens.cs' \
  --path 'tools/bizhawk-headless/src/Recording/SmokeCaptureRunner.cs' --path-rename 'tools/bizhawk-headless/src/Recording/SmokeCaptureRunner.cs:bizhawk-headless/src/Recording/SmokeCaptureRunner.cs' \
  --path 'tools/bizhawk-headless/src/Recording/StagedRunSegmentSink.cs' --path-rename 'tools/bizhawk-headless/src/Recording/StagedRunSegmentSink.cs:bizhawk-headless/src/Recording/StagedRunSegmentSink.cs' \
  --path 'tools/bizhawk-headless/src/Recording/TraceContract.cs' --path-rename 'tools/bizhawk-headless/src/Recording/TraceContract.cs:bizhawk-headless/src/Recording/TraceContract.cs' \
  --path 'tools/bizhawk-headless/src/Recording/TracePayloadCompressor.cs' --path-rename 'tools/bizhawk-headless/src/Recording/TracePayloadCompressor.cs:bizhawk-headless/src/Recording/TracePayloadCompressor.cs' \
  --path 'tools/bizhawk-headless/test.sh' --path-rename 'tools/bizhawk-headless/test.sh:bizhawk-headless/test.sh' \
  --path 'tools/bizhawk-headless/tests/AssertEx.cs' --path-rename 'tools/bizhawk-headless/tests/AssertEx.cs:bizhawk-headless/tests/AssertEx.cs' \
  --path 'tools/bizhawk-headless/tests/Bk2ReaderTests.cs' --path-rename 'tools/bizhawk-headless/tests/Bk2ReaderTests.cs:bizhawk-headless/tests/Bk2ReaderTests.cs' \
  --path 'tools/bizhawk-headless/tests/BootstrapTests.cs' --path-rename 'tools/bizhawk-headless/tests/BootstrapTests.cs:bizhawk-headless/tests/BootstrapTests.cs' \
  --path 'tools/bizhawk-headless/tests/CompleteRunAudioObserverTests.cs' --path-rename 'tools/bizhawk-headless/tests/CompleteRunAudioObserverTests.cs:bizhawk-headless/tests/CompleteRunAudioObserverTests.cs' \
  --path 'tools/bizhawk-headless/tests/DynamicArtRomProfileTests.cs' --path-rename 'tools/bizhawk-headless/tests/DynamicArtRomProfileTests.cs:bizhawk-headless/tests/DynamicArtRomProfileTests.cs' \
  --path 'tools/bizhawk-headless/tests/DynamicArtTransferStateTests.cs' --path-rename 'tools/bizhawk-headless/tests/DynamicArtTransferStateTests.cs:bizhawk-headless/tests/DynamicArtTransferStateTests.cs' \
  --path 'tools/bizhawk-headless/tests/EndToEndTests.cs' --path-rename 'tools/bizhawk-headless/tests/EndToEndTests.cs:bizhawk-headless/tests/EndToEndTests.cs' \
  --path 'tools/bizhawk-headless/tests/FakeS1Host.cs' --path-rename 'tools/bizhawk-headless/tests/FakeS1Host.cs:bizhawk-headless/tests/FakeS1Host.cs' \
  --path 'tools/bizhawk-headless/tests/GpgxAudioObserverBuildTests.cs' --path-rename 'tools/bizhawk-headless/tests/GpgxAudioObserverBuildTests.cs:bizhawk-headless/tests/GpgxAudioObserverBuildTests.cs' \
  --path 'tools/bizhawk-headless/tests/GpgxAudioObserverSourceLockTests.cs' --path-rename 'tools/bizhawk-headless/tests/GpgxAudioObserverSourceLockTests.cs:bizhawk-headless/tests/GpgxAudioObserverSourceLockTests.cs' \
  --path 'tools/bizhawk-headless/tests/GpgxAudioTraceNativeTests.cs' --path-rename 'tools/bizhawk-headless/tests/GpgxAudioTraceNativeTests.cs:bizhawk-headless/tests/GpgxAudioTraceNativeTests.cs' \
  --path 'tools/bizhawk-headless/tests/GpgxHostTests.cs' --path-rename 'tools/bizhawk-headless/tests/GpgxHostTests.cs:bizhawk-headless/tests/GpgxHostTests.cs' \
  --path 'tools/bizhawk-headless/tests/GpgxS3kAudioParityManifestTests.cs' --path-rename 'tools/bizhawk-headless/tests/GpgxS3kAudioParityManifestTests.cs:bizhawk-headless/tests/GpgxS3kAudioParityManifestTests.cs' \
  --path 'tools/bizhawk-headless/tests/GpgxYmWriteTimingLabTests.cs' --path-rename 'tools/bizhawk-headless/tests/GpgxYmWriteTimingLabTests.cs:bizhawk-headless/tests/GpgxYmWriteTimingLabTests.cs' \
  --path 'tools/bizhawk-headless/tests/GpgxZ80AudioCapabilityTests.cs' --path-rename 'tools/bizhawk-headless/tests/GpgxZ80AudioCapabilityTests.cs:bizhawk-headless/tests/GpgxZ80AudioCapabilityTests.cs' \
  --path 'tools/bizhawk-headless/tests/HardwareTimingEventEngineTests.cs' --path-rename 'tools/bizhawk-headless/tests/HardwareTimingEventEngineTests.cs:bizhawk-headless/tests/HardwareTimingEventEngineTests.cs' \
  --path 'tools/bizhawk-headless/tests/LoadQueueStateEventTests.cs' --path-rename 'tools/bizhawk-headless/tests/LoadQueueStateEventTests.cs:bizhawk-headless/tests/LoadQueueStateEventTests.cs' \
  --path 'tools/bizhawk-headless/tests/NoReplacePublisherTests.cs' --path-rename 'tools/bizhawk-headless/tests/NoReplacePublisherTests.cs:bizhawk-headless/tests/NoReplacePublisherTests.cs' \
  --path 'tools/bizhawk-headless/tests/RunSegmentCollector.cs' --path-rename 'tools/bizhawk-headless/tests/RunSegmentCollector.cs:bizhawk-headless/tests/RunSegmentCollector.cs' \
  --path 'tools/bizhawk-headless/tests/S1AuxEventEngineTests.cs' --path-rename 'tools/bizhawk-headless/tests/S1AuxEventEngineTests.cs:bizhawk-headless/tests/S1AuxEventEngineTests.cs' \
  --path 'tools/bizhawk-headless/tests/S1CompleteRunAudioReferenceCaptureTests.cs' --path-rename 'tools/bizhawk-headless/tests/S1CompleteRunAudioReferenceCaptureTests.cs:bizhawk-headless/tests/S1CompleteRunAudioReferenceCaptureTests.cs' \
  --path 'tools/bizhawk-headless/tests/S1CompleteRunMetadataWriterTests.cs' --path-rename 'tools/bizhawk-headless/tests/S1CompleteRunMetadataWriterTests.cs:bizhawk-headless/tests/S1CompleteRunMetadataWriterTests.cs' \
  --path 'tools/bizhawk-headless/tests/S1CreditsDemoCaptureRunnerTests.cs' --path-rename 'tools/bizhawk-headless/tests/S1CreditsDemoCaptureRunnerTests.cs:bizhawk-headless/tests/S1CreditsDemoCaptureRunnerTests.cs' \
  --path 'tools/bizhawk-headless/tests/S1CreditsDemoDifferentialTests.cs' --path-rename 'tools/bizhawk-headless/tests/S1CreditsDemoDifferentialTests.cs:bizhawk-headless/tests/S1CreditsDemoDifferentialTests.cs' \
  --path 'tools/bizhawk-headless/tests/S1DynamicArtObserverTests.cs' --path-rename 'tools/bizhawk-headless/tests/S1DynamicArtObserverTests.cs:bizhawk-headless/tests/S1DynamicArtObserverTests.cs' \
  --path 'tools/bizhawk-headless/tests/S1PlcHardwareTimingObserverTests.cs' --path-rename 'tools/bizhawk-headless/tests/S1PlcHardwareTimingObserverTests.cs:bizhawk-headless/tests/S1PlcHardwareTimingObserverTests.cs' \
  --path 'tools/bizhawk-headless/tests/S1RunCaptureRunnerStageFreeTests.cs' --path-rename 'tools/bizhawk-headless/tests/S1RunCaptureRunnerStageFreeTests.cs:bizhawk-headless/tests/S1RunCaptureRunnerStageFreeTests.cs' \
  --path 'tools/bizhawk-headless/tests/S1RunCaptureRunnerTests.cs' --path-rename 'tools/bizhawk-headless/tests/S1RunCaptureRunnerTests.cs:bizhawk-headless/tests/S1RunCaptureRunnerTests.cs' \
  --path 'tools/bizhawk-headless/tests/S1RunManifestWriterTests.cs' --path-rename 'tools/bizhawk-headless/tests/S1RunManifestWriterTests.cs:bizhawk-headless/tests/S1RunManifestWriterTests.cs' \
  --path 'tools/bizhawk-headless/tests/S1SmokeRecorderTests.cs' --path-rename 'tools/bizhawk-headless/tests/S1SmokeRecorderTests.cs:bizhawk-headless/tests/S1SmokeRecorderTests.cs' \
  --path 'tools/bizhawk-headless/tests/S1SpecialStageWriterTests.cs' --path-rename 'tools/bizhawk-headless/tests/S1SpecialStageWriterTests.cs:bizhawk-headless/tests/S1SpecialStageWriterTests.cs' \
  --path 'tools/bizhawk-headless/tests/S1TraceCaptureRunnerTests.cs' --path-rename 'tools/bizhawk-headless/tests/S1TraceCaptureRunnerTests.cs:bizhawk-headless/tests/S1TraceCaptureRunnerTests.cs' \
  --path 'tools/bizhawk-headless/tests/S1TraceCsvWriterTests.cs' --path-rename 'tools/bizhawk-headless/tests/S1TraceCsvWriterTests.cs:bizhawk-headless/tests/S1TraceCsvWriterTests.cs' \
  --path 'tools/bizhawk-headless/tests/S1TraceMetadataWriterTests.cs' --path-rename 'tools/bizhawk-headless/tests/S1TraceMetadataWriterTests.cs:bizhawk-headless/tests/S1TraceMetadataWriterTests.cs' \
  --path 'tools/bizhawk-headless/tests/S2AudioObserverProfileTests.cs' --path-rename 'tools/bizhawk-headless/tests/S2AudioObserverProfileTests.cs:bizhawk-headless/tests/S2AudioObserverProfileTests.cs' \
  --path 'tools/bizhawk-headless/tests/S2AuxArmBlockTests.cs' --path-rename 'tools/bizhawk-headless/tests/S2AuxArmBlockTests.cs:bizhawk-headless/tests/S2AuxArmBlockTests.cs' \
  --path 'tools/bizhawk-headless/tests/S2AuxEventEngineTests.cs' --path-rename 'tools/bizhawk-headless/tests/S2AuxEventEngineTests.cs:bizhawk-headless/tests/S2AuxEventEngineTests.cs' \
  --path 'tools/bizhawk-headless/tests/S2CompleteAudioCaptureRunnerTests.cs' --path-rename 'tools/bizhawk-headless/tests/S2CompleteAudioCaptureRunnerTests.cs:bizhawk-headless/tests/S2CompleteAudioCaptureRunnerTests.cs' \
  --path 'tools/bizhawk-headless/tests/S2CompleteAudioRawSinkTests.cs' --path-rename 'tools/bizhawk-headless/tests/S2CompleteAudioRawSinkTests.cs:bizhawk-headless/tests/S2CompleteAudioRawSinkTests.cs' \
  --path 'tools/bizhawk-headless/tests/S2DynamicArtObserverTests.cs' --path-rename 'tools/bizhawk-headless/tests/S2DynamicArtObserverTests.cs:bizhawk-headless/tests/S2DynamicArtObserverTests.cs' \
  --path 'tools/bizhawk-headless/tests/S2RunCaptureRunnerTests.cs' --path-rename 'tools/bizhawk-headless/tests/S2RunCaptureRunnerTests.cs:bizhawk-headless/tests/S2RunCaptureRunnerTests.cs' \
  --path 'tools/bizhawk-headless/tests/S2RunManifestWriterTests.cs' --path-rename 'tools/bizhawk-headless/tests/S2RunManifestWriterTests.cs:bizhawk-headless/tests/S2RunManifestWriterTests.cs' \
  --path 'tools/bizhawk-headless/tests/S2SpecialStageCaptureRunnerTests.cs' --path-rename 'tools/bizhawk-headless/tests/S2SpecialStageCaptureRunnerTests.cs:bizhawk-headless/tests/S2SpecialStageCaptureRunnerTests.cs' \
  --path 'tools/bizhawk-headless/tests/S2SpecialStageWriterTests.cs' --path-rename 'tools/bizhawk-headless/tests/S2SpecialStageWriterTests.cs:bizhawk-headless/tests/S2SpecialStageWriterTests.cs' \
  --path 'tools/bizhawk-headless/tests/S2TraceCaptureRunnerTests.cs' --path-rename 'tools/bizhawk-headless/tests/S2TraceCaptureRunnerTests.cs:bizhawk-headless/tests/S2TraceCaptureRunnerTests.cs' \
  --path 'tools/bizhawk-headless/tests/S2TraceCsvWriterTests.cs' --path-rename 'tools/bizhawk-headless/tests/S2TraceCsvWriterTests.cs:bizhawk-headless/tests/S2TraceCsvWriterTests.cs' \
  --path 'tools/bizhawk-headless/tests/S2TraceMetadataWriterTests.cs' --path-rename 'tools/bizhawk-headless/tests/S2TraceMetadataWriterTests.cs:bizhawk-headless/tests/S2TraceMetadataWriterTests.cs' \
  --path 'tools/bizhawk-headless/tests/S3kAudioObserverProfileTests.cs' --path-rename 'tools/bizhawk-headless/tests/S3kAudioObserverProfileTests.cs:bizhawk-headless/tests/S3kAudioObserverProfileTests.cs' \
  --path 'tools/bizhawk-headless/tests/S3KAuxEventEngineTests.cs' --path-rename 'tools/bizhawk-headless/tests/S3KAuxEventEngineTests.cs:bizhawk-headless/tests/S3KAuxEventEngineTests.cs' \
  --path 'tools/bizhawk-headless/tests/S3kCompleteAudioCaptureRunnerTests.cs' --path-rename 'tools/bizhawk-headless/tests/S3kCompleteAudioCaptureRunnerTests.cs:bizhawk-headless/tests/S3kCompleteAudioCaptureRunnerTests.cs' \
  --path 'tools/bizhawk-headless/tests/S3kCompleteAudioRawSinkTests.cs' --path-rename 'tools/bizhawk-headless/tests/S3kCompleteAudioRawSinkTests.cs:bizhawk-headless/tests/S3kCompleteAudioRawSinkTests.cs' \
  --path 'tools/bizhawk-headless/tests/S3KCompleteRunProfileTests.cs' --path-rename 'tools/bizhawk-headless/tests/S3KCompleteRunProfileTests.cs:bizhawk-headless/tests/S3KCompleteRunProfileTests.cs' \
  --path 'tools/bizhawk-headless/tests/S3KCompleteRunSegmenterTests.cs' --path-rename 'tools/bizhawk-headless/tests/S3KCompleteRunSegmenterTests.cs:bizhawk-headless/tests/S3KCompleteRunSegmenterTests.cs' \
  --path 'tools/bizhawk-headless/tests/S3KHookAbsenceTests.cs' --path-rename 'tools/bizhawk-headless/tests/S3KHookAbsenceTests.cs:bizhawk-headless/tests/S3KHookAbsenceTests.cs' \
  --path 'tools/bizhawk-headless/tests/S3kSfxLifecycleReferenceCaptureTests.cs' --path-rename 'tools/bizhawk-headless/tests/S3kSfxLifecycleReferenceCaptureTests.cs:bizhawk-headless/tests/S3kSfxLifecycleReferenceCaptureTests.cs' \
  --path 'tools/bizhawk-headless/tests/S3KTraceCaptureRunnerTests.cs' --path-rename 'tools/bizhawk-headless/tests/S3KTraceCaptureRunnerTests.cs:bizhawk-headless/tests/S3KTraceCaptureRunnerTests.cs' \
  --path 'tools/bizhawk-headless/tests/S3KTraceCsvWriterTests.cs' --path-rename 'tools/bizhawk-headless/tests/S3KTraceCsvWriterTests.cs:bizhawk-headless/tests/S3KTraceCsvWriterTests.cs' \
  --path 'tools/bizhawk-headless/tests/S3KTraceMetadataWriterTests.cs' --path-rename 'tools/bizhawk-headless/tests/S3KTraceMetadataWriterTests.cs:bizhawk-headless/tests/S3KTraceMetadataWriterTests.cs' \
  --path 'tools/bizhawk-headless/tests/SmokeCaptureRunnerTests.cs' --path-rename 'tools/bizhawk-headless/tests/SmokeCaptureRunnerTests.cs:bizhawk-headless/tests/SmokeCaptureRunnerTests.cs' \
  --path 'tools/bizhawk-headless/tests/test-timings.tsv' --path-rename 'tools/bizhawk-headless/tests/test-timings.tsv:bizhawk-headless/tests/test-timings.tsv' \
  --path 'tools/bizhawk-headless/tests/TestConsoleRouter.cs' --path-rename 'tools/bizhawk-headless/tests/TestConsoleRouter.cs:bizhawk-headless/tests/TestConsoleRouter.cs' \
  --path 'tools/bizhawk-headless/tests/TestMain.cs' --path-rename 'tools/bizhawk-headless/tests/TestMain.cs:bizhawk-headless/tests/TestMain.cs' \
  --path 'tools/bizhawk-headless/tests/TestOptions.cs' --path-rename 'tools/bizhawk-headless/tests/TestOptions.cs:bizhawk-headless/tests/TestOptions.cs' \
  --path 'tools/bizhawk-headless/tests/TestRunner.cs' --path-rename 'tools/bizhawk-headless/tests/TestRunner.cs:bizhawk-headless/tests/TestRunner.cs' \
  --path 'tools/bizhawk-headless/tests/TestScratch.cs' --path-rename 'tools/bizhawk-headless/tests/TestScratch.cs:bizhawk-headless/tests/TestScratch.cs' \
  --path 'tools/bizhawk-headless/tests/TestTimings.cs' --path-rename 'tools/bizhawk-headless/tests/TestTimings.cs:bizhawk-headless/tests/TestTimings.cs' \
  --path 'tools/bizhawk-headless/tests/TraceCliTests.cs' --path-rename 'tools/bizhawk-headless/tests/TraceCliTests.cs:bizhawk-headless/tests/TraceCliTests.cs' \
  --path 'tools/bizhawk-headless/tests/TracePayloadCompressorTests.cs' --path-rename 'tools/bizhawk-headless/tests/TracePayloadCompressorTests.cs:bizhawk-headless/tests/TracePayloadCompressorTests.cs' \
  --path 'tools/bizhawk-headless/trace_v5_capture_matrix.py' --path-rename 'tools/bizhawk-headless/trace_v5_capture_matrix.py:bizhawk-headless/trace_v5_capture_matrix.py' \
  --path 'tools/bizhawk-headless/verify-deterministic-build.sh' --path-rename 'tools/bizhawk-headless/verify-deterministic-build.sh:bizhawk-headless/verify-deterministic-build.sh' \
  --path 'tools/bizhawk/audio/s1_audio_parity_contract.lua' --path-rename 'tools/bizhawk/audio/s1_audio_parity_contract.lua:bizhawk/audio/s1_audio_parity_contract.lua' \
  --path 'tools/bizhawk/audio/s1_complete_run_audio_contract_test.lua' --path-rename 'tools/bizhawk/audio/s1_complete_run_audio_contract_test.lua:bizhawk/audio/s1_complete_run_audio_contract_test.lua' \
  --path 'tools/bizhawk/audio/s1_complete_run_audio_contract.lua' --path-rename 'tools/bizhawk/audio/s1_complete_run_audio_contract.lua:bizhawk/audio/s1_complete_run_audio_contract.lua' \
  --path 'tools/bizhawk/audio/s1_gameplay_audio_timeline_contract.lua' --path-rename 'tools/bizhawk/audio/s1_gameplay_audio_timeline_contract.lua:bizhawk/audio/s1_gameplay_audio_timeline_contract.lua' \
  --path 'tools/bizhawk/count_bk2_input_frames.ps1' --path-rename 'tools/bizhawk/count_bk2_input_frames.ps1:bizhawk/count_bk2_input_frames.ps1' \
  --path 'tools/bizhawk/create_bizhawk_diag_config.ps1' --path-rename 'tools/bizhawk/create_bizhawk_diag_config.ps1:bizhawk/create_bizhawk_diag_config.ps1' \
  --path 'tools/bizhawk/debug_s2_endact_inputs.lua' --path-rename 'tools/bizhawk/debug_s2_endact_inputs.lua:bizhawk/debug_s2_endact_inputs.lua' \
  --path 'tools/bizhawk/debug_s2_tails_despawn.lua' --path-rename 'tools/bizhawk/debug_s2_tails_despawn.lua:bizhawk/debug_s2_tails_despawn.lua' \
  --path 'tools/bizhawk/diag_aiz_collapse_onobj.lua' --path-rename 'tools/bizhawk/diag_aiz_collapse_onobj.lua:bizhawk/diag_aiz_collapse_onobj.lua' \
  --path 'tools/bizhawk/diag_aiz2_djf_probe.lua' --path-rename 'tools/bizhawk/diag_aiz2_djf_probe.lua:bizhawk/diag_aiz2_djf_probe.lua' \
  --path 'tools/bizhawk/diag_aiz2_monitor_solid_probe.lua' --path-rename 'tools/bizhawk/diag_aiz2_monitor_solid_probe.lua:bizhawk/diag_aiz2_monitor_solid_probe.lua' \
  --path 'tools/bizhawk/diag_s1_bounce.lua' --path-rename 'tools/bizhawk/diag_s1_bounce.lua:bizhawk/diag_s1_bounce.lua' \
  --path 'tools/bizhawk/diag_s1_plat_gate.lua' --path-rename 'tools/bizhawk/diag_s1_plat_gate.lua:bizhawk/diag_s1_plat_gate.lua' \
  --path 'tools/bizhawk/diag_s1_plat.lua' --path-rename 'tools/bizhawk/diag_s1_plat.lua:bizhawk/diag_s1_plat.lua' \
  --path 'tools/bizhawk/diag_s1_ypos_writes.lua' --path-rename 'tools/bizhawk/diag_s1_ypos_writes.lua:bizhawk/diag_s1_ypos_writes.lua' \
  --path 'tools/bizhawk/diag_s2_arz2_round68_obj28.lua' --path-rename 'tools/bizhawk/diag_s2_arz2_round68_obj28.lua:bizhawk/diag_s2_arz2_round68_obj28.lua' \
  --path 'tools/bizhawk/diag_s2_arz2_round69_obj28_slot1b.lua' --path-rename 'tools/bizhawk/diag_s2_arz2_round69_obj28_slot1b.lua:bizhawk/diag_s2_arz2_round69_obj28_slot1b.lua' \
  --path 'tools/bizhawk/diag_s2_dez_objc7_group.lua' --path-rename 'tools/bizhawk/diag_s2_dez_objc7_group.lua:bizhawk/diag_s2_dez_objc7_group.lua' \
  --path 'tools/bizhawk/diag_s2_mtz2_hurt_probe.lua' --path-rename 'tools/bizhawk/diag_s2_mtz2_hurt_probe.lua:bizhawk/diag_s2_mtz2_hurt_probe.lua' \
  --path 'tools/bizhawk/diag_s2_mtz3_ctrl_lock.lua' --path-rename 'tools/bizhawk/diag_s2_mtz3_ctrl_lock.lua:bizhawk/diag_s2_mtz3_ctrl_lock.lua' \
  --path 'tools/bizhawk/diag_s2_ooz_pform.lua' --path-rename 'tools/bizhawk/diag_s2_ooz_pform.lua:bizhawk/diag_s2_ooz_pform.lua' \
  --path 'tools/bizhawk/diag_s2_ooz_tails_push.lua' --path-rename 'tools/bizhawk/diag_s2_ooz_tails_push.lua:bizhawk/diag_s2_ooz_tails_push.lua' \
  --path 'tools/bizhawk/diag_tails_push_source.lua' --path-rename 'tools/bizhawk/diag_tails_push_source.lua:bizhawk/diag_tails_push_source.lua' \
  --path 'tools/bizhawk/diag_tails_wallprobe.lua' --path-rename 'tools/bizhawk/diag_tails_wallprobe.lua:bizhawk/diag_tails_wallprobe.lua' \
  --path 'tools/bizhawk/diag_template_fast.lua' --path-rename 'tools/bizhawk/diag_template_fast.lua:bizhawk/diag_template_fast.lua' \
  --path 'tools/bizhawk/diagnostics/plc_timing_probe_contract_test.lua' --path-rename 'tools/bizhawk/diagnostics/plc_timing_probe_contract_test.lua:bizhawk/diagnostics/plc_timing_probe_contract_test.lua' \
  --path 'tools/bizhawk/diagnostics/s1_plc_timing_probe.env.sh' --path-rename 'tools/bizhawk/diagnostics/s1_plc_timing_probe.env.sh:bizhawk/diagnostics/s1_plc_timing_probe.env.sh' \
  --path 'tools/bizhawk/diagnostics/s1_plc_timing_probe.lua' --path-rename 'tools/bizhawk/diagnostics/s1_plc_timing_probe.lua:bizhawk/diagnostics/s1_plc_timing_probe.lua' \
  --path 'tools/bizhawk/diagnostics/s2_plc_timing_probe.env.sh' --path-rename 'tools/bizhawk/diagnostics/s2_plc_timing_probe.env.sh:bizhawk/diagnostics/s2_plc_timing_probe.env.sh' \
  --path 'tools/bizhawk/diagnostics/s2_plc_timing_probe.lua' --path-rename 'tools/bizhawk/diagnostics/s2_plc_timing_probe.lua:bizhawk/diagnostics/s2_plc_timing_probe.lua' \
  --path 'tools/bizhawk/fetch_bizhawk_2_11_linux.sh' --path-rename 'tools/bizhawk/fetch_bizhawk_2_11_linux.sh:bizhawk/fetch_bizhawk_2_11_linux.sh' \
  --path 'tools/bizhawk/hook_solid_classify.lua' --path-rename 'tools/bizhawk/hook_solid_classify.lua:bizhawk/hook_solid_classify.lua' \
  --path 'tools/bizhawk/hook_speedtopos.lua' --path-rename 'tools/bizhawk/hook_speedtopos.lua:bizhawk/hook_speedtopos.lua' \
  --path 'tools/bizhawk/lib/oggf_hardware_timing.lua' --path-rename 'tools/bizhawk/lib/oggf_hardware_timing.lua:bizhawk/lib/oggf_hardware_timing.lua' \
  --path 'tools/bizhawk/lib/oggf_trace_common.lua' --path-rename 'tools/bizhawk/lib/oggf_trace_common.lua:bizhawk/lib/oggf_trace_common.lua' \
  --path 'tools/bizhawk/normalize_s2_traces_input.ps1' --path-rename 'tools/bizhawk/normalize_s2_traces_input.ps1:bizhawk/normalize_s2_traces_input.ps1' \
  --path 'tools/bizhawk/prepare_bizhawk_fast_lua.ps1' --path-rename 'tools/bizhawk/prepare_bizhawk_fast_lua.ps1:bizhawk/prepare_bizhawk_fast_lua.ps1' \
  --path 'tools/bizhawk/probes/aiz_plane_intro_scroll_probe.lua' --path-rename 'tools/bizhawk/probes/aiz_plane_intro_scroll_probe.lua:bizhawk/probes/aiz_plane_intro_scroll_probe.lua' \
  --path 'tools/bizhawk/probes/aiz_tails_anim_2707_probe.lua' --path-rename 'tools/bizhawk/probes/aiz_tails_anim_2707_probe.lua:bizhawk/probes/aiz_tails_anim_2707_probe.lua' \
  --path 'tools/bizhawk/probes/aiz_tails_hurt_anim_10744_probe.lua' --path-rename 'tools/bizhawk/probes/aiz_tails_hurt_anim_10744_probe.lua:bizhawk/probes/aiz_tails_hurt_anim_10744_probe.lua' \
  --path 'tools/bizhawk/probes/aiz2_entry_anchor_frame_probe.lua' --path-rename 'tools/bizhawk/probes/aiz2_entry_anchor_frame_probe.lua:bizhawk/probes/aiz2_entry_anchor_frame_probe.lua' \
  --path 'tools/bizhawk/probes/aiz2_entry_anim_writer_probe.lua' --path-rename 'tools/bizhawk/probes/aiz2_entry_anim_writer_probe.lua:bizhawk/probes/aiz2_entry_anim_writer_probe.lua' \
  --path 'tools/bizhawk/probes/aiz2_entry_sst_arrival_probe.lua' --path-rename 'tools/bizhawk/probes/aiz2_entry_sst_arrival_probe.lua:bizhawk/probes/aiz2_entry_sst_arrival_probe.lua' \
  --path 'tools/bizhawk/probes/example_stage_probe.lua' --path-rename 'tools/bizhawk/probes/example_stage_probe.lua:bizhawk/probes/example_stage_probe.lua' \
  --path 'tools/bizhawk/probes/examples/nested_stage_probe.lua' --path-rename 'tools/bizhawk/probes/examples/nested_stage_probe.lua:bizhawk/probes/examples/nested_stage_probe.lua' \
  --path 'tools/bizhawk/probes/hcz_allocation_epoch_probe.lua' --path-rename 'tools/bizhawk/probes/hcz_allocation_epoch_probe.lua:bizhawk/probes/hcz_allocation_epoch_probe.lua' \
  --path 'tools/bizhawk/probes/icz_f16361_yvel_probe.lua' --path-rename 'tools/bizhawk/probes/icz_f16361_yvel_probe.lua:bizhawk/probes/icz_f16361_yvel_probe.lua' \
  --path 'tools/bizhawk/probes/icz_rng_ownership_probe.lua' --path-rename 'tools/bizhawk/probes/icz_rng_ownership_probe.lua:bizhawk/probes/icz_rng_ownership_probe.lua' \
  --path 'tools/bizhawk/probes/icz_slot20_allocation_probe.lua' --path-rename 'tools/bizhawk/probes/icz_slot20_allocation_probe.lua:bizhawk/probes/icz_slot20_allocation_probe.lua' \
  --path 'tools/bizhawk/probes/mhz_f3246_findfloor_probe.lua' --path-rename 'tools/bizhawk/probes/mhz_f3246_findfloor_probe.lua:bizhawk/probes/mhz_f3246_findfloor_probe.lua' \
  --path 'tools/bizhawk/probes/mhz_f3246_madmole_release_probe.lua' --path-rename 'tools/bizhawk/probes/mhz_f3246_madmole_release_probe.lua:bizhawk/probes/mhz_f3246_madmole_release_probe.lua' \
  --path 'tools/bizhawk/probes/mhz_f3246_status_write_probe.lua' --path-rename 'tools/bizhawk/probes/mhz_f3246_status_write_probe.lua:bizhawk/probes/mhz_f3246_status_write_probe.lua' \
  --path 'tools/bizhawk/probes/mhz_rng_ownership_probe.lua' --path-rename 'tools/bizhawk/probes/mhz_rng_ownership_probe.lua:bizhawk/probes/mhz_rng_ownership_probe.lua' \
  --path 'tools/bizhawk/probes/probe_runtime.lua' --path-rename 'tools/bizhawk/probes/probe_runtime.lua:bizhawk/probes/probe_runtime.lua' \
  --path 'tools/bizhawk/probes/README.md' --path-rename 'tools/bizhawk/probes/README.md:bizhawk/probes/README.md' \
  --path 'tools/bizhawk/probes/s1_audio_driver_parity_probe.lua' --path-rename 'tools/bizhawk/probes/s1_audio_driver_parity_probe.lua:bizhawk/probes/s1_audio_driver_parity_probe.lua' \
  --path 'tools/bizhawk/probes/s1_complete_run_audio_probe.lua' --path-rename 'tools/bizhawk/probes/s1_complete_run_audio_probe.lua:bizhawk/probes/s1_complete_run_audio_probe.lua' \
  --path 'tools/bizhawk/probes/s1_ghz1_gameplay_audio_timeline_probe.lua' --path-rename 'tools/bizhawk/probes/s1_ghz1_gameplay_audio_timeline_probe.lua:bizhawk/probes/s1_ghz1_gameplay_audio_timeline_probe.lua' \
  --path 'tools/bizhawk/probes/s1_lz3_animal_departure_probe.lua' --path-rename 'tools/bizhawk/probes/s1_lz3_animal_departure_probe.lua:bizhawk/probes/s1_lz3_animal_departure_probe.lua' \
  --path 'tools/bizhawk/probes/s1_lz3_capsule_rng_probe.lua' --path-rename 'tools/bizhawk/probes/s1_lz3_capsule_rng_probe.lua:bizhawk/probes/s1_lz3_capsule_rng_probe.lua' \
  --path 'tools/bizhawk/probes/s1_lz3_capsule_slots_probe.lua' --path-rename 'tools/bizhawk/probes/s1_lz3_capsule_slots_probe.lua:bizhawk/probes/s1_lz3_capsule_slots_probe.lua' \
  --path 'tools/bizhawk/probes/s1_lz3_explosion_lifetime_probe.lua' --path-rename 'tools/bizhawk/probes/s1_lz3_explosion_lifetime_probe.lua:bizhawk/probes/s1_lz3_explosion_lifetime_probe.lua' \
  --path 'tools/bizhawk/probes/s2_cpz2_bossid_probe.lua' --path-rename 'tools/bizhawk/probes/s2_cpz2_bossid_probe.lua:bizhawk/probes/s2_cpz2_bossid_probe.lua' \
  --path 'tools/bizhawk/probes/s2_cpz2_chunkid_probe.lua' --path-rename 'tools/bizhawk/probes/s2_cpz2_chunkid_probe.lua:bizhawk/probes/s2_cpz2_chunkid_probe.lua' \
  --path 'tools/bizhawk/probes/s2_cpz2_d7_clobber_probe.lua' --path-rename 'tools/bizhawk/probes/s2_cpz2_d7_clobber_probe.lua:bizhawk/probes/s2_cpz2_d7_clobber_probe.lua' \
  --path 'tools/bizhawk/probes/s2_cpz2_obj05_exec_probe.lua' --path-rename 'tools/bizhawk/probes/s2_cpz2_obj05_exec_probe.lua:bizhawk/probes/s2_cpz2_obj05_exec_probe.lua' \
  --path 'tools/bizhawk/probes/s2_cpz2_plane_probe.lua' --path-rename 'tools/bizhawk/probes/s2_cpz2_plane_probe.lua:bizhawk/probes/s2_cpz2_plane_probe.lua' \
  --path 'tools/bizhawk/probes/s2_cpz2_push_order_probe.lua' --path-rename 'tools/bizhawk/probes/s2_cpz2_push_order_probe.lua:bizhawk/probes/s2_cpz2_push_order_probe.lua' \
  --path 'tools/bizhawk/probes/s2_cpz2_push_owner_probe.lua' --path-rename 'tools/bizhawk/probes/s2_cpz2_push_owner_probe.lua:bizhawk/probes/s2_cpz2_push_owner_probe.lua' \
  --path 'tools/bizhawk/probes/s2_cpz2_runobject_probe.lua' --path-rename 'tools/bizhawk/probes/s2_cpz2_runobject_probe.lua:bizhawk/probes/s2_cpz2_runobject_probe.lua' \
  --path 'tools/bizhawk/probes/s2_cpz2_xvel_writer_probe.lua' --path-rename 'tools/bizhawk/probes/s2_cpz2_xvel_writer_probe.lua:bizhawk/probes/s2_cpz2_xvel_writer_probe.lua' \
  --path 'tools/bizhawk/README.md' --path-rename 'tools/bizhawk/README.md:bizhawk/README.md' \
  --path 'tools/bizhawk/record_s2_level_select_traces.ps1' --path-rename 'tools/bizhawk/record_s2_level_select_traces.ps1:bizhawk/record_s2_level_select_traces.ps1' \
  --path 'tools/bizhawk/record_s2_trace.bat' --path-rename 'tools/bizhawk/record_s2_trace.bat:bizhawk/record_s2_trace.bat' \
  --path 'tools/bizhawk/record_s3k_trace.bat' --path-rename 'tools/bizhawk/record_s3k_trace.bat:bizhawk/record_s3k_trace.bat' \
  --path 'tools/bizhawk/record_trace.bat' --path-rename 'tools/bizhawk/record_trace.bat:bizhawk/record_trace.bat' \
  --path 'tools/bizhawk/record_trace.sh' --path-rename 'tools/bizhawk/record_trace.sh:bizhawk/record_trace.sh' \
  --path 'tools/bizhawk/run_bizhawk_hidden.ps1' --path-rename 'tools/bizhawk/run_bizhawk_hidden.ps1:bizhawk/run_bizhawk_hidden.ps1' \
  --path 'tools/bizhawk/run_bizhawk_lua.bat' --path-rename 'tools/bizhawk/run_bizhawk_lua.bat:bizhawk/run_bizhawk_lua.bat' \
  --path 'tools/bizhawk/run_bizhawk_lua.sh' --path-rename 'tools/bizhawk/run_bizhawk_lua.sh:bizhawk/run_bizhawk_lua.sh' \
  --path 'tools/bizhawk/s1_complete_run_recorder.lua' --path-rename 'tools/bizhawk/s1_complete_run_recorder.lua:bizhawk/s1_complete_run_recorder.lua' \
  --path 'tools/bizhawk/s1_level_map.lua' --path-rename 'tools/bizhawk/s1_level_map.lua:bizhawk/s1_level_map.lua' \
  --path 'tools/bizhawk/s1_mz3_slot_probe.lua' --path-rename 'tools/bizhawk/s1_mz3_slot_probe.lua:bizhawk/s1_mz3_slot_probe.lua' \
  --path 'tools/bizhawk/s1_sbz2_slot_probe.lua' --path-rename 'tools/bizhawk/s1_sbz2_slot_probe.lua:bizhawk/s1_sbz2_slot_probe.lua' \
  --path 'tools/bizhawk/s1_syz1_floorup_probe.lua' --path-rename 'tools/bizhawk/s1_syz1_floorup_probe.lua:bizhawk/s1_syz1_floorup_probe.lua' \
  --path 'tools/bizhawk/s1_syz1_slot_probe.lua' --path-rename 'tools/bizhawk/s1_syz1_slot_probe.lua:bizhawk/s1_syz1_slot_probe.lua' \
  --path 'tools/bizhawk/s1_syz3_slot_probe.lua' --path-rename 'tools/bizhawk/s1_syz3_slot_probe.lua:bizhawk/s1_syz3_slot_probe.lua' \
  --path 'tools/bizhawk/s1_trace_recorder.lua' --path-rename 'tools/bizhawk/s1_trace_recorder.lua:bizhawk/s1_trace_recorder.lua' \
  --path 'tools/bizhawk/s1-complete-run-level-map.txt' --path-rename 'tools/bizhawk/s1-complete-run-level-map.txt:bizhawk/s1-complete-run-level-map.txt' \
  --path 'tools/bizhawk/s2_ss_trace_recorder.lua' --path-rename 'tools/bizhawk/s2_ss_trace_recorder.lua:bizhawk/s2_ss_trace_recorder.lua' \
  --path 'tools/bizhawk/s2_trace_recorder.lua' --path-rename 'tools/bizhawk/s2_trace_recorder.lua:bizhawk/s2_trace_recorder.lua' \
  --path 'tools/bizhawk/s3k_complete_run_recorder.lua' --path-rename 'tools/bizhawk/s3k_complete_run_recorder.lua:bizhawk/s3k_complete_run_recorder.lua' \
  --path 'tools/bizhawk/s3k_domain_probe.lua' --path-rename 'tools/bizhawk/s3k_domain_probe.lua:bizhawk/s3k_domain_probe.lua' \
  --path 'tools/bizhawk/s3k_handoff_diag.lua' --path-rename 'tools/bizhawk/s3k_handoff_diag.lua:bizhawk/s3k_handoff_diag.lua' \
  --path 'tools/bizhawk/s3k_initial_process_sprites_probe.lua' --path-rename 'tools/bizhawk/s3k_initial_process_sprites_probe.lua:bizhawk/s3k_initial_process_sprites_probe.lua' \
  --path 'tools/bizhawk/s3k_player_base_compare.lua' --path-rename 'tools/bizhawk/s3k_player_base_compare.lua:bizhawk/s3k_player_base_compare.lua' \
  --path 'tools/bizhawk/s3k_player_search.lua' --path-rename 'tools/bizhawk/s3k_player_search.lua:bizhawk/s3k_player_search.lua' \
  --path 'tools/bizhawk/s3k_slot_scan.lua' --path-rename 'tools/bizhawk/s3k_slot_scan.lua:bizhawk/s3k_slot_scan.lua' \
  --path 'tools/bizhawk/s3k_trace_recorder.lua' --path-rename 'tools/bizhawk/s3k_trace_recorder.lua:bizhawk/s3k_trace_recorder.lua' \
  --path 'tools/bizhawk/SHARED_MODULE_HANDOFF.md' --path-rename 'tools/bizhawk/SHARED_MODULE_HANDOFF.md:bizhawk/SHARED_MODULE_HANDOFF.md' \
  --path 'tools/bizhawk/trace_y_instructions.lua' --path-rename 'tools/bizhawk/trace_y_instructions.lua:bizhawk/trace_y_instructions.lua' \
  --path 'tools/bizhawk/trace_y_poll.lua' --path-rename 'tools/bizhawk/trace_y_poll.lua:bizhawk/trace_y_poll.lua' \
  --path 'tools/bizhawk/watch_y_write.lua' --path-rename 'tools/bizhawk/watch_y_write.lua:bizhawk/watch_y_write.lua' \
  --path 'tools/retro/debug_credits.py' --path-rename 'tools/retro/debug_credits.py:retro/debug_credits.py' \
  --path 'tools/retro/requirements.txt' --path-rename 'tools/retro/requirements.txt:retro/requirements.txt' \
  --path 'tools/retro/s1_credits_trace_recorder.py' --path-rename 'tools/retro/s1_credits_trace_recorder.py:retro/s1_credits_trace_recorder.py' \
  --path 'tools/retro/s1_trace_recorder.py' --path-rename 'tools/retro/s1_trace_recorder.py:retro/s1_trace_recorder.py' \
  --path 'tools/retro/trace_core.py' --path-rename 'tools/retro/trace_core.py:retro/trace_core.py' \
  --path 'tools/testing/test_compare_trace_v5_candidates.py' --path-rename 'tools/testing/test_compare_trace_v5_candidates.py:testing/test_compare_trace_v5_candidates.py' \
  --path 'tools/testing/test_trace_v5_capture_matrix.py' --path-rename 'tools/testing/test_trace_v5_capture_matrix.py:testing/test_trace_v5_capture_matrix.py' \
  --path 'tools/testing/test_validate_trace_v5.py' --path-rename 'tools/testing/test_validate_trace_v5.py:testing/test_validate_trace_v5.py' \
  --path 'tools/traces/build_s1_credits_raw_host_evidence.py' --path-rename 'tools/traces/build_s1_credits_raw_host_evidence.py:traces/build_s1_credits_raw_host_evidence.py' \
  --path 'tools/traces/compare_trace_v5_candidates.py' --path-rename 'tools/traces/compare_trace_v5_candidates.py:traces/compare_trace_v5_candidates.py' \
  --path 'tools/traces/compress-traces.ps1' --path-rename 'tools/traces/compress-traces.ps1:traces/compress-traces.ps1' \
  --path 'tools/traces/no_replace_output.py' --path-rename 'tools/traces/no_replace_output.py:traces/no_replace_output.py' \
  --path 'tools/traces/s1_credits_raw_evidence.py' --path-rename 'tools/traces/s1_credits_raw_evidence.py:traces/s1_credits_raw_evidence.py' \
  --path 'tools/traces/trace_fixture_inventory.py' --path-rename 'tools/traces/trace_fixture_inventory.py:traces/trace_fixture_inventory.py' \
  --path 'tools/traces/trace_v5_capture_matrix.py' --path-rename 'tools/traces/trace_v5_capture_matrix.py:traces/trace_v5_capture_matrix.py' \
  --path 'tools/traces/validate_trace_v5.py' --path-rename 'tools/traces/validate_trace_v5.py:traces/validate_trace_v5.py' \
  --path 'tools/traces/verify_s1_credits_raw_host_evidence.py' --path-rename 'tools/traces/verify_s1_credits_raw_host_evidence.py:traces/verify_s1_credits_raw_host_evidence.py' \
  --replace-text "$TRACECHASER_WORK_ROOT/private/history-replace-text.txt"
```

## Resulting history

The source extraction-base commit changes only excluded OpenGGF evidence and
therefore maps to the all-zero object in `.git/filter-repo/commit-map`; this is
the expected empty-commit pruning result. The latest retained source commit
`a8bbcbbebcfe7bad39294066e38a39eba6983940` maps to the filtered import tip
`4e4a1cfcf9289abb171c64d6388ebfc9d7814bdc`.

The single filtered root is
`3fc3f6d5ffa5448b9a0f9acbcf644d2084a01080`, originating from OpenGGF
`fd19f881222dec4bdc05478cb441677ae46dedb8`; it preserves Farrell Hayman's
2025-12-03 GPLv3 commit and authorship. The first native-recorder commit is
`2537859963bac6eff44917f2b4735d8e3350e0e1`, mapped from OpenGGF
`5bda21b64322eb4e85d9e2b6c940165a7d0ed09c`; it preserves Farrell's
2026-07-23 author date and `feat(trace): bootstrap headless GPGX host` message.

Representative first/last retained history was verified for:

| Path | First retained commit | Latest imported commit |
| --- | --- | --- |
| `bizhawk-headless/src/Program.cs` | `f3d8325be5ddc392caa33574b3238a0f089a47d4` | `29f835a9b9432aafe8727d95345c6d78d8ccbb92` |
| `bizhawk/s1_trace_recorder.lua` | `047a06a06a1097d5650a864d09af9656c1935b90` | `3734e3adaa3bb540f313bee9a639d56e39df1d6f` |
| `traces/validate_trace_v5.py` | `017a354204f929c9952f1fd6584441c015b45455` | `4e4a1cfcf9289abb171c64d6388ebfc9d7814bdc` |
| `bizhawk-headless/run.sh` | `f3d8325be5ddc392caa33574b3238a0f089a47d4` | `abff180fc1a703beced36d6ab294fcc00754589a` |
| `bizhawk-headless/docs/s1-trace-recorder-behavior.md` | `665e1e02b8c55137c8429c31411a5f26f6ee5f32` | `007e542f377e836eb674d274bbfb7c0b4db83c55` |
| `contracts/audio/normalization-contract-v1.json` | `bc6eba5b3b532af210d3b98ec3536e7c53caaccc` | `f925e8e602df55e03040f663c0efbf0e6230f3c0` |

`git blame` on the moved S1 recorder behavior document retained Farrell's
original authorship and the original documentation and v5-hardening summaries.

## Reachable-object and license audit

Before provenance additions, `main` contained 591 commits and 4,105 reachable
objects: 591 commits, 1,761 trees, and 1,753 blobs. All 352 historical paths
were enumerated. The largest blob was 289,181 bytes, below the scanner's
1,048,576-byte ceiling. The checked-in scanner streams blob bytes without text
decoding, rejects forbidden suffixes/components/content, known archive/BK2,
ROM, and executable magic, and reports commit/object/path for every violation.
Its synthetic history tests include deleted prohibited data so a clean current
tree cannot mask a bad historical blob.

The only allowed license or notice paths are `LICENSE` and
`bizhawk-headless/native/gpgx-audio-observer/notices/zstd-LICENSE`. Their
SHA-256 values are respectively
`3972dc9744f6499f0f9b2dbf76696f2ae7ad8af9b23dde66d6af86c9dfb36986`
and
`7055266497633c9025b777c78eb7235af13922117480ed5c674677adc381c9d8`.
The GPLv3 file originates at filtered root `3fc3f6d5...`; the Zstandard BSD
notice originates at `82a971bcbe1e62b859a5cc095655e1b1cc80ed35` in the buffered GPGX audio
observer work. No other license-like history path is retained.

The focused scanner tests and the full all-history scan passed. Both excluded
diagnostic paths have zero reachable history. `git fsck --full` completed with
no output. After filtering and before any commit, the repository had one local
branch (`main`), no remote, and no tags.
