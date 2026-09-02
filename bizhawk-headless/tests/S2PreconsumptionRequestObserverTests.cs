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
                "S2PreconsumptionRequestObserverTests make the runner correlate the callback before the row is published",
                RunnerCorrelatesCallbackBeforeRowPublication));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject a non-marker record before the exact marker",
                RejectsNonMarkerBeforeExactMarker));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests reject a marker with the wrong source or root owner",
                RejectsWrongSourceOrOwner));
            tests.Add(new TestMain.TestCase(
                "S2PreconsumptionRequestObserverTests observe from row zero and publish only comparison-boundary transfers",
                ObservesFromRowZeroAndPublishesAtBoundary));
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
                    observer.CorrelateRow(769, new[] { Marker(0x00FF1020, 17) });

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
                    observer.CorrelateRow(769, new[] { Marker(0x12345679, 1) }),
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

        private static void RunnerCorrelatesCallbackBeforeRowPublication()
        {
            var host = new FakeHost();
            host.Set("D0", 0xCE); host.Set("D1", 2); host.Set("A7", 0x00FF1000);
            IReadOnlyList<S2PreconsumptionRequestObserver.Transfer> transfers =
                S2CompleteAudioCaptureRunner.CaptureRequestV3RowForTesting(
                    Fixture("gpgx-audio-service-manifest-s2-request-v3.json"),
                    host, S2AudioObserverProfile.FirstRow,
                    () => host.Execute(S2PreconsumptionRequestObserver.Pc),
                    Frame(S2AudioObserverProfile.FirstRow,
                        Marker(0x00FF1000, 22)));
            AssertEx.Equal(1, transfers.Count);
            AssertEx.Equal((byte)0xCE, transfers[0].Request);
            AssertEx.Equal((ushort)2, transfers[0].Slot);
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
            AssertEx.Throws<InvalidOperationException>(() => observer.CorrelateRow(
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
            AssertEx.Throws<InvalidOperationException>(() => observer.CorrelateRow(
                769, new[] { wrong }), "source/owner");
            AssertEx.Throws<InvalidOperationException>(() => observer.Dispose(),
                "unmatched");
        }

        private static void ObservesFromRowZeroAndPublishesAtBoundary()
        {
            var host = new FakeHost();
            host.Set("D0", 0xB5); host.Set("D1", 0); host.Set("A7", 1);
            var rows = new List<S2CompleteAudioCaptureRunner.RequestV3Row>();
            for (int row = 0; row <= S2AudioObserverProfile.FirstRow; row++)
                rows.Add(new S2CompleteAudioCaptureRunner.RequestV3Row(row,
                    () => host.Execute(S2PreconsumptionRequestObserver.Pc),
                    Frame(row, Marker(1, (uint)(row + 1)))));
            IReadOnlyList<S2PreconsumptionRequestObserver.Transfer> transfers =
                S2CompleteAudioCaptureRunner.CaptureRequestV3RowsForTesting(
                    Fixture("gpgx-audio-service-manifest-s2-request-v3.json"),
                    host, rows);
            AssertEx.Equal(1, transfers.Count);
            AssertEx.Equal(S2AudioObserverProfile.FirstRow, transfers[0].Row);
            AssertEx.Equal(1, host.Registrations);
            AssertEx.Equal(1, host.Disposals);
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
