using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OpenGGF.BizHawk.Headless.Tests
{
    internal static class S3kSubmissionAudioRawV2Tests
    {
        public static void Register(ICollection<TestMain.TestCase> tests)
        {
            tests.Add(new TestMain.TestCase(
                "S3kSubmissionAudioRawV2Tests retain an intra-Advance mailbox submission",
                RetainsIntraAdvanceMailboxSubmission));
            tests.Add(new TestMain.TestCase(
                "S3kSubmissionAudioRawV2Tests remain explicitly unbound",
                RemainsExplicitlyUnbound));
            tests.Add(new TestMain.TestCase(
                "S3kSubmissionAudioRawV2Tests keep raw v1 unable to claim submissions",
                KeepsRawV1UnableToClaimSubmissions));
            tests.Add(new TestMain.TestCase(
                "S3kSubmissionAudioRawV2Tests never infer submissions from state or chips",
                NeverInfersSubmissionsFromStateOrChips));
        }

        private static void RetainsIntraAdvanceMailboxSubmission()
        {
            var api = new EventTraceApi();
            CompleteRunAudioObserver observer =
                S3kSubmissionAudioObserverProfile.CreateUnboundObserver(
                    ManifestPath(), api);
            var output = new StringWriter();
            var sink = new S3kSubmissionAudioRawV2Sink(
                new StateSource(), output,
                S3kSubmissionAudioObserverProfile.UnboundAuthorityForTesting);
            sink.Begin(observer.CaptureBoundaryFrontierAndResetPublication());
            api.Events = SameAdvanceEvents();
            CompleteRunAudioObserver.FrameCapture frame =
                observer.CaptureCanonicalFrame(0, () => { });
            sink.Frame(0, frame);
            sink.Complete(observer.CaptureCutoffFrontier());

            JObject rawFrame = JObject.Parse(output.ToString().Split(
                new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)[2]);
            AssertEx.Equal("openggf.s3k-complete-run-audio-raw.v2",
                (string)JObject.Parse(output.ToString().Split('\n')[0])["schema"]);
            JArray submissions = (JArray)rawFrame["submissions"];
            AssertEx.Equal(1, submissions.Count);
            AssertEx.Equal(0xFE, (int)submissions[0]["request"]);
            AssertEx.Equal(1, (int)submissions[0]["begin_ordinal"]);
            AssertEx.Equal(5, (int)submissions[0]["end_ordinal"]);
            AssertEx.Equal(0x1358, (int)submissions[0]["begin_pc"]);
            AssertEx.Equal(0x1374, (int)submissions[0]["end_pc"]);
            AssertEx.Equal("fe", (string)submissions[0]["mailbox_hex"]);
            JArray events = (JArray)rawFrame["events"];
            AssertEx.Equal(16, (int)events[16]["ordinal"]);
            AssertEx.Equal(12, (int)events[16]["service_kind"]);
        }

        private static void KeepsRawV1UnableToClaimSubmissions()
        {
            var api = new EventTraceApi();
            CompleteRunAudioObserver observer =
                S3kSubmissionAudioObserverProfile.CreateUnboundObserver(
                    ManifestPath(), api);
            CompleteRunAudioObserver.CutoffFrontier boundary =
                observer.CaptureBoundaryFrontierAndResetPublication();
            api.Events = SameAdvanceEvents();
            CompleteRunAudioObserver.FrameCapture frame =
                observer.CaptureCanonicalFrame(810, () => { });
            var output = new StringWriter();
            var sink = new S3kCompleteAudioRawSink(new StateSource(), output);
            sink.Begin(boundary);
            sink.Frame(810, frame);
            sink.Complete(observer.CaptureCutoffFrontier());

            JObject rawFrame = JObject.Parse(output.ToString().Split(
                new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)[2]);
            AssertEx.Equal(
                "{\"type\":\"metadata\",\"schema\":\"openggf.s3k-complete-run-audio-raw.v1\","
                + "\"rom_sha1\":\"cfbf98c36c776677290a872547ac47c53d2761d6\","
                + "\"bk2_sha256\":\"aa892856df22b7bb1fe5accb48db10b90dc26845d1dccee90352da30349f53cc\","
                + "\"service_manifest_sha256\":\"ef8f8103c38d70e41cb09cb29751f56815a0401709dc509071aa514d614813a0\","
                + "\"first_row\":810,\"exclusive_end\":434417,\"state_start\":7168,"
                + "\"state_exclusive_end\":8192}",
                output.ToString().Split('\n')[0]);
            AssertEx.Equal(S3kCompleteAudioRawSink.Schema,
                (string)JObject.Parse(output.ToString().Split('\n')[0])["schema"]);
            AssertEx.Equal(false, rawFrame.ContainsKey("submissions"));
        }

        private static void NeverInfersSubmissionsFromStateOrChips()
        {
            var api = new EventTraceApi();
            CompleteRunAudioObserver observer =
                S3kSubmissionAudioObserverProfile.CreateUnboundObserver(
                    ManifestPath(), api);
            var state = new byte[0x400];
            state[0] = 0xFE;
            var output = new StringWriter();
            var sink = new S3kSubmissionAudioRawV2Sink(
                new StateSource(state), output,
                S3kSubmissionAudioObserverProfile.UnboundAuthorityForTesting);
            sink.Begin(observer.CaptureBoundaryFrontierAndResetPublication());
            api.Events = NonSubmissionChipEvents();
            sink.Frame(0, observer.CaptureCanonicalFrame(0, () => { }));
            sink.Complete(observer.CaptureCutoffFrontier());

            JObject rawFrame = JObject.Parse(output.ToString().Split(
                new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)[2]);
            AssertEx.Equal(1, ((JArray)rawFrame["events"]).Count(value =>
                (int)value["kind"] == 4));
            AssertEx.Equal(0, ((JArray)rawFrame["submissions"]).Count);
        }

        private static void RemainsExplicitlyUnbound()
        {
            AssertEx.Equal(false,
                S3kSubmissionAudioObserverProfile.UnboundAuthorityForTesting.IsProductionBound);
            AssertEx.Equal(false, typeof(S3kCompleteAudioCaptureRunner)
                .GetMethods(System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic)
                .Any(value => value.Name.IndexOf("Submission",
                    StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static GpgxAudioTraceEvent[] SameAdvanceEvents()
        {
            return new[]
            {
                Event(0, 1, 1, 0, 8, 0, 1, 0x1150, 14),
                Event(1, 1, 2, 1, 13, 1, 2, 0x1358, 27),
                Event(2, 5, 2, 1, 13, 1, 2, 0x1374, 2),
                Event(3, 6, 2, 1, 13, 1, 2, 0x1374, 2,
                    offset: 0, length: 1, payload: 0xFE),
                Event(4, 7, 2, 1, 13, 1, 2, 0x1374, 2, offset: 1),
                Event(5, 2, 2, 1, 13, 1, 2, 0x1374, 28),
                Event(6, 5, 1, 0, 8, 0, 1, 0x1165, 1),
                Event(7, 6, 1, 0, 8, 0, 1, 0x1165, 1,
                    offset: 0, length: 1, payload: 0),
                Event(8, 7, 1, 0, 8, 0, 1, 0x1165, 1, offset: 1),
                Event(9, 2, 1, 0, 8, 0, 1, 0x1165, 16),
                Event(10, 1, 3, 0, 3, 0, 1, 56, 2),
                Event(11, 1, 4, 3, 11, 1, 1, 283, 23),
                Event(12, 5, 4, 3, 11, 1, 1, 289, 1),
                Event(13, 6, 4, 3, 11, 1, 1, 289, 1,
                    offset: 0, length: 1, payload: 0),
                Event(14, 7, 4, 3, 11, 1, 1, 289, 1, offset: 1),
                Event(15, 2, 4, 3, 11, 1, 1, 289, 24),
                Event(16, 1, 5, 3, 12, 1, 1, 289, 24),
                Event(17, 5, 5, 3, 12, 1, 1, 69, 1),
                Event(18, 6, 5, 3, 12, 1, 1, 69, 1,
                    offset: 0, length: 1, payload: 0),
                Event(19, 7, 5, 3, 12, 1, 1, 69, 1, offset: 1),
                Event(20, 2, 5, 3, 12, 1, 1, 69, 25),
                Event(21, 5, 3, 0, 3, 0, 1, 132, 1),
                Event(22, 6, 3, 0, 3, 0, 1, 132, 1,
                    offset: 0, length: 1, payload: 0),
                Event(23, 7, 3, 0, 3, 0, 1, 132, 1, offset: 1),
                Event(24, 2, 3, 0, 3, 0, 1, 132, 3)
            };
        }

        private static GpgxAudioTraceEvent[] NonSubmissionChipEvents()
        {
            return new[]
            {
                Event(0, 1, 1, 0, 8, 0, 1, 0x1150, 14),
                Event(1, 4, 1, 0, 8, 0, 1, 0x1160, 0, value: 0xFE),
                Event(2, 5, 1, 0, 8, 0, 1, 0x1165, 1),
                Event(3, 6, 1, 0, 8, 0, 1, 0x1165, 1,
                    offset: 0, length: 1, payload: 0),
                Event(4, 7, 1, 0, 8, 0, 1, 0x1165, 1, offset: 1),
                Event(5, 2, 1, 0, 8, 0, 1, 0x1165, 16)
            };
        }

        private static GpgxAudioTraceEvent Event(uint ordinal, byte kind,
            ushort token, ushort parent, byte serviceKind, byte depth,
            byte cpu, uint pc, ushort subject, byte value = 0,
            ushort offset = 0, byte length = 0, ulong payload = 0)
        {
            return new GpgxAudioTraceEvent
            {
                Ordinal = ordinal, Kind = kind, ServiceToken = token,
                ParentToken = parent, ServiceKindId = serviceKind,
                Depth = depth, SourceCpu = cpu, Pc = pc, Subject = subject,
                Value = value, Offset = offset, PayloadLength = length,
                Payload = payload
            };
        }

        private static string ManifestPath()
        {
            string binary = Path.GetDirectoryName(
                typeof(S3kSubmissionAudioRawV2Tests).Assembly.Location);
            return Path.GetFullPath(Path.Combine(binary, "..", "..",
                "fixtures/gpgx-audio-service-manifest-s3k-submission-v2.json"));
        }

        private sealed class StateSource : IS3kCompleteAudioStateSource
        {
            private readonly byte[] state;
            internal StateSource() : this(new byte[0x400]) { }
            internal StateSource(byte[] value) { state = (byte[])value.Clone(); }
            public bool IsLagged { get { return false; } }
            public byte[] CaptureDriverState() { return (byte[])state.Clone(); }
        }

        private sealed class EventTraceApi : IGpgxAudioTraceApi
        {
            internal GpgxAudioTraceEvent[] Events = new GpgxAudioTraceEvent[0];
            public uint AbiVersion { get { return 1; } }
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
            { fault = default(GpgxAudioObserverAdapter.FirstFault); return 0; }
            public int BeginPublicationEpoch() { return 0; }
            public int AbortFrame() { return 0; }
            public int Disable() { return 0; }
        }
    }
}
