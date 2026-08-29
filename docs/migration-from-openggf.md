# Migration from OpenGGF

TraceChaser was extracted from OpenGGF without changing the live trace contract:
published traces remain `trace_schema: 5`, with native recorder provenance and
the existing comparison, timing, inventory, and no-replacement rules.

## Portable paths and inputs

TraceChaser commands resolve their own checkout from the entry point's location.
They do not search a current directory, Git superproject, sibling checkout, or
machine-specific OpenGGF path. The former OpenGGF paths map as follows:

### Pre-v5 historical evidence: former path map

| Former OpenGGF path | TraceChaser path |
|---|---|
| `tools/bizhawk-headless/` | `bizhawk-headless/` |
| `tools/bizhawk/` | `bizhawk/` |
| `tools/traces/` | `traces/` |

### Current standalone paths

Python imports use the `traces` package. The canonical capture-matrix module is
`traces/trace_v5_capture_matrix.py`; `bizhawk-headless/trace_v5_capture_matrix.py`
is a thin command-line entry point retained for users of the old component
location.

Capture-matrix operations require the matrix file and explicit consumer inputs:
the OpenGGF checkout, fixture root and inventory, movie root, all three verified
ROM paths, and external batch/candidate roots as applicable. No ROM filename or
environment-variable discovery is performed. Scratch and candidate roots are
rejected when they are within either source tree. The default native BizHawk
installation is checkout-local
`.dependencies/BizHawk-2.11-linux-x64`; it contains dependencies, never capture
output.

All paths may contain spaces. Matrix expansion returns literal argument vectors
and performs shell quoting only when writing a human-readable command ledger.

## Producer and consumer test ownership

The extracted producer tests remain behavioral and use synthetic explicit
inputs. Assertions that require OpenGGF-owned files were not weakened or made
optional; they return to the OpenGGF consumer suite in the integration task.

| Removed TraceChaser assumption | OpenGGF consumer disposition |
|---|---|
| `.agents`/`.claude` trace-skill mirrors and their capability text | Keep as OpenGGF agent-document policy tests. |
| Java replay Javadoc and `TestTraceFixtureRootOverride` implementation text | Keep beside the corresponding OpenGGF Java tests. |
| OpenGGF BizHawk README and recorder-behavior prose assertions | Replace prose-change detectors with consumer conformance tests where behavior is executable; retain documentation checks in OpenGGF only where policy requires them. |
| Read-only inventory generation against OpenGGF's canonical trace fixtures | Run against the explicit OpenGGF fixture root from the consumer suite. |
| Git-index inventory discovery from OpenGGF root and fixture children | Keep as an OpenGGF worktree integration test. |
| Exact six-movie extraction identities, frozen OpenGGF commits/diffs, extraction argv ledger, and committed fixture publication mappings | Keep in the OpenGGF extraction/integration evidence suite using explicit paths into both checkouts. |
| Count and placement of OpenGGF BK2 fixtures after assembly | Assert in OpenGGF's consumer publication test; TraceChaser asserts only that the explicitly supplied static inputs are copied byte-for-byte. |
| OpenGGF provenance commits `081167cb…41828f10` are present in the producer repository | Preserve those IDs unchanged as opaque provenance in the reviewed matrix. OpenGGF asserts its cutover mapping; TraceChaser preflights mapped build commits `88ec9f1c…39848394` from the filter-repo proof. |
| The legacy full publication matrix has exactly 36 producer rows | Retained as a producer-owned behavioral contract over `ROWS`; the six-row extraction matrix remains a separate, exact-order cutover contract. |
| The extraction preflight runs every reviewed named native filter | Retained in the explicit `tracechaser_build.native_test_filters` boundary and asserted before the clean build runner receives the filter tuple. |
| The deterministic-build smoke guard targets only `TracePayloadCompressor` | Retained distinctly in `verify-deterministic-build.sh`; producer tests pin that exact filter and exclude `Bk2Reader` and `S2AudioObserverProfile`. |
| Lua recorders could silently default to caller-relative output | Replaced by direct producer enforcement: all six recorders derive the installed sibling common module, which invokes the TraceChaser canonical path-policy helper, requires explicit producer/consumer/interpreter inputs, and rejects protected aliases even under direct invocation. Launcher preflight is supplementary, not authority. |
| Stable-Retro recorders defaulted to caller-relative output | Replaced by required `--input-repository-root` and `--output-dir` arguments and the same canonical external-output policy. |
| Windows/Bash recorder wrappers only normalized output text | Replaced by behavioral canonical-path rejection, including existing symlink/junction aliases, before emulator startup. |
| Capture command-ledger output was only no-replace | Retains no-replace publication and additionally rejects output beneath either source checkout. |

TraceChaser continues to test exact matrix format and row ordering, complete
explicit ROM/movie identity checks, typed argv expansion, no-replacement
assembly, source-tree write rejection, trace-v5 validation, comparison, and raw
host-evidence behavior.

The OpenGGF consumer cutover must assert the exact filter-repo relationship
`41828f10998f531e614d855c858ba1b26429d757 ->
398483941681d4b6a29d68494c5664a0e58a59a7` and the mapped parent boundary
`88ec9f1c61992a04f72763d94a231e7ffe0ff801..398483941681d4b6a29d68494c5664a0e58a59a7`.
TraceChaser deliberately does not attempt to discover or open an OpenGGF
checkout while verifying that standalone build boundary.

## Producer-contract replacement ledger

Task 5 classified all 48 `@Test` methods in the eleven Java contract sources at
OpenGGF extraction commit `88530afdf331fb152f88a4d14adb8f93f2299ff6`.
Producer replacements below run only against TraceChaser-owned source or
explicit synthetic inputs. Consumer dispositions remain OpenGGF assertions for
Task 10; they are not weakened into optional TraceChaser tests. Executable Lua
contracts resolve the overridable `LUA_BIN` through `PATH`, support exactly Lua
5.4, report the detected patch version, and skip only when the requested
interpreter is absent. Structural contracts never depend on that executable.

| OpenGGF Java source and method | TraceChaser replacement or OpenGGF consumer disposition |
|---|---|
| `TestBizhawkProbeContractGuard.sharedRuntimeOwnsProbeLifecycle` | `ProbeRuntimeContractTests.test_shared_runtime_owns_probe_lifecycle` |
| `TestBizhawkProbeContractGuard.sharedRuntimeCleansUpAndPreservesOriginalFailures` | `ProbeRuntimeContractTests.test_shared_runtime_cleans_up_and_preserves_original_failures` |
| `TestBizhawkProbeContractGuard.everyNamespacedProbeUsesDeclarativeRuntimeContract` | `ProbeEnumerationContractTests.test_every_namespaced_probe_uses_declarative_runtime_contract`; recursive `**/*.lua` discovery is independently pinned by `test_nested_probe_violation_is_enumerated`. |
| `TestBizhawkProbeContractGuard.luaLongStringsAndCommentsCannotSpoofTheContract` | `ProbeEnumerationContractTests.test_long_strings_and_comments_cannot_spoof_contract` |
| `TestTraceAnimationRecorderContract.allGameplayRecordersEmitSymmetricAnimationColumns` | `AnimationRecorderContractTests.test_all_gameplay_recorders_emit_symmetric_animation_columns` |
| `TestTraceAnimationRecorderContract.recordersReadNativeAnimationAndDisplayedMappingBytes` | `AnimationRecorderContractTests.test_recorders_read_native_animation_and_displayed_mapping_bytes` |
| `TestTraceAnimationRecorderContract.s3kRecordersSupportPhysicsAnimationOnlyRegeneration` | `AnimationRecorderContractTests.test_s3k_recorders_support_physics_animation_only_regeneration` |
| `TestTraceAnimationRecorderContract.s3kRecorderMetadataOmitsRetiredReplayPhaseControls` | `AnimationRecorderContractTests.test_s3k_recorder_metadata_omits_retired_replay_phase_controls` |
| `TestTraceAnimationRecorderContract.s3kRecorderUsesCanonicalBk2OffsetForEveryProfile` | `AnimationRecorderContractTests.test_s3k_recorder_uses_canonical_bk2_offset_for_every_profile` |
| `TestTraceAnimationRecorderContract.s3kRecorderCanonicalInputGuardRejectsAnAlternateAdjustedCall` | `S3kInputWrapperMutationTests.test_alternate_adjusted_call_is_rejected` |
| `TestTraceAnimationRecorderContract.s1CompleteRunRecorderDisambiguatesEveryRepeatedSegmentDirectory` | `AnimationRecorderContractTests.test_s1_complete_run_disambiguates_repeated_segment_directories` |
| `TestTraceAnimationRecorderContract.s1CompleteRunRecorderCanCaptureFocusedFinalZoneRngCalls` | `AnimationRecorderContractTests.test_s1_complete_run_can_capture_focused_final_zone_rng_calls` |
| `TestTraceAnimationRecorderContract.fastBizHawkWrapperDelegatesOneShotInitializationToRecorder` | `FastWrapperContractTests.test_fast_wrapper_delegates_one_shot_initialization_to_recorder` |
| `TestTraceAnimationRecorderContract.windowsValidatorAcceptsNestedProbeAndIgnoresLongBracketDecoys` | `FastWrapperContractTests.test_windows_validator_accepts_nested_probe_and_ignores_long_bracket_decoys` |
| `TestTraceAnimationRecorderContract.bizHawkLinuxToolingPinsRecorderCompatible211` | `BizHawkLuaToolingContractTests.test_linux_tooling_pins_recorder_compatible_211` |
| `TestTraceAnimationRecorderContract.allCommittedGameplayFixturesCarryV5AnimationCsv` | **OpenGGF consumer:** retain canonical-fixture counts, schema parsing, gzip/plain header parsing, 42/43-column compatibility, and animation/life-column assertions during Task 10. |
| `TestTraceRecorderCounterAddresses.sonic1RecorderUsesDisassemblyBackedExecutionCounters` | `RecorderCounterAddressContractTests.test_sonic1_uses_disassembly_backed_execution_counters` |
| `TestTraceRecorderCounterAddresses.sonic2RecorderUsesDisassemblyBackedExecutionCounters` | `RecorderCounterAddressContractTests.test_sonic2_uses_disassembly_backed_execution_counters` |
| `TestTraceRecorderCounterAddresses.sonic3kRecorderUsesDisassemblyBackedExecutionCounters` | `RecorderCounterAddressContractTests.test_sonic3k_uses_disassembly_backed_execution_counters` |
| `TestTraceRecorderCounterAddresses.sonic3kCompleteRunRecorderUsesTheSameExecutionCounters` | `RecorderCounterAddressContractTests.test_sonic3k_complete_run_uses_the_same_execution_counters` |
| `S2SpecialStageRecorderContractTest.recorderDeclaresBoundedRev01RecurringPassAndControlHooks` | `S2SpecialStageRecorderContractTests.test_recorder_declares_bounded_rev01_recurring_pass_and_control_hooks` |
| `S2SpecialStageRecorderContractTest.workflowValidatesRequiredSpecialStageAuxFamilies` | `S2SpecialStageRecorderContractTests.test_workflow_validates_required_special_stage_aux_families` |
| `S2SpecialStageRecorderContractTest.workflowBuildsGitignoredScratchPathFromExistingParent` | `S2SpecialStageRecorderContractTests.test_workflow_builds_scratch_below_explicit_external_output`; preserves scratch ownership while applying Task 4's explicit external-root contract. |
| `S2SpecialStageRecorderContractTest.passReplayDerivesInputFromBk2IdentityAndIgnoresAuxHeldDiagnostics` | **OpenGGF consumer:** retain BK2 identity-to-logical-input mapping against `S2SpecialStageReplayHarness`. |
| `S2SpecialStageRecorderContractTest.committedArtifactHasControlTransitionsAndLogicalPassEndCoverage` | **OpenGGF consumer:** retain canonical special-stage fixture parsing, pass binding, event-family, finish, and results-tail assertions. |
| `S2SpecialStageRecorderContractTest.f915BindsOnlyTheCompletedX58PassAndLagRowF916AddsNoPass` | **OpenGGF consumer:** retain the canonical rows 915/916 pass-binding assertion. |
| `TestPlcTimingEvidenceTool.committedVariedHistoryEvidenceIsApprovedAndAnalyzerClean` | **OpenGGF consumer:** retain with the Java PLC evidence tool and canonical evidence vector. |
| `TestPlcTimingEvidenceTool.evidenceBudgetsSerializeInStableNumericOrder` | **OpenGGF consumer:** retain with the Java PLC evidence serializer. |
| `TestPlcTimingEvidenceTool.cliDerivesACompactVectorFromExecuteHookRecords` | **OpenGGF consumer:** retain with the Java PLC evidence CLI. |
| `TestPlcTimingEvidenceTool.cliRejectsOrphanAndDuplicateHblankStates` | **OpenGGF consumer:** retain with the Java PLC evidence CLI validator. |
| `TestPlcTimingEvidenceTool.cliRejectsHblankAfterLagOrNonDeferCapableVint` | **OpenGGF consumer:** retain with the Java PLC evidence CLI validator. |
| `TestPlcTimingEvidenceTool.cliRejectsHblankStateContradictingItsVint` | **OpenGGF consumer:** retain with the Java PLC evidence CLI validator. |
| `TestPlcTimingEvidenceTool.cliRejectsHblankAssociationAfterASecondVint` | **OpenGGF consumer:** retain with the Java PLC evidence CLI validator. |
| `TestPlcTimingEvidenceTool.cliRejectsOracleOnlyRecordsWithoutAnIndependentFrameSnapshot` | **OpenGGF consumer:** retain with the Java PLC evidence CLI validator. |
| `TestPlcTimingEvidenceTool.cliRejectsAServiceHookThatDidNotCaptureItsPostServiceState` | **OpenGGF consumer:** retain with the Java PLC evidence CLI validator. |
| `TestPlcTimingEvidenceTool.structuralMutationsRejectTheObservedOracle` | **OpenGGF consumer:** retain with the Java PLC evidence analyzer. |
| `TestPlcTimingEvidenceTool.replacementDiscardsTheOldQueueWithoutClearingItsOwnReplacement` | **OpenGGF consumer:** retain with the Java PLC state-machine analyzer. |
| `TestPlcTimingEvidenceTool.consumerSeesQueuedHeadBeforeRunPlcPreparesItsDecoder` | **OpenGGF consumer:** retain with the Java PLC state-machine analyzer. |
| `TestPlcTimingEvidenceTool.atomicReplacementRecordCarriesTheCompletedIdlePostState` | **OpenGGF consumer:** retain with the Java PLC evidence CLI. |
| `TestPlcTimingEvidenceTool.bothProbeStateMachinesHandleEmptyPartialAndCompletingCalls` | `PlcProbeBehaviorContractTests.test_both_state_machines_handle_empty_partial_and_completing_calls`; the byte-preserved harness is now `testing/lua/plc_timing_probe_contract_test.lua`. |
| `TestS1CompleteRunLuaContract.pureContractCoversCompleteRunQueuePriorityLifecycleAndDacSemantics` | `PureLuaAudioContractTests.test_s1_complete_run_queue_priority_lifecycle_and_dac_semantics` |
| `TestS1CompleteRunProbeContract.probeIsReadOnlyAndPinsEverySourceDerivedM68kLifecycleAndLoaderSite` | `S1CompleteRunProbeContractTests.test_probe_is_read_only_and_pins_m68k_lifecycle_and_loader_sites` |
| `TestS1CompleteRunProbeContract.probeConsumesTypedNativeZ80DacServicesWithoutM68kParentAssumption` | `S1CompleteRunProbeContractTests.test_probe_consumes_typed_z80_dac_services_without_m68k_parent` |
| `TestS1AudioParityLuaContract.pureLuaContractReproducesSharedHandDerivedGoldenVector` | `PureLuaAudioContractTests.test_s1_parity_contract_reproduces_hand_derived_vector` |
| `TestS1AudioParityProbeContract.observerIsRuntimeOwnedReadOnlyAndCoversEveryReviewedCaptureSite` | `S1AudioParityProbeContractTests.test_observer_is_runtime_owned_read_only_and_covers_reviewed_sites` |
| `TestS1AudioParityProbeContract.linuxLauncherSuppliesDigestOfActualMovieBytes` | `S1AudioParityProbeContractTests.test_linux_launcher_supplies_digest_of_actual_movie_bytes`; invokes the real launcher with a fake Mono endpoint and proves the actual BK2 bytes override a forged digest. |
| `TestS1GameplayAudioTimelineLuaContract.pureLuaTimelineContractPreservesQueueAndContentionSemantics` | `PureLuaAudioContractTests.test_s1_gameplay_timeline_preserves_queue_and_contention_semantics` |
| `TestS1Ghz1GameplayAudioProbeContract.probeIsReadOnlyPinnedAndUsesTheTimelineContract` | `S1GameplayAudioProbeContractTests.test_probe_is_read_only_pinned_and_uses_timeline_contract` |

## Historical citations

`docs/history-import.md`, `history-paths.tsv`, and provenance passages in the
imported component documentation intentionally retain former OpenGGF paths.
They describe where bytes and evidence originated, not live TraceChaser command
locations. Live commands and internal citations use the root-relative paths in
the table above.
