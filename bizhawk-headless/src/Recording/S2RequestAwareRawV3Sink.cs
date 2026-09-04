using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;

namespace OpenGGF.BizHawk.Headless
{
    internal static partial class S2CompleteAudioCaptureRunner
    {
      internal sealed partial class RequestAwareRawV3Candidate
      {
        /// <summary>
        /// Unbound strict v3 extension of the complete v2 raw envelope. It is
        /// private to the closed producer, so no caller can supply an
        /// independently-built frame or transfer list.
        /// </summary>
        private sealed class RawV3Sink
        {
        internal const string Schema = "openggf.s2-complete-run-audio-raw.v4";
        private readonly StringWriter v2Output = new StringWriter(CultureInfo.InvariantCulture);
        private readonly S2CompleteAudioRawSink v2;
        private readonly TextWriter output;
        private readonly int firstRow;
        private readonly int exclusiveEnd;
        private int lastRow = -1;
        private long nextTransferOrdinal;
        private bool begun, complete;

        internal RawV3Sink(IS2CompleteAudioStateSource state, TextWriter writer,
            int sourceFirstRow, int sourceExclusiveEnd)
            : this(state, writer, sourceFirstRow, sourceExclusiveEnd,
                S2AudioObserverProfile.MovieSha256)
        { }

        internal RawV3Sink(IS2CompleteAudioStateSource state, TextWriter writer,
            int sourceFirstRow, int sourceExclusiveEnd, string recordingSha256)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (sourceFirstRow < 0 || sourceExclusiveEnd <= sourceFirstRow)
                throw new ArgumentOutOfRangeException("sourceFirstRow");
            output = writer ?? throw new ArgumentNullException("writer");
            firstRow = sourceFirstRow;
            exclusiveEnd = sourceExclusiveEnd;
            v2 = new S2CompleteAudioRawSink(state, v2Output, firstRow,
                exclusiveEnd, recordingSha256);
        }

        internal RawV3Sink(IS2CompleteAudioStateSource state, TextWriter writer)
            : this(state,writer,S2AudioObserverProfile.FirstRow,
                S2AudioObserverProfile.ExclusiveEnd)
        { }

        internal void Begin(CompleteRunAudioObserver.CutoffFrontier boundary)
        {
            if (begun) throw new InvalidOperationException("The unbound S2 raw-v3 epoch already began.");
            v2.Begin(boundary); Flush(true, null); begun = true;
        }

        internal void Frame(int row, CompleteRunAudioObserver.FrameCapture frame,
            OverrideResumeDiagnosticAudio.Packet audio,
            IReadOnlyList<S2PreconsumptionRequestObserver.Transfer> transfers)
        {
            if (!begun || complete) throw new InvalidOperationException("The unbound S2 raw-v3 epoch is not active.");
            if (frame == null || transfers == null) throw new ArgumentNullException(frame == null ? "frame" : "transfers");
            if (frame.Bk2Row != row || row != (lastRow < 0 ? firstRow : lastRow + 1) || row >= exclusiveEnd)
                throw new InvalidDataException("The S2 raw-v3 rows are not contiguous.");
            JArray values = Transfers(row, transfers);
            v2.Frame(row, frame, audio);
            Flush(false, value => value["request_transfers"] = values);
            lastRow = row;
        }

        internal void Complete(CompleteRunAudioObserver.CutoffFrontier cutoff)
        {
            if (!begun || complete) throw new InvalidOperationException("The unbound S2 raw-v3 epoch is not active.");
            if (cutoff == null) throw new ArgumentNullException("cutoff");
            if (lastRow != exclusiveEnd - 1)
                throw new InvalidDataException("The S2 raw-v3 candidate rejects an early or empty cutoff.");
            v2.CompleteRequestAwareInventoryOnly(cutoff);
            Flush(false, null); complete = true;
        }

        private JArray Transfers(int row, IReadOnlyList<S2PreconsumptionRequestObserver.Transfer> transfers)
        {
            // One pass of sndDriverInput can transfer one music request and
            // four SFX ones, because .doSFX loads moveq #4-1,d1 on the shipped
            // fixBugs = 0 path (docs/s2disasm/s2.asm:1310-1315).
            if (transfers.Count > 5) throw new InvalidDataException("The S2 raw-v3 transfer count exceeds the fixed slot bound.");
            var values = new JArray();
            uint previousNativeOrdinal = 0;
            bool hasPreviousNativeOrdinal = false;
            for (int index = 0; index < transfers.Count; index++)
            {
                S2PreconsumptionRequestObserver.Transfer transfer = transfers[index];
                if (transfer == null || transfer.Row != row || transfer.Request == 0
                    || transfer.SourceCpu != S2PreconsumptionRequestObserver.MarkerSourceCpu)
                    throw new InvalidDataException("The S2 raw-v3 has an invalid request transfer.");
                // Each site keeps its own guarantees. The SFX store is
                // marker-correlated, so its records still carry a queue slot,
                // its own PC, a strictly increasing native ordinal and a
                // reviewed marker owner. The music store at loc_10C0
                // (:1302-1304) emits no native marker, so its records carry
                // the reserved slot, that PC, and no correlation at all.
                if (transfer.Site == S2PreconsumptionRequestObserver.TransferSite.Music)
                {
                    if (transfer.Slot != S2PreconsumptionRequestObserver.MusicSlot
                        || transfer.Pc != S2PreconsumptionRequestObserver.MusicPc
                        || transfer.NativeOrdinal != 0 || transfer.ServiceToken != 0
                        || transfer.ServiceKind != 0 || transfer.Depth != 0)
                        throw new InvalidDataException("The S2 raw-v3 has an invalid music request transfer.");
                }
                else
                {
                    if (transfer.Slot > 3 || transfer.Pc != S2PreconsumptionRequestObserver.Pc
                        || (hasPreviousNativeOrdinal && transfer.NativeOrdinal <= previousNativeOrdinal)
                        || !HasReviewedMarkerOwner(transfer))
                        throw new InvalidDataException("The S2 raw-v3 has an invalid request transfer.");
                    previousNativeOrdinal = transfer.NativeOrdinal;
                    hasPreviousNativeOrdinal = true;
                }
                values.Add(new JObject { ["row"] = row, ["order"] = index, ["global_transfer_ordinal"] = nextTransferOrdinal++, ["site"] = transfer.Site == S2PreconsumptionRequestObserver.TransferSite.Music ? "music" : "sfx", ["request"] = transfer.Request, ["slot"] = transfer.Slot, ["pc"] = transfer.Pc, ["a7"] = transfer.A7.ToString(CultureInfo.InvariantCulture), ["native_ordinal"] = transfer.NativeOrdinal, ["source_cpu"] = transfer.SourceCpu, ["service_token"] = transfer.ServiceToken, ["service_kind"] = transfer.ServiceKind, ["depth"] = transfer.Depth, ["active_service_owner"] = new JObject { ["token"] = transfer.ServiceToken, ["kind"] = transfer.ServiceKind, ["depth"] = transfer.Depth } });
            }
            return values;
        }

        /// <summary>
        /// The two reviewed owners a transfer may carry: the root boundary, and
        /// a transfer nested under a kind-3 VInt service. Anything else is not
        /// a topology this candidate has reviewed.
        /// </summary>
        private static bool HasReviewedMarkerOwner(
            S2PreconsumptionRequestObserver.Transfer transfer)
        {
            bool root = transfer.ServiceToken
                    == S2PreconsumptionRequestObserver.MarkerServiceToken
                && transfer.ServiceKind
                    == S2PreconsumptionRequestObserver.MarkerServiceKind
                && transfer.Depth == S2PreconsumptionRequestObserver.MarkerDepth;
            bool kind3 = transfer.ServiceToken
                    != S2PreconsumptionRequestObserver.MarkerServiceToken
                && transfer.ServiceKind
                    == S2PreconsumptionRequestObserver.Kind3MarkerServiceKind
                && (transfer.Depth
                        == S2PreconsumptionRequestObserver.MarkerDepth
                    || transfer.Depth == 1);
            return root || kind3;
        }

        private void Flush(bool metadata, Action<JObject> framePatch)
        {
            string[] lines = v2Output.ToString().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            v2Output.GetStringBuilder().Length = 0;
            for (int index = 0; index < lines.Length; index++)
            {
                JObject value = JObject.Parse(lines[index]);
                if (metadata && (string)value["type"] == "metadata")
                { value["schema"] = Schema; value["production_bound"] = false; value["request_manifest_schema"] = "openggf.s2-preconsumption-request-manifest.v1"; }
                if (framePatch != null && (string)value["type"] == "frame") framePatch(value);
                output.Write(value.ToString(Newtonsoft.Json.Formatting.None)); output.Write('\n');
            }
        }
        }
      }
    }
}
