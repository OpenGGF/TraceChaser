using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;

namespace OpenGGF.BizHawk.Headless
{
    internal interface IS3kCompleteAudioStateSource
    {
        bool IsLagged { get; }
        byte[] CaptureDriverState();
    }

    internal sealed class GpgxS3kCompleteAudioStateSource : IS3kCompleteAudioStateSource
    {
        private readonly GpgxHost host;

        internal GpgxS3kCompleteAudioStateSource(GpgxHost value)
        { host = value ?? throw new ArgumentNullException("value"); }

        public bool IsLagged { get { return host.IsLagged; } }

        public byte[] CaptureDriverState()
        {
            int start = S3kAudioObserverProfile.DriverStateStart;
            int end = S3kAudioObserverProfile.DriverStateExclusiveEnd;
            var bytes = new byte[end - start];
            for (int index = 0; index < bytes.Length; index++)
                bytes[index] = host.ReadZ80RamByte(start + index);
            return bytes;
        }
    }

    /// <summary>
    /// Lossless bounded staging stream. It carries no canonical-store or
    /// publication authority; the Java game adapter validates it separately.
    /// </summary>
    internal sealed class S3kCompleteAudioRawSink : IS3kCompleteAudioCaptureSink
    {
        internal const string Schema = "openggf.s3k-complete-run-audio-raw.v1";
        private readonly IS3kCompleteAudioStateSource state;
        private readonly TextWriter output;
        private readonly S3kRawAudioAuthority authority;
        private int lastRow = -1;
        private bool begun;
        private bool complete;

        internal S3kCompleteAudioRawSink(IS3kCompleteAudioStateSource stateSource,
            TextWriter writer)
            : this(stateSource, writer, S3kRawAudioAuthority.ProductionV1)
        {
        }

        internal S3kCompleteAudioRawSink(IS3kCompleteAudioStateSource stateSource,
            TextWriter writer, S3kRawAudioAuthority rawAuthority)
        {
            state = stateSource ?? throw new ArgumentNullException("stateSource");
            output = writer ?? throw new ArgumentNullException("writer");
            authority = rawAuthority ?? throw new ArgumentNullException("rawAuthority");
            if (!object.ReferenceEquals(authority, S3kRawAudioAuthority.ProductionV1)
                && !object.ReferenceEquals(authority,
                    S3kSubmissionAudioObserverProfile.UnboundAuthorityForTesting)
                && !object.ReferenceEquals(authority,
                    S3kPreconsumptionRequestRawAuthority.Instance))
                throw new ArgumentException(
                    "The S3K raw authority is not a closed production or test-only profile.",
                    "rawAuthority");
        }

        public void Begin(CompleteRunAudioObserver.CutoffFrontier boundary)
        {
            if (begun) throw new InvalidOperationException("The S3K raw epoch already began.");
            if (boundary == null) throw new ArgumentNullException("boundary");
            JObject metadata = object.ReferenceEquals(authority,
                S3kPreconsumptionRequestRawAuthority.Instance)
                ? new JObject
                {
                    ["type"]="metadata", ["schema"]=authority.Schema,
                    ["authority"]="S3K_SONIC_TAILS_REQUEST_DIAGNOSTIC",
                    ["rom_sha1"]=authority.RomSha1,
                    ["bk2_basename"]=S3kPreconsumptionRequestProfile.MovieBaseName,
                    ["bk2_sha256"]=authority.Bk2Sha256,
                    ["bk2_row_count"]=S3kPreconsumptionRequestProfile.MovieRowCount,
                    ["service_manifest_sha256"]=authority.ManifestSha256,
                    ["first_row"]=authority.FirstRow,
                    ["exclusive_end"]=authority.ExclusiveEnd,
                    ["state_start"]=authority.StateStart,
                    ["state_exclusive_end"]=authority.StateExclusiveEnd,
                    ["request_begin_pc"]=S3kPreconsumptionRequestProfile.BeginPc,
                    ["request_end_pc"]=S3kPreconsumptionRequestProfile.EndPc,
                    ["request_begin_opcode"]=S3kPreconsumptionRequestProfile.BeginOpcode,
                    ["request_end_opcode"]=S3kPreconsumptionRequestProfile.EndOpcode,
                    ["request_begin_token"]=S3kPreconsumptionRequestProfile.BeginToken,
                    ["request_end_token"]=S3kPreconsumptionRequestProfile.EndToken,
                    ["request_action"]="SNAPSHOT_AT_PC",
                    ["request_parent_independent"]=true,
                    ["request_mailbox_address"]=S3kPreconsumptionRequestProfile.MailboxAddress,
                    ["request_mailbox_range_id"]=S3kPreconsumptionRequestProfile.MailboxRangeId
                }
                : authority.IsProductionBound
                ? new JObject
                {
                    ["type"]="metadata", ["schema"]=Schema,
                    ["rom_sha1"]=S3kAudioObserverProfile.RomSha1,
                    ["bk2_sha256"]=S3kAudioObserverProfile.MovieSha256,
                    ["service_manifest_sha256"]=S3kAudioObserverProfile.ManifestSha256,
                    ["first_row"]=S3kAudioObserverProfile.FirstRow,
                    ["exclusive_end"]=S3kAudioObserverProfile.ExclusiveEnd,
                    ["state_start"]=S3kAudioObserverProfile.DriverStateStart,
                    ["state_exclusive_end"]=S3kAudioObserverProfile.DriverStateExclusiveEnd
                }
                : new JObject
                {
                    ["type"]="metadata", ["schema"]=authority.Schema,
                    ["authority"]="UNBOUND_TEST_ONLY",
                    ["rom_sha1"]=authority.RomSha1,
                    ["service_manifest_sha256"]=authority.ManifestSha256,
                    ["first_row"]=authority.FirstRow,
                    ["exclusive_end"]=authority.ExclusiveEnd,
                    ["state_start"]=authority.StateStart,
                    ["state_exclusive_end"]=authority.StateExclusiveEnd
                };
            Write(metadata);
            JObject baseline = Boundary("baseline", boundary);
            baseline["row"] = authority.FirstRow;
            Write(baseline);
            begun = true;
        }

        public void Frame(int row, CompleteRunAudioObserver.FrameCapture frame)
        {
            RequireActive();
            if (frame == null) throw new ArgumentNullException("frame");
            int expected = lastRow < 0 ? authority.FirstRow : lastRow + 1;
            if (row != expected || row >= authority.ExclusiveEnd)
                throw new InvalidDataException("The S3K raw rows are not contiguous and in range.");
            var requests = new JArray();
            foreach (CompleteRunAudioObserver.RequestObservation value
                in frame.RequestObservations)
            {
                var bytes = new byte[value.Bytes.Count];
                value.Bytes.CopyTo(bytes, 0);
                requests.Add(new JObject
                {
                    ["hook_token"]=value.HookToken, ["pc"]=value.Pc,
                    ["source_cpu"]=value.SourceCpu,
                    ["native_ordinal"]=value.NativeOrdinal,
                    ["active_service_token"]=value.ServiceToken,
                    ["active_service_kind"]=value.ServiceKind,
                    ["active_depth"]=value.Depth,
                    ["range_id"]=value.RangeId,
                    ["bytes_hex"]=Hex(bytes)
                });
            }
            var events = new JArray();
            foreach (GpgxAudioTraceEvent value in frame.RawEvents)
            {
                events.Add(new JObject
                {
                    ["ordinal"]=value.Ordinal, ["service_token"]=value.ServiceToken,
                    ["parent_token"]=value.ParentToken, ["pc"]=value.Pc,
                    ["subject"]=value.Subject, ["offset"]=value.Offset,
                    ["kind"]=value.Kind, ["service_kind"]=value.ServiceKindId,
                    ["depth"]=value.Depth, ["source_cpu"]=value.SourceCpu,
                    ["payload_length"]=value.PayloadLength, ["value"]=value.Value,
                    ["flags"]=value.Flags, ["reserved"]=value.Reserved,
                    ["payload"]=value.Payload.ToString(CultureInfo.InvariantCulture)
                });
            }
            var frameValue = new JObject
            {
                ["type"]="frame", ["row"]=row, ["lag"]=state.IsLagged,
                ["state_hex"]=StateHex(), ["events"]=events
            };
            if (authority.IncludeSubmissions)
                frameValue["submissions"] = Submissions(frame);
            if (requests.Count != 0) frameValue["requests"] = requests;
            Write(frameValue);
            lastRow = row;
        }

        public void Complete(CompleteRunAudioObserver.CutoffFrontier cutoff)
        {
            RequireActive();
            if (cutoff == null) throw new ArgumentNullException("cutoff");
            JObject value = Boundary("cutoff", cutoff);
            value["exclusive_end"] = lastRow < 0
                ? authority.FirstRow : lastRow + 1;
            Write(value);
            complete = true;
        }

        private JObject Boundary(string type, CompleteRunAudioObserver.CutoffFrontier frontier)
        {
            var active = new JArray();
            foreach (CompleteRunAudioObserver.DriverService service in frontier.ActiveServices)
                active.Add(Service(service));
            var pending = new JArray();
            foreach (CompleteRunAudioObserver.DriverService service in frontier.PendingServices)
                pending.Add(Service(service));
            return new JObject
            {
                ["type"]=type, ["state_hex"]=StateHex(),
                ["ym_port0_latch"]=frontier.YmPort0Address,
                ["ym_port1_latch"]=frontier.YmPort1Address,
                ["native_arm_epoch"]=frontier.ArmEpoch,
                ["native_armed"]=frontier.IsArmed,
                ["active_services"]=active, ["pending_descendants"]=pending
            };
        }

        private static JObject Service(CompleteRunAudioObserver.DriverService service)
        {
            var chips = new JArray();
            foreach (CompleteRunAudioObserver.OwnedChipEvent chip in service.OwnedChipEvents)
            {
                chips.Add(new JObject
                {
                    ["coordinate"]=chip.Coordinate, ["native_ordinal"]=chip.NativeOrdinal,
                    ["event_kind"]=chip.EventKind, ["subject"]=chip.Subject,
                    ["value"]=chip.Value, ["pc"]=chip.Pc,
                    ["source_cpu"]=chip.SourceCpu, ["data"]=chip.IsData,
                    ["port"]=chip.Port, ["register"]=chip.Register
                });
            }
            var snapshots = new JArray();
            foreach (CompleteRunAudioObserver.SnapshotGroup snapshot in service.Snapshots)
            {
                snapshots.Add(new JObject
                {
                    ["range_id"]=snapshot.RangeId, ["source_cpu"]=snapshot.SourceCpu,
                    ["pc"]=snapshot.Pc, ["bytes_hex"]=Hex(snapshot.Bytes)
                });
            }
            var ancestry = new JArray();
            foreach (CompleteRunAudioObserver.AncestryTransition transition in service.AncestryTransitions)
            {
                ancestry.Add(new JObject
                {
                    ["coordinate"]=transition.Coordinate,
                    ["native_ordinal"]=transition.NativeOrdinal,
                    ["previous_parent_token"]=transition.PreviousParentToken,
                    ["previous_depth"]=transition.PreviousDepth,
                    ["current_parent_token"]=transition.CurrentParentToken,
                    ["current_depth"]=transition.CurrentDepth,
                    ["hook_token"]=transition.HookToken,
                    ["source_cpu"]=transition.SourceCpu, ["pc"]=transition.Pc
                });
            }
            return new JObject
            {
                ["token"]=service.Token, ["parent_token"]=service.ParentToken,
                ["kind"]=service.Kind, ["depth"]=service.Depth,
                ["current_parent_token"]=service.CurrentParentToken,
                ["current_depth"]=service.CurrentDepth,
                ["begin_coordinate"]=service.BeginCoordinate,
                ["end_coordinate"]=service.EndCoordinate,
                ["begin_pc"]=service.BeginPc, ["end_pc"]=service.EndPc,
                ["begin_hook_token"]=service.BeginHookToken,
                ["end_hook_token"]=service.EndHookToken,
                ["begin_source_cpu"]=service.BeginSourceCpu,
                ["cancelled"]=service.Cancelled, ["complete"]=service.IsComplete,
                ["chips"]=chips, ["snapshots"]=snapshots,
                ["ancestry_transitions"]=ancestry
            };
        }

        private string StateHex()
        {
            byte[] bytes = state.CaptureDriverState();
            int expected = authority.StateExclusiveEnd-authority.StateStart;
            if (bytes == null || bytes.Length != expected)
                throw new InvalidDataException("The S3K raw state snapshot is not exactly $1C00..$1FFF.");
            return Hex(bytes);
        }

        private JArray Submissions(CompleteRunAudioObserver.FrameCapture frame)
        {
            var result = new JArray();
            foreach (CompleteRunAudioObserver.DriverService service in frame.Services)
            {
                if (service.Kind != 13) continue;
                if (!service.IsComplete || service.Cancelled
                    || service.BeginPc != 0x1358 || service.EndPc != authority.SubmissionEndPc
                    || service.BeginHookToken != 27 || service.EndHookToken != 28
                    || service.BeginSourceCpu != 2 || service.Snapshots.Count != 1)
                    throw new InvalidDataException(
                        "The unbound S3K submission service is not the exact Play_Music boundary.");
                CompleteRunAudioObserver.SnapshotGroup snapshot = service.Snapshots[0];
                if (snapshot.RangeId != authority.MailboxRangeId
                    || snapshot.SourceCpu != 2
                    || snapshot.Pc != authority.SubmissionEndPc
                    || snapshot.Bytes.Length != 1)
                    throw new InvalidDataException(
                        "The unbound S3K submission mailbox snapshot is not exact.");
                GpgxAudioTraceEvent completion = default(GpgxAudioTraceEvent);
                bool completionSeen = false;
                foreach (GpgxAudioTraceEvent native in frame.RawEvents)
                {
                    if (native.Kind == 2 && native.ServiceToken == service.Token)
                    { completion = native; completionSeen = true; }
                }
                if (!completionSeen || completion.Ordinal <= service.BeginNativeOrdinal)
                    throw new InvalidDataException(
                        "The unbound S3K submission native ordering is not exact.");
                result.Add(new JObject
                {
                    ["service_token"]=service.Token,
                    ["parent_token"]=service.ParentToken,
                    ["begin_ordinal"]=service.BeginNativeOrdinal,
                    ["end_ordinal"]=completion.Ordinal,
                    ["begin_pc"]=service.BeginPc,
                    ["end_pc"]=service.EndPc,
                    ["begin_hook_token"]=service.BeginHookToken,
                    ["end_hook_token"]=service.EndHookToken,
                    ["mailbox_hex"]=Hex(snapshot.Bytes),
                    ["request"]=snapshot.Bytes[0]
                });
            }
            return result;
        }

        private static string Hex(byte[] bytes)
        {
            char[] value = new char[bytes.Length * 2];
            const string alphabet = "0123456789abcdef";
            for (int index = 0; index < bytes.Length; index++)
            {
                value[index * 2] = alphabet[bytes[index] >> 4];
                value[index * 2 + 1] = alphabet[bytes[index] & 15];
            }
            return new string(value);
        }

        private void Write(JObject value)
        {
            output.Write(value.ToString(Newtonsoft.Json.Formatting.None));
            output.Write('\n');
        }

        private void RequireActive()
        {
            if (!begun || complete)
                throw new InvalidOperationException("The S3K raw epoch is not active.");
        }
    }

    internal sealed class S3kRawAudioAuthority
    {
        internal static readonly S3kRawAudioAuthority ProductionV1 =
            new S3kRawAudioAuthority(S3kCompleteAudioRawSink.Schema,
                S3kAudioObserverProfile.RomSha1,
                S3kAudioObserverProfile.MovieSha256,
                S3kAudioObserverProfile.ManifestSha256,
                S3kAudioObserverProfile.FirstRow,
                S3kAudioObserverProfile.ExclusiveEnd,
                S3kAudioObserverProfile.DriverStateStart,
                S3kAudioObserverProfile.DriverStateExclusiveEnd,
                true, false);

        internal S3kRawAudioAuthority(string schema, string romSha1,
            string bk2Sha256, string manifestSha256, int firstRow,
            int exclusiveEnd, int stateStart, int stateExclusiveEnd,
            bool productionBound, bool includeSubmissions)
        {
            Schema=schema;RomSha1=romSha1;Bk2Sha256=bk2Sha256;
            ManifestSha256=manifestSha256;FirstRow=firstRow;
            ExclusiveEnd=exclusiveEnd;StateStart=stateStart;
            StateExclusiveEnd=stateExclusiveEnd;
            IsProductionBound=productionBound;IncludeSubmissions=includeSubmissions;
            MailboxRangeId=2;SubmissionEndPc=0x1374;
        }

        internal string Schema{get;private set;}
        internal string RomSha1{get;private set;}
        internal string Bk2Sha256{get;private set;}
        internal string ManifestSha256{get;private set;}
        internal int FirstRow{get;private set;}
        internal int ExclusiveEnd{get;private set;}
        internal int StateStart{get;private set;}
        internal int StateExclusiveEnd{get;private set;}
        internal bool IsProductionBound{get;private set;}
        internal bool IncludeSubmissions{get;set;}
        internal ushort MailboxRangeId{get;set;}
        internal uint SubmissionEndPc{get;set;}
    }

    internal sealed class S3kSubmissionAudioRawV2Sink : IS3kCompleteAudioCaptureSink
    {
        private readonly S3kCompleteAudioRawSink inner;
        internal S3kSubmissionAudioRawV2Sink(IS3kCompleteAudioStateSource state,
            TextWriter output, S3kRawAudioAuthority authority)
        {
            if (!object.ReferenceEquals(authority,
                    S3kSubmissionAudioObserverProfile.UnboundAuthorityForTesting)
                || authority.IsProductionBound
                || !authority.IncludeSubmissions)
                throw new ArgumentException(
                    "The S3K raw-v2 sink requires explicit unbound submission authority.",
                    "authority");
            inner = new S3kCompleteAudioRawSink(state, output, authority);
        }
        public void Begin(CompleteRunAudioObserver.CutoffFrontier boundary)
        { inner.Begin(boundary); }
        public void Frame(int row, CompleteRunAudioObserver.FrameCapture frame)
        { inner.Frame(row, frame); }
        public void Complete(CompleteRunAudioObserver.CutoffFrontier cutoff)
        { inner.Complete(cutoff); }
    }
}
