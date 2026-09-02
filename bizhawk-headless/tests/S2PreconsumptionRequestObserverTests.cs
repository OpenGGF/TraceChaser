using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace OpenGGF.BizHawk.Headless.Tests
{
    internal static class S2PreconsumptionRequestObserverTests
    {
        public static void Register(ICollection<TestMain.TestCase> tests)
        {
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests retain the fixed accepted transfer until its exact A7 marker",
                RetainsFixedAcceptedTransferUntilExactMarker));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests preserve the reviewed kind-3 marker owner through correlation",
                PreservesKind3MarkerOwnerThroughCorrelation));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests use exact production GPGX M68K register names",
                UsesExactProductionM68kRegisterNames));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject malformed request transfer correlation",
                RejectsMalformedCorrelation));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests pin the unbound fixed request manifest without changing v2 authority",
                PinsUnboundFixedManifestWithoutChangingV2Authority));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject ambiguous legacy marker inventory fields",
                RejectsAmbiguousLegacyMarkerInventoryFields));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject reordered or inexact marker inventory maps",
                RejectsReorderedOrInexactMarkerInventoryMaps));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests make the session own the callback advance drain and correlation",
                SessionOwnsCallbackAdvanceDrainAndCorrelation));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests close native profile correlation and raw v3 publication in one producer",
                ClosesNativeCorrelationAndRawV3Publication));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests fail before registration when the closed producer manifest differs",
                RejectsClosedProducerManifestMismatch));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests fail before native configuration when the closed producer base profile differs",
                RejectsClosedProducerBaseProfileMismatch));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject an early closed-producer cutoff and clean both observers once",
                RejectsEarlyClosedProducerCutoffAndCleansOnce));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests keep ABI4 EventCount unavailable during an active callback frame",
                KeepsAbi4EventCountUnavailableDuringCallbackFrame));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests correlate multiple requests in one row across ordinary events",
                CorrelatesMultipleRequestsAcrossOrdinaryEvents));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject a non-marker record before the exact marker",
                RejectsNonMarkerBeforeExactMarker));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject a marker with the wrong source or root owner",
                RejectsWrongSourceOrOwner));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests observe from row zero and publish only comparison-boundary transfers",
                ObservesFromRowZeroAndPublishesAtBoundary));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests ignore ordinary native events on a row without a request",
                IgnoresOrdinaryEventsWithoutRequest));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests ignore ordinary native events around the exact request marker",
                IgnoresOrdinaryEventsAroundExactMarker));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject a wrong fixed marker kind",
                () => RejectsFixedMarkerMutation(value => { value.Kind = 9; return value; })));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject a wrong fixed marker value",
                () => RejectsFixedMarkerMutation(value => { value.Value = 2; return value; })));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject a wrong fixed marker PC",
                () => RejectsFixedMarkerMutation(value => { value.Pc = 0x0010D7; return value; })));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject a wrong fixed marker token",
                () => RejectsFixedMarkerMutation(value => { value.Subject = 23; return value; })));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject a wrong fixed marker payload",
                () => RejectsFixedMarkerMutation(value => { value.Payload = 0x11; return value; })));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject a wrong fixed marker source",
                () => RejectsFixedMarkerMutation(value => { value.SourceCpu = 1; return value; })));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject a wrong fixed marker root token",
                () => RejectsFixedMarkerMutation(value => { value.ServiceToken = 1; return value; })));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject a wrong fixed marker root kind",
                () => RejectsFixedMarkerMutation(value => { value.ServiceKindId = 1; return value; })));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject a wrong fixed marker root depth",
                () => RejectsFixedMarkerMutation(value => { value.Depth = 1; return value; })));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject a zero request callback",
                () => RejectsCallback(0, 0, "zero transfer")));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject a below-range request slot",
                () => RejectsCallback(1, uint.MaxValue, "slot outside")));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject an above-range request slot",
                () => RejectsCallback(1, 4, "slot outside")));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject callback overflow in one owned row",
                RejectsCallbackOverflow));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject a callback outside the owned row",
                RejectsCallbackOutsideOwnedRow));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject a duplicate fixed marker",
                RejectsDuplicateFixedMarker));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject end-of-row callback residue",
                RejectsEndOfRowCallbackResidue));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests dispose the fixed registration when terminal evidence is early",
                DisposesWhenTerminalEvidenceIsEarly));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject a matching marker already observed before its callback",
                RejectsMarkerObservedBeforeCallback));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests fail closed when an owned candidate session is disposed early",
                FailsClosedWhenOwnedCandidateSessionIsDisposedEarly));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests complete the full candidate interval and unregister once",
                CompletesFullCandidateIntervalAndUnregistersOnce));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject a cross-row candidate advance",
                RejectsCrossRowCandidateAdvance));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject reversed FIFO callback marker evidence",
                RejectsReversedFifoCallbackMarkerEvidence));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject terminal evidence while a callback is pending",
                RejectsTerminalEvidenceWhileCallbackPending));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests ignore a completed service wholly before the callback successor boundary",
                IgnoresTerminalBeforeCallbackSuccessorBoundary));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests compare terminal evidence to each pending callback successor boundary",
                ComparesTerminalAgainstEachPendingCallbackBoundary));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject a terminal at the second pending callback boundary",
                RejectsTerminalAtSecondPendingCallbackBoundary));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject terminal evidence at a pending callback successor boundary",
                RejectsTerminalAtCallbackSuccessorBoundary));
        }

        private static void RetainsFixedAcceptedTransferUntilExactMarker()
        {
            var host = new FakeHost();
            host.Set("M68K D0", 0x000000B5); host.Set("M68K D1", 3);
            host.Set("M68K A7", 0x00FF1020);
            var api = new QueuedTraceApi();
            var observer = OpenSession(host, api);
            IReadOnlyList<S2PreconsumptionRequestObserver.Transfer> transfers =
                observer.AdvanceRow(0, () =>
                {
                    api.Events = new GpgxAudioTraceEvent[0];
                    host.Execute(S2PreconsumptionRequestObserver.Pc);
                    api.Events = new[] { Marker(0x00FF1020, 0) };
                });
            AssertEx.Equal(1, transfers.Count);
            AssertEx.Equal(0, transfers[0].Row);
            AssertEx.Equal((byte)0xB5, transfers[0].Request);
            AssertEx.Equal((ushort)3, transfers[0].Slot);
            AssertEx.Equal(0x0010D6u, transfers[0].Pc);
            AssertEx.Equal(0x00FF1020u, transfers[0].A7);
            AssertEx.Equal(0u, transfers[0].NativeOrdinal);
            DisposeIncompleteSession(observer);
            AssertEx.Equal(1, host.Registrations);
            AssertEx.Equal(1, host.Disposals);
            AssertEx.Equal(S2PreconsumptionRequestObserver.Pc, host.Address);
        }

        private static void UsesExactProductionM68kRegisterNames()
        {
            var host = RequestHost(0xB5, 3, 0x00FF1020);
            AssertEx.Throws<ArgumentException>(() => host.Set("D0", 0xB5),
                "production GPGX M68K register names");
            AssertEx.Throws<InvalidOperationException>(
                () => host.ReadCpuRegister("A7"),
                "non-production GPGX register name");
            var api = new QueuedTraceApi();
            var observer = OpenSession(host, api);
            IReadOnlyList<S2PreconsumptionRequestObserver.Transfer> transfers =
                observer.AdvanceRow(0, () =>
                {
                    host.Execute(S2PreconsumptionRequestObserver.Pc);
                    api.Events = new[] { Marker(0x00FF1020, 0) };
                });
            AssertEx.Equal(1, transfers.Count);
            AssertEx.Equal((byte)0xB5, transfers[0].Request);
            AssertEx.Equal((ushort)3, transfers[0].Slot);
            AssertEx.Equal(0x00FF1020u, transfers[0].A7);
            DisposeIncompleteSession(observer);
        }

        private static void PreservesKind3MarkerOwnerThroughCorrelation()
        {
            var host = RequestHost(0xCE, 2, 0x00FF1000);
            var api = new QueuedTraceApi();
            var observer = OpenSession(host, api);
            observer.AdvanceRow(0, () =>
                api.Events = new[] { Kind3ServiceBegin(0, 1) });
            IReadOnlyList<S2PreconsumptionRequestObserver.Transfer> transfers =
                observer.AdvanceRow(1, () =>
                {
                    api.Events = new GpgxAudioTraceEvent[0];
                    host.Execute(S2PreconsumptionRequestObserver.Pc);
                    api.Events = new[]
                    {
                        Kind3Marker(0x00FF1000, 0, 1)
                    };
                });
            AssertEx.Equal(1, transfers.Count);
            AssertEx.Equal((ushort)1, transfers[0].ServiceToken);
            AssertEx.Equal((byte)3, transfers[0].ServiceKind);
            AssertEx.Equal((byte)0, transfers[0].Depth);
            AssertEx.Equal((byte)2, transfers[0].SourceCpu);
            DisposeIncompleteSession(observer);
        }

        private static void RejectsMalformedCorrelation()
        {
            var host = new FakeHost();
            host.Set("M68K D0", 1); host.Set("M68K D1", 0);
            host.Set("M68K A7", 0x12345678);
            var api = new QueuedTraceApi();
            using (var observer = OpenSession(host, api))
            {
                AssertEx.Throws<InvalidOperationException>(() =>
                    observer.AdvanceRow(0, () =>
                    {
                        api.Events = new GpgxAudioTraceEvent[0];
                        host.Execute(S2PreconsumptionRequestObserver.Pc);
                        api.Events = new[] { Marker(0x12345679, 0) };
                    }), "A7");
            }
        }

        private static void PinsUnboundFixedManifestWithoutChangingV2Authority()
        {
            S2PreconsumptionRequestProfile.Candidate candidate =
                S2PreconsumptionRequestProfile.LoadCandidate(Fixture(
                    "gpgx-audio-service-manifest-s2-request-v3.json"));
            AssertEx.Equal(0x0010D6u, candidate.Pc);
            AssertEx.Equal("13801009", candidate.Opcode);
            AssertEx.Equal((ushort)24, candidate.MarkerToken);
            AssertEx.Equal((ushort)25, candidate.Kind3MarkerToken);
            AssertEx.Equal(false, candidate.ProductionBound);
            AssertEx.Throws<InvalidOperationException>(() =>
                candidate.RequireProductionAuthority(), "unbound");
            AssertEx.Equal(
                "ef8f8103c38d70e41cb09cb29751f56815a0401709dc509071aa514d614813a0",
                S2AudioObserverProfile.ServiceManifestSha256);
            AssertEx.Equal(
                S2PreconsumptionRequestProfile.CandidateNativePatchSha256,
                Sha256File(Path.Combine(EndToEndTests.ToolDirectory,
                    "native", "gpgx-audio-observer-candidates",
                    "0001-s2-request-successor-ordinal.patch")));
            AssertEx.Equal(
                S2PreconsumptionRequestProfile.CandidateNativeRecipeSha256,
                Sha256File(Path.Combine(EndToEndTests.ToolDirectory,
                    "native", "gpgx-audio-observer-candidates",
                    "s2-request-selftest-recipe.json")));
        }

        private static void RejectsAmbiguousLegacyMarkerInventoryFields()
        {
            AssertCandidateManifestRejected(root =>
                root["request_transfer"]["marker_token"] = 24,
                "marker token map");
            AssertCandidateManifestRejected(root =>
                root["request_transfer"]["marker_expected_kinds"] =
                    new JArray(0, 3), "marker token map");
        }

        private static void RejectsReorderedOrInexactMarkerInventoryMaps()
        {
            AssertCandidateManifestRejected(root =>
            {
                JArray map = (JArray)root["request_transfer"]
                    ["marker_tokens_by_expected_kind"];
                JToken first = map[0];
                map[0] = map[1];
                map[1] = first;
            }, "marker token map");
            AssertCandidateManifestRejected(root =>
                root["request_transfer"]["marker_tokens_by_expected_kind"]
                    [0]["extra"] = 1, "marker token map");
            AssertCandidateManifestRejected(root =>
                ((JArray)root["request_transfer"]
                    ["marker_tokens_by_expected_kind"]).RemoveAt(1),
                "marker token map");
        }

        private static void AssertCandidateManifestRejected(
            Action<JObject> mutate, string message)
        {
            string scratch = TestScratch.CreateRootPath(
                "s2-request-marker-map");
            Directory.CreateDirectory(scratch);
            try
            {
                JObject root = JObject.Parse(File.ReadAllText(Fixture(
                    "gpgx-audio-service-manifest-s2-request-v3.json")));
                mutate(root);
                string path = Path.Combine(scratch, "candidate.json");
                File.WriteAllText(path, root.ToString());
                AssertEx.Throws<InvalidDataException>(() =>
                    S2PreconsumptionRequestProfile.LoadCandidate(path),
                    message);
            }
            finally { Directory.Delete(scratch, true); }
        }

        private static void SessionOwnsCallbackAdvanceDrainAndCorrelation()
        {
            var host = new FakeHost();
            host.Set("M68K D0", 0xCE); host.Set("M68K D1", 2);
            host.Set("M68K A7", 0x00FF1000);
            var api = new QueuedTraceApi();
            var session = OpenSession(host, api);
            try
            {
                for (int row = 0; row <= S2AudioObserverProfile.FirstRow; row++)
                {
                    int capturedRow = row;
                    session.AdvanceRow(row, () =>
                    {
                        if (capturedRow == S2AudioObserverProfile.FirstRow)
                            host.Execute(S2PreconsumptionRequestObserver.Pc);
                        api.Events = capturedRow == S2AudioObserverProfile.FirstRow
                            ? new[] { Marker(0x00FF1000, 0) }
                            : new GpgxAudioTraceEvent[0];
                    });
                }
                IReadOnlyList<S2PreconsumptionRequestObserver.Transfer> transfers =
                    session.PublishedTransfers;
                AssertEx.Equal(1, transfers.Count);
                AssertEx.Equal((byte)0xCE, transfers[0].Request);
                AssertEx.Equal((ushort)2, transfers[0].Slot);
            }
            finally { DisposeIncompleteSession(session); }
            AssertEx.Equal(1, host.Disposals);
        }

        private static void ClosesNativeCorrelationAndRawV3Publication()
        {
            var api = new QueuedTraceApi { RequireFixedCandidateHook = true };
            var host = RequestHost(0xCE, 2, 0x00FF1000);
            host.AudioApi = api;
            var output = new StringWriter();
            S2CompleteAudioCaptureRunner.RequestAwareRawV3Candidate producer =
                S2CompleteAudioCaptureRunner.OpenRequestAwareRawV3Candidate(
                    Fixture("gpgx-audio-service-manifest-s2-request-v3.json"),
                    Fixture("gpgx-audio-service-manifests-v1.json"),
                    host, output);
            AssertEx.Equal(1, api.ConfigureCalls);
            AssertEx.Equal(2, api.FixedCandidateHookCount);
            AssertEx.Equal(1, host.Registrations);
            AssertEx.Equal(S2PreconsumptionRequestObserver.Pc, host.Address);
            try
            {
                for (int row = 0; row <= S2AudioObserverProfile.FirstRow; row++)
                {
                    int ownedRow = row;
                    host.AdvanceAction = () =>
                    {
                        api.Events = new GpgxAudioTraceEvent[0];
                        if (ownedRow == S2AudioObserverProfile.FirstRow - 1)
                        {
                            api.Events = BootstrapAndKind3Begin();
                        }
                        else if (ownedRow == S2AudioObserverProfile.FirstRow)
                        {
                            host.Execute(S2PreconsumptionRequestObserver.Pc);
                            api.Events = new[]
                            {
                                Kind3Marker(0x00FF1000, 0, 2)
                            };
                        }
                    };
                    producer.AdvanceRow(row, new Bk2Frame());
                }
                string[] lines = output.ToString().Split(
                    new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                JObject selected = null;
                for (int index = 0; index < lines.Length; index++)
                {
                    JObject value = JObject.Parse(lines[index]);
                    if ((string)value["type"] == "frame") selected = value;
                }
                AssertEx.Equal(true, selected != null);
                AssertEx.Equal(S2AudioObserverProfile.FirstRow,
                    (int)selected["row"]);
                JArray events = (JArray)selected["events"];
                JArray transfers = (JArray)selected["request_transfers"];
                AssertEx.Equal(1, events.Count);
                AssertEx.Equal(1, transfers.Count);
                AssertEx.Equal((int)events[0]["ordinal"],
                    (int)transfers[0]["native_ordinal"]);
                AssertEx.Equal((long)events[0]["payload"],
                    (long)transfers[0]["a7"]);
                AssertEx.Equal(2,
                    (int)transfers[0]["service_token"]);
                AssertEx.Equal(3,
                    (int)transfers[0]["service_kind"]);
                AssertEx.Equal(0,
                    (int)transfers[0]["depth"]);
                AssertEx.Equal(0, host.Disposals);
            }
            finally
            {
                AssertEx.Throws<InvalidDataException>(
                    () => producer.Dispose(), "full power-on interval");
            }
            AssertEx.Equal(1, host.Disposals);
        }

        private static void RejectsClosedProducerManifestMismatch()
        {
            string scratch = TestScratch.CreateRootPath(
                "s2-request-candidate-manifest");
            Directory.CreateDirectory(scratch);
            try
            {
                JObject candidate = JObject.Parse(File.ReadAllText(Fixture(
                    "gpgx-audio-service-manifest-s2-request-v3.json")));
                candidate["request_transfer"]["marker_token"] = 23;
                string path = Path.Combine(scratch, "candidate.json");
                File.WriteAllText(path, candidate.ToString());
                var api = new QueuedTraceApi { RequireFixedCandidateHook = true };
                var host = new FakeHost { AudioApi = api };
                AssertEx.Throws<InvalidDataException>(() =>
                    S2CompleteAudioCaptureRunner.OpenRequestAwareRawV3Candidate(
                        path, Fixture("gpgx-audio-service-manifests-v1.json"),
                        host, new StringWriter()), "marker token map");
                AssertEx.Equal(0, host.Registrations);
                AssertEx.Equal(0, api.ConfigureCalls);
            }
            finally { Directory.Delete(scratch, true); }
        }

        private static void RejectsClosedProducerBaseProfileMismatch()
        {
            string scratch = TestScratch.CreateRootPath(
                "s2-request-base-manifest");
            Directory.CreateDirectory(scratch);
            try
            {
                string path = Path.Combine(scratch, "base.json");
                JObject manifest = JObject.Parse(File.ReadAllText(Fixture(
                    "gpgx-audio-service-manifests-v1.json")));
                manifest["games"]["s2"]["hooks"][0]["pc"] = 57;
                File.WriteAllText(path, manifest.ToString());
                var api = new QueuedTraceApi
                    { RequireFixedCandidateHook = true };
                var host = new FakeHost { AudioApi = api };
                AssertEx.Throws<InvalidDataException>(() =>
                    S2CompleteAudioCaptureRunner.OpenRequestAwareRawV3Candidate(
                        Fixture("gpgx-audio-service-manifest-s2-request-v3.json"),
                        path, host, new StringWriter()),
                    "base manifest file identity");
                AssertEx.Equal(0, host.Registrations);
                AssertEx.Equal(0, api.ConfigureCalls);
            }
            finally { Directory.Delete(scratch, true); }
        }

        private static void RejectsEarlyClosedProducerCutoffAndCleansOnce()
        {
            var api = new QueuedTraceApi { RequireFixedCandidateHook = true };
            var host = new FakeHost { AudioApi = api };
            S2CompleteAudioCaptureRunner.RequestAwareRawV3Candidate producer =
                S2CompleteAudioCaptureRunner.OpenRequestAwareRawV3Candidate(
                    Fixture("gpgx-audio-service-manifest-s2-request-v3.json"),
                    Fixture("gpgx-audio-service-manifests-v1.json"), host,
                    new StringWriter());
            host.AdvanceAction = () =>
                api.Events = new GpgxAudioTraceEvent[0];
            producer.AdvanceRow(0, new Bk2Frame());
            AssertEx.Throws<InvalidDataException>(() => producer.Complete(),
                "full power-on interval");
            AssertEx.Equal(1, host.Disposals);
            AssertEx.Equal(1, api.DisableCalls);
            producer.Dispose();
            AssertEx.Equal(1, host.Disposals);
            AssertEx.Equal(1, api.DisableCalls);
        }

        private static void KeepsAbi4EventCountUnavailableDuringCallbackFrame()
        {
            var host = RequestHost(0xCE, 2, 0x00FF1000);
            var api = new QueuedTraceApi();
            var session = OpenSession(host, api);
            IReadOnlyList<S2PreconsumptionRequestObserver.Transfer> transfers =
                session.AdvanceRow(0, () =>
                {
                    api.Events = new GpgxAudioTraceEvent[0];
                    host.Execute(S2PreconsumptionRequestObserver.Pc);
                    api.Events = new[] { Marker(0x00FF1000, 0) };
                });
            AssertEx.Equal(1, transfers.Count);
            AssertEx.Equal(0, api.ActiveFrameEventCountAttempts);
            DisposeIncompleteSession(session);
        }

        private static void CorrelatesMultipleRequestsAcrossOrdinaryEvents()
        {
            var host = RequestHost(0xB5, 0, 0x10);
            var api = new QueuedTraceApi();
            var session = OpenSession(host, api);
            try
            {
                IReadOnlyList<S2PreconsumptionRequestObserver.Transfer> transfers =
                    session.AdvanceRow(0, () =>
                    {
                        api.Events = new GpgxAudioTraceEvent[0];
                        host.Execute(S2PreconsumptionRequestObserver.Pc);
                        api.Events = new[] { Marker(0x10, 0) };
                        host.Set("M68K D0", 0xCE); host.Set("M68K D1", 2);
                        host.Set("M68K A7", 0x20);
                        host.Execute(S2PreconsumptionRequestObserver.Pc);
                        api.Events = new[]
                        {
                            Marker(0x10, 0), Ordinary(1), Marker(0x20, 2)
                        };
                    });
                AssertEx.Equal(2, transfers.Count);
                AssertEx.Equal((byte)0xB5, transfers[0].Request);
                AssertEx.Equal(0u, transfers[0].NativeOrdinal);
                AssertEx.Equal((byte)0xCE, transfers[1].Request);
                AssertEx.Equal(2u, transfers[1].NativeOrdinal);
            }
            finally { DisposeIncompleteSession(session); }
        }

        private static void RejectsNonMarkerBeforeExactMarker()
        {
            var host = new FakeHost();
            host.Set("M68K D0", 1); host.Set("M68K D1", 0);
            host.Set("M68K A7", 0x12);
            var api = new QueuedTraceApi();
            using (var observer = OpenSession(host, api))
            {
                AssertEx.Throws<InvalidOperationException>(() =>
                    observer.AdvanceRow(0, () =>
                    {
                        api.Events = new GpgxAudioTraceEvent[0];
                        host.Execute(S2PreconsumptionRequestObserver.Pc);
                        GpgxAudioTraceEvent wrong = Marker(0x12, 0);
                        wrong.Kind = 2;
                        api.Events = new[] { wrong, Marker(0x12, 1) };
                    }), "unexpected payload");
            }
        }

        private static void RejectsWrongSourceOrOwner()
        {
            var host = new FakeHost();
            host.Set("M68K D0", 1); host.Set("M68K D1", 0);
            host.Set("M68K A7", 0x12);
            var api = new QueuedTraceApi();
            using (var observer = OpenSession(host, api))
            {
                AssertEx.Throws<InvalidOperationException>(() =>
                    observer.AdvanceRow(0, () =>
                    {
                        api.Events = new GpgxAudioTraceEvent[0];
                        host.Execute(S2PreconsumptionRequestObserver.Pc);
                        GpgxAudioTraceEvent wrong = Marker(0x12, 0);
                        wrong.SourceCpu = 1; wrong.ServiceToken = 9;
                        wrong.ServiceKindId = 3; wrong.Depth = 1;
                        api.Events = new[] { wrong };
                    }), "marker fields");
            }
        }

        private static void ObservesFromRowZeroAndPublishesAtBoundary()
        {
            var host = new FakeHost();
            host.Set("M68K D0", 0xB5); host.Set("M68K D1", 0);
            host.Set("M68K A7", 1);
            var api = new QueuedTraceApi();
            var session = OpenSession(host, api);
            try
            {
                for (int row = 0; row <= S2AudioObserverProfile.FirstRow; row++)
                {
                    session.AdvanceRow(row, () =>
                    {
                        api.Events = new GpgxAudioTraceEvent[0];
                        host.Execute(S2PreconsumptionRequestObserver.Pc);
                        api.Events = new[] { Marker(1, 0) };
                    });
                }
                AssertEx.Equal(1, session.PublishedTransfers.Count);
                AssertEx.Equal(S2AudioObserverProfile.FirstRow,
                    session.PublishedTransfers[0].Row);
            }
            finally { DisposeIncompleteSession(session); }
            AssertEx.Equal(1, host.Registrations);
            AssertEx.Equal(1, host.Disposals);
        }

        private static void IgnoresOrdinaryEventsWithoutRequest()
        {
            var host = new FakeHost();
            var api = new QueuedTraceApi();
            var session = OpenSession(host, api);
            try
            {
                IReadOnlyList<S2PreconsumptionRequestObserver.Transfer> transfers =
                    session.AdvanceRow(0, () => api.Events = new[] { Ordinary(0) });
                AssertEx.Equal(0, transfers.Count);
                AssertEx.Equal(0, session.PublishedTransfers.Count);
            }
            finally { DisposeIncompleteSession(session); }
        }

        private static void IgnoresOrdinaryEventsAroundExactMarker()
        {
            var host = RequestHost(0xB5, 3, 0x1234);
            var api = new QueuedTraceApi();
            var session = OpenSession(host, api);
            try
            {
                IReadOnlyList<S2PreconsumptionRequestObserver.Transfer> transfers =
                    session.AdvanceRow(0, () =>
                    {
                        api.Events = new[] { Ordinary(0) };
                        host.Execute(S2PreconsumptionRequestObserver.Pc);
                        api.Events = new[] { Ordinary(0), Marker(0x1234, 1), Ordinary(2) };
                    });
                AssertEx.Equal(1, transfers.Count);
                AssertEx.Equal((byte)0xB5, transfers[0].Request);
                AssertEx.Equal((ushort)3, transfers[0].Slot);
                AssertEx.Equal(1u, transfers[0].NativeOrdinal);
            }
            finally { DisposeIncompleteSession(session); }
        }

        private static void RejectsFixedMarkerMutation(
            Func<GpgxAudioTraceEvent, GpgxAudioTraceEvent> mutate)
        {
            var host = RequestHost(1, 0, 0x10);
            var api = new QueuedTraceApi();
            using (var session = OpenSession(host, api))
            {
                AssertEx.Throws<InvalidOperationException>(() => session.AdvanceRow(0, () =>
                {
                    host.Execute(S2PreconsumptionRequestObserver.Pc);
                    GpgxAudioTraceEvent marker = Marker(0x10, 0);
                    marker = mutate(marker);
                    api.Events = new[] { marker };
                }), "");
            }
        }

        private static void RejectsCallback(uint request, uint slot,
            string message)
        {
            var host = new FakeHost();
            host.Set("M68K D0", request); host.Set("M68K D1", slot);
            host.Set("M68K A7", 0x10);
            var api = new QueuedTraceApi();
            using (var session = OpenSession(host, api))
            {
                AssertEx.Throws<InvalidOperationException>(() => session.AdvanceRow(0,
                    () => host.Execute(S2PreconsumptionRequestObserver.Pc)), message);
            }
        }

        private static void RejectsCallbackOverflow()
        {
            var host = RequestHost(1, 0, 0x10);
            var api = new QueuedTraceApi();
            using (var session = OpenSession(host, api))
            {
                AssertEx.Throws<InvalidOperationException>(() => session.AdvanceRow(0, () =>
                {
                    for (int index = 0; index < 5; index++)
                        host.Execute(S2PreconsumptionRequestObserver.Pc);
                }), "four-slot");
            }
        }

        private static void RejectsCallbackOutsideOwnedRow()
        {
            var host = RequestHost(1, 0, 0x10);
            var api = new QueuedTraceApi();
            var session = OpenSession(host, api);
            try
            {
                AssertEx.Throws<InvalidOperationException>(() =>
                    host.Execute(S2PreconsumptionRequestObserver.Pc), "outside an active row");
            }
            finally { DisposeIncompleteSession(session); }
        }

        private static void RejectsDuplicateFixedMarker()
        {
            var host = RequestHost(1, 0, 0x10);
            var api = new QueuedTraceApi();
            using (var session = OpenSession(host, api))
            {
                AssertEx.Throws<InvalidOperationException>(() => session.AdvanceRow(0, () =>
                {
                    host.Execute(S2PreconsumptionRequestObserver.Pc);
                    api.Events = new[] { Marker(0x10, 0), Marker(0x10, 1) };
                }), "orphaned or duplicated");
            }
        }

        private static void RejectsEndOfRowCallbackResidue()
        {
            var host = RequestHost(1, 0, 0x10);
            var api = new QueuedTraceApi();
            using (var session = OpenSession(host, api))
            {
                AssertEx.Throws<InvalidOperationException>(() => session.AdvanceRow(0, () =>
                {
                    host.Execute(S2PreconsumptionRequestObserver.Pc);
                    api.Events = new GpgxAudioTraceEvent[0];
                }), "no exact native A7 marker");
            }
        }

        private static void DisposesWhenTerminalEvidenceIsEarly()
        {
            var host = new FakeHost();
            var api = new QueuedTraceApi();
            var session = OpenSession(host, api);
            AssertEx.Throws<InvalidDataException>(() => session.Complete(),
                "full power-on interval");
            AssertEx.Equal(1, host.Disposals);
            session.Dispose();
            AssertEx.Equal(1, host.Disposals);
        }

        private static void RejectsMarkerObservedBeforeCallback()
        {
            var host = RequestHost(1, 0, 0x10);
            var api = new QueuedTraceApi();
            using (var session = OpenSession(host, api))
            {
                AssertEx.Throws<InvalidOperationException>(() => session.AdvanceRow(0, () =>
                {
                    api.Events = new[] { Marker(0x10, 0) };
                    host.Execute(S2PreconsumptionRequestObserver.Pc);
                    api.Events = new[] { Marker(0x10, 0), Marker(0x10, 1) };
                }), "successor");
            }
        }

        private static void FailsClosedWhenOwnedCandidateSessionIsDisposedEarly()
        {
            var host = new FakeHost();
            var session = OpenSession(host, new QueuedTraceApi());
            AssertEx.Throws<InvalidDataException>(() => session.Dispose(),
                "full power-on interval");
            AssertEx.Equal(1, host.Disposals);
            session.Dispose();
            AssertEx.Equal(1, host.Disposals);
        }

        private static void CompletesFullCandidateIntervalAndUnregistersOnce()
        {
            var host = new FakeHost();
            var api = new QueuedTraceApi();
            var session = OpenSession(host, api);
            for (int row = 0; row < S2AudioObserverProfile.ExclusiveEnd; row++)
                session.AdvanceRow(row, () =>
                    api.Events = new GpgxAudioTraceEvent[0]);
            session.Complete();
            AssertEx.Equal(1, host.Disposals);
            session.Dispose();
            AssertEx.Equal(1, host.Disposals);
        }

        private static void RejectsCrossRowCandidateAdvance()
        {
            var host = new FakeHost();
            var session = OpenSession(host, new QueuedTraceApi());
            AssertEx.Throws<InvalidDataException>(() => session.AdvanceRow(1,
                () => { }), "cannot carry evidence across rows");
            AssertEx.Equal(1, host.Disposals);
        }

        private static void RejectsReversedFifoCallbackMarkerEvidence()
        {
            var host = RequestHost(1, 0, 0x10);
            var api = new QueuedTraceApi();
            using (var session = OpenSession(host, api))
            {
                AssertEx.Throws<InvalidOperationException>(() => session.AdvanceRow(0, () =>
                {
                    host.Execute(S2PreconsumptionRequestObserver.Pc);
                    host.Set("M68K A7", 0x20);
                    api.Events = new[] { Marker(0x10, 0) };
                    host.Execute(S2PreconsumptionRequestObserver.Pc);
                    api.Events = new[] { Marker(0x20, 0), Marker(0x10, 1) };
                }), "A7 differs");
            }
        }

        private static void RejectsTerminalEvidenceWhileCallbackPending()
        {
            var host = RequestHost(1, 0, 0x10);
            var api = new QueuedTraceApi();
            using (var observer = OpenSession(host, api))
            {
                AssertEx.Throws<InvalidOperationException>(() =>
                    observer.AdvanceRow(0, () =>
                    {
                        api.Events = new GpgxAudioTraceEvent[0];
                        host.Execute(S2PreconsumptionRequestObserver.Pc);
                        api.Events = new[] { new GpgxAudioTraceEvent { Kind = 2 } };
                    }), "completion without active service");
            }
        }

        private static void IgnoresTerminalBeforeCallbackSuccessorBoundary()
        {
            var host = RequestHost(1, 0, 0x10);
            var api = new QueuedTraceApi();
            var observer = OpenSession(host, api);
            IReadOnlyList<S2PreconsumptionRequestObserver.Transfer> transfers =
                observer.AdvanceRow(0, () =>
                {
                    api.Events = new[]
                    {
                        ServiceBegin(0, 1), ServiceEnd(1, 1)
                    };
                    host.Execute(S2PreconsumptionRequestObserver.Pc);
                    api.Events = new[]
                    {
                        ServiceBegin(0, 1), ServiceEnd(1, 1),
                        Marker(0x10, 2)
                    };
                });
            AssertEx.Equal(1, transfers.Count);
            AssertEx.Equal(2u, transfers[0].NativeOrdinal);
            DisposeIncompleteSession(observer);
        }

        private static void ComparesTerminalAgainstEachPendingCallbackBoundary()
        {
            var host = RequestHost(1, 0, 0x10);
            var api = new QueuedTraceApi();
            var observer = OpenSession(host, api);
            IReadOnlyList<S2PreconsumptionRequestObserver.Transfer> transfers =
                observer.AdvanceRow(0, () =>
                {
                    api.Events = new[]
                    {
                        ServiceBegin(0, 1), ServiceEnd(1, 1)
                    };
                    host.Execute(S2PreconsumptionRequestObserver.Pc);
                    api.Events = new[]
                    {
                        ServiceBegin(0, 1), ServiceEnd(1, 1),
                        Marker(0x10, 2), ServiceBegin(3, 2), ServiceEnd(4, 2)
                    };
                    host.Set("M68K D0", 2); host.Set("M68K D1", 1);
                    host.Set("M68K A7", 0x20);
                    host.Execute(S2PreconsumptionRequestObserver.Pc);
                    api.Events = new[]
                    {
                        ServiceBegin(0, 1), ServiceEnd(1, 1),
                        Marker(0x10, 2), ServiceBegin(3, 2), ServiceEnd(4, 2),
                        Marker(0x20, 5)
                    };
                });
            AssertEx.Equal(2, transfers.Count);
            AssertEx.Equal(2u, transfers[0].NativeOrdinal);
            AssertEx.Equal(5u, transfers[1].NativeOrdinal);
            DisposeIncompleteSession(observer);
        }

        private static void RejectsTerminalAtCallbackSuccessorBoundary()
        {
            var host = RequestHost(1, 0, 0x10);
            var api = new QueuedTraceApi();
            using (var observer = OpenSession(host, api))
            {
                AssertEx.Throws<InvalidOperationException>(() =>
                    observer.AdvanceRow(0, () =>
                    {
                        host.Execute(S2PreconsumptionRequestObserver.Pc);
                        api.Events = new[]
                        {
                            ServiceBegin(0, 1), ServiceEnd(1, 1),
                            Marker(0x10, 2)
                        };
                    }), "terminal boundary followed its callback");
            }
        }

        private static void RejectsTerminalAtSecondPendingCallbackBoundary()
        {
            var host = RequestHost(1, 0, 0x10);
            var api = new QueuedTraceApi();
            using (var observer = OpenSession(host, api))
            {
                AssertEx.Throws<InvalidOperationException>(() =>
                    observer.AdvanceRow(0, () =>
                    {
                        api.Events = new[]
                        {
                            ServiceBegin(0, 1), ServiceEnd(1, 1)
                        };
                        host.Execute(S2PreconsumptionRequestObserver.Pc);
                        api.Events = new[]
                        {
                            ServiceBegin(0, 1), ServiceEnd(1, 1),
                            Marker(0x10, 2), ServiceBegin(3, 2)
                        };
                        host.Set("M68K D0", 2); host.Set("M68K D1", 1);
                        host.Set("M68K A7", 0x20);
                        host.Execute(S2PreconsumptionRequestObserver.Pc);
                        api.Events = new[]
                        {
                            ServiceBegin(0, 1), ServiceEnd(1, 1),
                            Marker(0x10, 2), ServiceBegin(3, 2),
                            ServiceEnd(4, 2), Marker(0x20, 5)
                        };
                    }), "terminal boundary followed its callback");
            }
        }

        private static S2PreconsumptionRequestObserver
            OpenSession(FakeHost host, QueuedTraceApi api)
        {
            S2PreconsumptionRequestProfile.Candidate candidate =
                S2PreconsumptionRequestProfile.LoadCandidate(Fixture(
                    "gpgx-audio-service-manifest-s2-request-v3.json"));
            return new S2PreconsumptionRequestObserver(candidate, host,
                CreateAudioObserver(api));
        }

        private static void DisposeIncompleteSession(
            S2PreconsumptionRequestObserver session)
        {
            AssertEx.Throws<InvalidDataException>(() => session.Dispose(),
                "full power-on interval");
        }

        private static FakeHost RequestHost(byte request, ushort slot, uint a7)
        {
            var host = new FakeHost();
            host.Set("M68K D0", request); host.Set("M68K D1", slot);
            host.Set("M68K A7", a7);
            return host;
        }

        private static GpgxAudioTraceEvent Ordinary(uint ordinal)
        {
            return new GpgxAudioTraceEvent
            {
                Kind = 10, Value = 3, Pc = 0x002000, Subject = 30,
                Ordinal = ordinal, PayloadLength = 4, Payload = 0,
                SourceCpu = 2, ServiceToken = 0, ParentToken = 0,
                ServiceKindId = 0, Depth = 0
            };
        }

        private static string Fixture(string name)
        {
            return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "fixtures", name));
        }

        private static string Sha256File(string path)
        {
            using (SHA256 hash = SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(File.ReadAllBytes(path));
                return BitConverter.ToString(bytes).Replace("-", "")
                    .ToLowerInvariant();
            }
        }

        private static GpgxAudioTraceEvent Marker(uint a7, uint ordinal)
        {
            return new GpgxAudioTraceEvent
            {
                Kind = 10, Value = 3, Pc = S2PreconsumptionRequestObserver.Pc,
                Subject = S2PreconsumptionRequestObserver.MarkerToken,
                Ordinal = ordinal, PayloadLength = 4, Payload = a7,
                SourceCpu = 2, ServiceToken = 0, ParentToken = 0,
                ServiceKindId = 0, Depth = 0
            };
        }

        private static GpgxAudioTraceEvent Kind3Marker(uint a7,
            uint ordinal, ushort serviceToken)
        {
            return new GpgxAudioTraceEvent
            {
                Kind = 10, Value = 3,
                Pc = S2PreconsumptionRequestObserver.Pc,
                Subject = 25, Ordinal = ordinal,
                PayloadLength = 4, Payload = a7,
                SourceCpu = 2, ServiceToken = serviceToken,
                ParentToken = 0, ServiceKindId = 3, Depth = 0
            };
        }

        private static GpgxAudioTraceEvent Kind3ServiceBegin(
            uint ordinal, ushort token)
        {
            return new GpgxAudioTraceEvent
            {
                Kind = 1, Ordinal = ordinal, ServiceToken = token,
                ParentToken = 0, Pc = 0x002FF0, Subject = 31,
                ServiceKindId = 3, Depth = 0, SourceCpu = 2
            };
        }

        private static GpgxAudioTraceEvent[] BootstrapAndKind3Begin()
        {
            const int ChunkCount = 1024;
            var events = new GpgxAudioTraceEvent[ChunkCount + 5];
            events[0] = new GpgxAudioTraceEvent
            {
                Ordinal = 0, Kind = 1, ServiceToken = 1,
                ParentToken = 0, ServiceKindId = 2, Depth = 0,
                SourceCpu = 2, Pc = 0x000EC000, Subject = 9
            };
            events[1] = new GpgxAudioTraceEvent
            {
                Ordinal = 1, Kind = 5, ServiceToken = 1,
                ParentToken = 0, ServiceKindId = 2, Depth = 0,
                SourceCpu = 2, Pc = 0x000EC036, Subject = 1
            };
            for (int index = 0; index < ChunkCount; index++)
            {
                events[index + 2] = new GpgxAudioTraceEvent
                {
                    Ordinal = (uint)(index + 2), Kind = 6,
                    ServiceToken = 1, ParentToken = 0,
                    ServiceKindId = 2, Depth = 0, SourceCpu = 2,
                    Pc = 0x000EC036, Subject = 1,
                    Offset = (ushort)(index * 8), PayloadLength = 8
                };
            }
            events[ChunkCount + 2] = new GpgxAudioTraceEvent
            {
                Ordinal = (uint)(ChunkCount + 2), Kind = 7,
                ServiceToken = 1, ParentToken = 0,
                ServiceKindId = 2, Depth = 0, SourceCpu = 2,
                Pc = 0x000EC036, Subject = 1, Offset = 8192
            };
            events[ChunkCount + 3] = new GpgxAudioTraceEvent
            {
                Ordinal = (uint)(ChunkCount + 3), Kind = 2,
                ServiceToken = 1, ParentToken = 0,
                ServiceKindId = 2, Depth = 0, SourceCpu = 2,
                Pc = 0x000EC036, Subject = 10
            };
            events[ChunkCount + 4] = new GpgxAudioTraceEvent
            {
                Ordinal = (uint)(ChunkCount + 4), Kind = 1,
                ServiceToken = 2, ParentToken = 0,
                ServiceKindId = 3, Depth = 0, SourceCpu = 1,
                Pc = 56, Subject = 1
            };
            return events;
        }

        private static GpgxAudioTraceEvent ServiceBegin(
            uint ordinal, ushort token)
        {
            return new GpgxAudioTraceEvent
            {
                Kind = 1, Ordinal = ordinal, ServiceToken = token,
                ParentToken = 0, Pc = 0x003000, Subject = 26,
                ServiceKindId = 1, Depth = 0, SourceCpu = 1
            };
        }

        private static GpgxAudioTraceEvent ServiceEnd(
            uint ordinal, ushort token)
        {
            return new GpgxAudioTraceEvent
            {
                Kind = 2, Ordinal = ordinal, ServiceToken = token,
                ParentToken = 0, Pc = 0x003001, Subject = 27,
                ServiceKindId = 1, Depth = 0, SourceCpu = 1
            };
        }

        private static CompleteRunAudioObserver.FrameCapture Frame(int row,
            params GpgxAudioTraceEvent[] events)
        {
            return new CompleteRunAudioObserver.FrameCapture(events,
                new List<CompleteRunAudioObserver.ServiceBuilder>(),
                new List<CompleteRunAudioObserver.ResetRecord>(), 0,
                (CompleteRunAudioObserver.DeferredBeginReservation)null, row);
        }

        private static CompleteRunAudioObserver CreateAudioObserver(
            QueuedTraceApi api)
        {
            var config = new GpgxAudioObserverAdapter.Config
            {
                AbiVersion = 4, StructSize = 64, KindSize = 16,
                HookSize = 32, RangeSize = 16, EventSize = 32,
                WatchMaskBytes = 8192, EventCapacity = 65536,
                HookCount = 6, KindCount = 2,
                MaxContinuationFrames = 4
            };
            var hooks = new[]
            {
                new GpgxAudioObserverAdapter.ServiceHook
                {
                    HookToken = 31, Action = 1, Cpu = 2, Pc = 0x002FF0,
                    ServiceKindId = 3, ExpectedActiveKind = 0
                },
                new GpgxAudioObserverAdapter.ServiceHook
                {
                    HookToken = 26, Action = 1, Cpu = 1, Pc = 0x003000,
                    ServiceKindId = 1, ExpectedActiveKind = 0
                },
                new GpgxAudioObserverAdapter.ServiceHook
                {
                    HookToken = 27, Action = 2, Cpu = 1, Pc = 0x003001,
                    ServiceKindId = 0, ExpectedActiveKind = 1
                },
                new GpgxAudioObserverAdapter.ServiceHook
                {
                    HookToken = S2PreconsumptionRequestObserver.MarkerToken,
                    Action = 7, Cpu = 2,
                    Pc = S2PreconsumptionRequestObserver.Pc
                },
                new GpgxAudioObserverAdapter.ServiceHook
                {
                    HookToken = 25, Action = 7, Cpu = 2,
                    Pc = S2PreconsumptionRequestObserver.Pc,
                    ExpectedActiveKind = 3
                },
                new GpgxAudioObserverAdapter.ServiceHook
                {
                    HookToken = 30, Action = 7, Cpu = 2, Pc = 0x002000
                }
            };
            return new CompleteRunAudioObserver(api, config, new byte[8192],
                new[]
                {
                    new GpgxAudioObserverAdapter.ServiceKind { KindId = 1 },
                    new GpgxAudioObserverAdapter.ServiceKind
                    {
                        KindId = 3, Flags = 6, ContinuationFrameLimit = 4
                    }
                }, hooks,
                new GpgxAudioObserverAdapter.SnapshotRange[0]);
        }

        private sealed class QueuedTraceApi : IGpgxAudioTraceApi,
            IS2RequestSuccessorOrdinalApi
        {
            internal GpgxAudioTraceEvent[] Events = new GpgxAudioTraceEvent[0];
            private int phase;
            internal int ActiveFrameEventCountAttempts;
            internal bool RequireFixedCandidateHook;
            internal int FixedCandidateHookCount;
            internal int ConfigureCalls;
            internal int DisableCalls;
            public uint AbiVersion { get { return 4; } }
            public uint EventSize { get { return 32; } }
            public uint Capacity { get { return 65536; } }
            public int Configure(ref GpgxAudioObserverAdapter.Config config,
                byte[] mask, GpgxAudioObserverAdapter.ServiceKind[] kinds,
                GpgxAudioObserverAdapter.ServiceHook[] hooks,
                GpgxAudioObserverAdapter.SnapshotRange[] ranges)
            {
                ConfigureCalls++;
                FixedCandidateHookCount = 0;
                int candidateSiteMarkerCount = 0;
                bool exactCandidateMap = true;
                bool sorted = true;
                for (int index = 0; index < hooks.Length; index++)
                {
                    GpgxAudioObserverAdapter.ServiceHook hook = hooks[index];
                    if (index != 0)
                    {
                        GpgxAudioObserverAdapter.ServiceHook previous =
                            hooks[index - 1];
                        if (previous.Cpu > hook.Cpu
                            || (previous.Cpu == hook.Cpu
                                && previous.Pc > hook.Pc)
                            || (previous.Cpu == hook.Cpu
                                && previous.Pc == hook.Pc
                                && previous.HookToken >= hook.HookToken))
                            sorted = false;
                    }
                    if (hook.Action == 7 && hook.Cpu == 2
                        && hook.Pc == 0x0010D6)
                    {
                        bool common = hook.ServiceKindId == 0
                            && hook.Flags == 0 && hook.OpcodeLength == 4
                            && hook.RangeFirst == 0 && hook.RangeCount == 0
                            && hook.Opcode == 0x09108013UL
                            && hook.Reserved == 0;
                        bool exact = candidateSiteMarkerCount == 0
                            ? hook.HookToken == 24
                                && hook.ExpectedActiveKind == 0
                            : candidateSiteMarkerCount == 1
                                && hook.HookToken == 25
                                && hook.ExpectedActiveKind == 3;
                        exactCandidateMap &= common && exact;
                        candidateSiteMarkerCount++;
                    }
                }
                if (exactCandidateMap && candidateSiteMarkerCount == 2)
                    FixedCandidateHookCount = 2;
                if (RequireFixedCandidateHook
                    && (FixedCandidateHookCount != 2 || !sorted)) return -3;
                phase = 1;
                return 0;
            }
            public int BeginFrame()
            {
                if (phase != 1) return -2;
                phase = 2; return 0;
            }
            public int EndFrame()
            {
                if (phase != 2) return -2;
                phase = 3; return 0;
            }
            public int EventCount(out uint count, out uint overflow)
            {
                count = 0; overflow = 0;
                if (phase != 3)
                {
                    if (phase == 2) ActiveFrameEventCountAttempts++;
                    return -2;
                }
                count = (uint)Events.Length; return 0;
            }
            public int Drain(GpgxAudioTraceEvent[] events, uint capacity,
                out uint count)
            {
                if (phase != 3) { count = 0; return -2; }
                count = (uint)Events.Length;
                if (events != null) Array.Copy(Events, events, Events.Length);
                phase = 1;
                return 0;
            }
            public int GetFirstFault(out GpgxAudioObserverAdapter.FirstFault fault)
            { fault = new GpgxAudioObserverAdapter.FirstFault(); return 0; }
            public int BeginPublicationEpoch() { return 0; }
            public int AbortFrame()
            {
                if (phase != 2 && phase != 3) return -2;
                phase = 1; return 0;
            }
            public int Disable()
            { DisableCalls++; phase = 0; return 0; }
            public int S2RequestSuccessorOrdinal(out uint ordinal)
            {
                ordinal = 0;
                if (phase != 2) return -2;
                ordinal = (uint)Events.Length;
                return 0;
            }
        }

        private sealed class FakeHost : IS2RequestAwareRawV3CandidateHost
        {
            private readonly Dictionary<string,uint> registers =
                new Dictionary<string,uint>(StringComparer.Ordinal);
            private Action callback;
            internal uint Address; internal int Registrations; internal int Disposals;
            internal QueuedTraceApi AudioApi;
            internal Action AdvanceAction;
            internal void Set(string name, uint value)
            {
                if (name != "M68K D0" && name != "M68K D1"
                    && name != "M68K A7")
                    throw new ArgumentException(
                        "The fake accepts only production GPGX M68K register names.",
                        "name");
                registers[name] = value;
            }
            internal void Execute(uint address)
            {
                if (address != Address || callback == null)
                    throw new InvalidOperationException("No fixed callback is registered.");
                callback();
            }
            public int CompletedFrame { get { return 0; } }
            public bool IsLagged { get { return false; } }
            public int LagCount { get { return 0; } }
            public void ClearButtons() { }
            public void SetButton(string name, bool pressed) { }
            public IDisposable RegisterExecuteCallback(uint address, Action value)
            {
                Address = address; callback = value; Registrations++;
                return new Registration(this);
            }
            public void Advance()
            {
                if (AdvanceAction != null) AdvanceAction();
            }
            public byte ReadMainRamByte(int offset) { return 0; }
            public uint ReadCpuRegister(string name)
            {
                if (name != "M68K D0" && name != "M68K D1"
                    && name != "M68K A7")
                    throw new InvalidOperationException(
                        "The observer requested a non-production GPGX register name.");
                return registers[name];
            }
            public IGpgxAudioTraceApi CreateRequestCandidateAudioTraceApi()
            {
                if (AudioApi == null) throw new InvalidOperationException(
                    "The fake candidate host has no audio API.");
                return AudioApi;
            }
            public byte[] CaptureDriverState() { return new byte[0x2000]; }
            public void Dispose() { }
            private sealed class Registration : IDisposable
            {
                private readonly FakeHost host; private bool disposed;
                internal Registration(FakeHost value) { host = value; }
                public void Dispose() { if (!disposed) { disposed = true; host.Disposals++; } }
            }
        }
    }
}
