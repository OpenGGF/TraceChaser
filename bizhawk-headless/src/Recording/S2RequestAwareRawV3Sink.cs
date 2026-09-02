using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>Unbound strict v3 extension of the complete v2 raw envelope.</summary>
    internal sealed class S2RequestAwareRawV3Sink
    {
        internal const string Schema = "openggf.s2-complete-run-audio-raw.v3";
        private readonly StringWriter v2Output = new StringWriter(CultureInfo.InvariantCulture);
        private readonly S2CompleteAudioRawSink v2;
        private readonly TextWriter output;
        private int lastRow = -1;
        private uint lastNativeOrdinal;
        private bool begun, complete;

        internal S2RequestAwareRawV3Sink(IS2CompleteAudioStateSource state, TextWriter writer)
        {
            if (state == null) throw new ArgumentNullException("state");
            output = writer ?? throw new ArgumentNullException("writer");
            v2 = new S2CompleteAudioRawSink(state, v2Output);
        }

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
            if (frame.Bk2Row != row || row != (lastRow < 0 ? S2AudioObserverProfile.FirstRow : lastRow + 1) || row >= S2AudioObserverProfile.ExclusiveEnd)
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
            if (lastRow != S2AudioObserverProfile.ExclusiveEnd - 1)
                throw new InvalidDataException("The S2 raw-v3 candidate rejects an early or empty cutoff.");
            v2.Complete(cutoff); Flush(false, null); complete = true;
        }

        private JArray Transfers(int row, IReadOnlyList<S2PreconsumptionRequestObserver.Transfer> transfers)
        {
            if (transfers.Count > 4) throw new InvalidDataException("The S2 raw-v3 transfer count exceeds the fixed slot bound.");
            var values = new JArray();
            for (int index = 0; index < transfers.Count; index++)
            {
                S2PreconsumptionRequestObserver.Transfer transfer = transfers[index];
                if (transfer == null || transfer.Row != row || transfer.Request == 0 || transfer.Slot > 3 || transfer.Pc != S2PreconsumptionRequestObserver.Pc || transfer.NativeOrdinal == 0 || transfer.NativeOrdinal <= lastNativeOrdinal || transfer.ServiceToken != S2PreconsumptionRequestObserver.MarkerServiceToken || transfer.ServiceKind != S2PreconsumptionRequestObserver.MarkerServiceKind || transfer.Depth != S2PreconsumptionRequestObserver.MarkerDepth)
                    throw new InvalidDataException("The S2 raw-v3 has an invalid request transfer.");
                lastNativeOrdinal = transfer.NativeOrdinal;
                values.Add(new JObject { ["row"] = row, ["order"] = index, ["request"] = transfer.Request, ["slot"] = transfer.Slot, ["pc"] = transfer.Pc, ["a7"] = transfer.A7.ToString(CultureInfo.InvariantCulture), ["native_ordinal"] = transfer.NativeOrdinal, ["service_token"] = transfer.ServiceToken, ["service_kind"] = transfer.ServiceKind, ["depth"] = transfer.Depth, ["active_service_owner"] = new JObject { ["token"] = transfer.ServiceToken, ["kind"] = transfer.ServiceKind, ["depth"] = transfer.Depth } });
            }
            return values;
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
