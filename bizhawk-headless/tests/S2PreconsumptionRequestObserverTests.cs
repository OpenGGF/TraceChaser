using System;
using System.Collections.Generic;
using System.IO;

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
                "S2PreconsumptionRequestObserverTests reject malformed request transfer correlation",
                RejectsMalformedCorrelation));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests pin the unbound fixed request manifest without changing v2 authority",
                PinsUnboundFixedManifestWithoutChangingV2Authority));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests make the session own the callback advance drain and correlation",
                SessionOwnsCallbackAdvanceDrainAndCorrelation));
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
        }

        private static void RetainsFixedAcceptedTransferUntilExactMarker()
        {
            var host = new FakeHost();
            host.Set("D0", 0x000000B5); host.Set("D1", 3); host.Set("A7", 0x00FF1020);
            using (var observer = new S2PreconsumptionRequestObserver(host))
            {
                observer.BeginRow(769);
                host.Execute(S2PreconsumptionRequestObserver.Pc);
                IReadOnlyList<S2PreconsumptionRequestObserver.Transfer> transfers =
                    observer.CompleteOwnedRow(769, new[] { Marker(0x00FF1020, 17) });

                AssertEx.Equal(1, transfers.Count);
                AssertEx.Equal(769, transfers[0].Row);
                AssertEx.Equal((byte)0xB5, transfers[0].Request);
                AssertEx.Equal((ushort)3, transfers[0].Slot);
                AssertEx.Equal(0x0010D6u, transfers[0].Pc);
                AssertEx.Equal(0x00FF1020u, transfers[0].A7);
                AssertEx.Equal(17u, transfers[0].NativeOrdinal);
            }
            AssertEx.Equal(1, host.Registrations);
            AssertEx.Equal(1, host.Disposals);
            AssertEx.Equal(S2PreconsumptionRequestObserver.Pc, host.Address);
        }

        private static void RejectsMalformedCorrelation()
        {
            var host = new FakeHost();
            host.Set("D0", 1); host.Set("D1", 0); host.Set("A7", 0x12345678);
            using (var observer = new S2PreconsumptionRequestObserver(host))
            {
                observer.BeginRow(769);
                host.Execute(S2PreconsumptionRequestObserver.Pc);
                AssertEx.Throws<InvalidOperationException>(() =>
                    observer.CompleteOwnedRow(769, new[] { Marker(0x12345679, 1) }),
                    "A7");
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
            AssertEx.Equal(false, candidate.ProductionBound);
            AssertEx.Throws<InvalidOperationException>(() =>
                candidate.RequireProductionAuthority(), "unbound");
            AssertEx.Equal(
                "ef8f8103c38d70e41cb09cb29751f56815a0401709dc509071aa514d614813a0",
                S2AudioObserverProfile.ServiceManifestSha256);
        }

        private static void SessionOwnsCallbackAdvanceDrainAndCorrelation()
        {
            var host = new FakeHost();
            host.Set("D0", 0xCE); host.Set("D1", 2); host.Set("A7", 0x00FF1000);
            var api = new QueuedTraceApi();
            var session = S2CompleteAudioCaptureRunner.OpenRequestCandidateSession(
                Fixture("gpgx-audio-service-manifest-s2-request-v3.json"), host,
                CreateAudioObserver(api));
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

        private static void RejectsNonMarkerBeforeExactMarker()
        {
            var host = new FakeHost();
            host.Set("D0", 1); host.Set("D1", 0); host.Set("A7", 0x12);
            var observer = new S2PreconsumptionRequestObserver(host);
            observer.BeginRow(769);
            host.Execute(S2PreconsumptionRequestObserver.Pc);
            GpgxAudioTraceEvent wrong = Marker(0x12, 1);
            wrong.Kind = 2;
            AssertEx.Throws<InvalidOperationException>(() => observer.CompleteOwnedRow(
                769, new[] { wrong, Marker(0x12, 2) }), "next record");
            AssertEx.Throws<InvalidOperationException>(() => observer.Dispose(),
                "unmatched");
        }

        private static void RejectsWrongSourceOrOwner()
        {
            var host = new FakeHost();
            host.Set("D0", 1); host.Set("D1", 0); host.Set("A7", 0x12);
            var observer = new S2PreconsumptionRequestObserver(host);
            observer.BeginRow(769);
            host.Execute(S2PreconsumptionRequestObserver.Pc);
            GpgxAudioTraceEvent wrong = Marker(0x12, 1);
            wrong.SourceCpu = 1; wrong.ServiceToken = 9;
            wrong.ServiceKindId = 3; wrong.Depth = 1;
            AssertEx.Throws<InvalidOperationException>(() => observer.CompleteOwnedRow(
                769, new[] { wrong }), "source/owner");
            AssertEx.Throws<InvalidOperationException>(() => observer.Dispose(),
                "unmatched");
        }

        private static void ObservesFromRowZeroAndPublishesAtBoundary()
        {
            var host = new FakeHost();
            host.Set("D0", 0xB5); host.Set("D1", 0); host.Set("A7", 1);
            var api = new QueuedTraceApi();
            var session = S2CompleteAudioCaptureRunner.OpenRequestCandidateSession(
                Fixture("gpgx-audio-service-manifest-s2-request-v3.json"), host,
                CreateAudioObserver(api));
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
            host.Set("D0", request); host.Set("D1", slot); host.Set("A7", 0x10);
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
                    host.Set("A7", 0x20);
                    api.Events = new[] { Marker(0x10, 0) };
                    host.Execute(S2PreconsumptionRequestObserver.Pc);
                    api.Events = new[] { Marker(0x20, 0), Marker(0x10, 1) };
                }), "A7 differs");
            }
        }

        private static void RejectsTerminalEvidenceWhileCallbackPending()
        {
            var host = RequestHost(1, 0, 0x10);
            var observer = new S2PreconsumptionRequestObserver(host);
            observer.BeginRow(0);
            host.Execute(S2PreconsumptionRequestObserver.Pc);
            AssertEx.Throws<InvalidOperationException>(() => observer.CompleteOwnedRow(0,
                new[] { new GpgxAudioTraceEvent { Kind = 2 } }),
                "terminal boundary");
            AssertEx.Throws<InvalidOperationException>(() => observer.Dispose(),
                "unmatched");
        }

        private static S2CompleteAudioCaptureRunner.RequestCandidateSession
            OpenSession(FakeHost host, QueuedTraceApi api)
        {
            return S2CompleteAudioCaptureRunner.OpenRequestCandidateSession(
                Fixture("gpgx-audio-service-manifest-s2-request-v3.json"), host,
                CreateAudioObserver(api));
        }

        private static void DisposeIncompleteSession(
            S2CompleteAudioCaptureRunner.RequestCandidateSession session)
        {
            AssertEx.Throws<InvalidDataException>(() => session.Dispose(),
                "full power-on interval");
        }

        private static FakeHost RequestHost(byte request, ushort slot, uint a7)
        {
            var host = new FakeHost();
            host.Set("D0", request); host.Set("D1", slot); host.Set("A7", a7);
            return host;
        }

        private static GpgxAudioTraceEvent Ordinary(uint ordinal)
        {
            return new GpgxAudioTraceEvent
            {
                Kind = 10, Value = 3, Pc = 0x002000, Subject = 25,
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
                HookCount = 2
            };
            var hooks = new[]
            {
                new GpgxAudioObserverAdapter.ServiceHook
                {
                    HookToken = S2PreconsumptionRequestObserver.MarkerToken,
                    Action = 7, Cpu = 2,
                    Pc = S2PreconsumptionRequestObserver.Pc
                },
                new GpgxAudioObserverAdapter.ServiceHook
                {
                    HookToken = 25, Action = 7, Cpu = 2, Pc = 0x002000
                }
            };
            return new CompleteRunAudioObserver(api, config, new byte[8192],
                new GpgxAudioObserverAdapter.ServiceKind[0], hooks,
                new GpgxAudioObserverAdapter.SnapshotRange[0]);
        }

        private sealed class QueuedTraceApi : IGpgxAudioTraceApi
        {
            internal GpgxAudioTraceEvent[] Events = new GpgxAudioTraceEvent[0];
            public uint AbiVersion { get { return 4; } }
            public uint EventSize { get { return 32; } }
            public uint Capacity { get { return 65536; } }
            public int Configure(ref GpgxAudioObserverAdapter.Config config,
                byte[] mask, GpgxAudioObserverAdapter.ServiceKind[] kinds,
                GpgxAudioObserverAdapter.ServiceHook[] hooks,
                GpgxAudioObserverAdapter.SnapshotRange[] ranges) { return 0; }
            public int BeginFrame() { return 0; }
            public int EndFrame() { return 0; }
            public int EventCount(out uint count, out uint overflow)
            { count = (uint)Events.Length; overflow = 0; return 0; }
            public int Drain(GpgxAudioTraceEvent[] events, uint capacity,
                out uint count)
            {
                count = (uint)Events.Length;
                if (events != null) Array.Copy(Events, events, Events.Length);
                return 0;
            }
            public int GetFirstFault(out GpgxAudioObserverAdapter.FirstFault fault)
            { fault = new GpgxAudioObserverAdapter.FirstFault(); return 0; }
            public int BeginPublicationEpoch() { return 0; }
            public int AbortFrame() { return 0; }
            public int Disable() { return 0; }
        }

        private sealed class FakeHost : IGpgxHost, ICpuRegisterReader
        {
            private readonly Dictionary<string,uint> registers =
                new Dictionary<string,uint>(StringComparer.Ordinal);
            private Action callback;
            internal uint Address; internal int Registrations; internal int Disposals;
            internal void Set(string name, uint value) { registers[name] = value; }
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
            public void Advance() { }
            public byte ReadMainRamByte(int offset) { return 0; }
            public uint ReadCpuRegister(string name) { return registers[name]; }
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
