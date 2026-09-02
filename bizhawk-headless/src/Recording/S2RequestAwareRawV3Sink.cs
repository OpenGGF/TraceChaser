using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>
    /// Unbound candidate raw-v3 writer. It records only already-correlated
    /// observation evidence; it has no fixture or production capture authority.
    /// </summary>
    internal sealed class S2RequestAwareRawV3Sink
    {
        internal const string Schema = "openggf.s2-complete-run-audio-raw.v3";
        private readonly IS2CompleteAudioStateSource state;
        private readonly TextWriter output;
        private int lastRow = -1;
        private bool begun;
        private bool complete;

        internal S2RequestAwareRawV3Sink(IS2CompleteAudioStateSource value,
            TextWriter writer)
        {
            state = value ?? throw new ArgumentNullException("value");
            output = writer ?? throw new ArgumentNullException("writer");
        }

        internal void Begin(CompleteRunAudioObserver.CutoffFrontier boundary)
        {
            if (boundary == null) throw new ArgumentNullException("boundary");
            if (begun) throw new InvalidOperationException(
                "The unbound S2 raw-v3 epoch already began.");
            Write(new JObject
            {
                ["type"] = "metadata", ["schema"] = Schema,
                ["production_bound"] = false,
                ["base_service_manifest_sha256"] =
                    S2AudioObserverProfile.ServiceManifestSha256,
                ["first_row"] = S2AudioObserverProfile.FirstRow,
                ["state_start"] = S2AudioObserverProfile.DriverStateStart,
                ["state_exclusive_end"] =
                    S2AudioObserverProfile.DriverStateExclusiveEnd
            });
            Write(new JObject { ["type"] = "baseline",
                ["row"] = S2AudioObserverProfile.FirstRow,
                ["state_hex"] = StateHex() });
            begun = true;
        }

        internal void Frame(int row, CompleteRunAudioObserver.FrameCapture frame,
            OverrideResumeDiagnosticAudio.Packet audio,
            IReadOnlyList<S2PreconsumptionRequestObserver.Transfer> transfers)
        {
            if (!begun || complete) throw new InvalidOperationException(
                "The unbound S2 raw-v3 epoch is not active.");
            if (frame == null || audio == null || transfers == null)
                throw new ArgumentNullException("raw-v3 frame input");
            int expected = lastRow < 0 ? S2AudioObserverProfile.FirstRow : lastRow + 1;
            if (row != expected || frame.Bk2Row != row)
                throw new InvalidDataException("The S2 raw-v3 rows are not contiguous.");
            var values = new JArray();
            uint previousOrdinal = 0;
            for (int index = 0; index < transfers.Count; index++)
            {
                S2PreconsumptionRequestObserver.Transfer transfer = transfers[index];
                if (transfer == null || transfer.Row != row || transfer.Request == 0
                    || transfer.Slot > 3 || transfer.Pc
                        != S2PreconsumptionRequestObserver.Pc
                    || transfer.NativeOrdinal == 0
                    || (index != 0 && transfer.NativeOrdinal <= previousOrdinal))
                    throw new InvalidDataException(
                        "The S2 raw-v3 has an invalid request transfer.");
                previousOrdinal = transfer.NativeOrdinal;
                values.Add(new JObject
                {
                    ["row"] = row, ["order"] = index, ["request"] = transfer.Request,
                    ["slot"] = transfer.Slot, ["pc"] = transfer.Pc,
                    ["a7"] = transfer.A7.ToString(CultureInfo.InvariantCulture),
                    ["native_ordinal"] = transfer.NativeOrdinal,
                    ["service_token"] = transfer.ServiceToken,
                    ["service_kind"] = transfer.ServiceKind,
                    ["depth"] = transfer.Depth,
                    ["active_service_owner"] = new JObject
                    {
                        ["token"] = transfer.ServiceToken,
                        ["kind"] = transfer.ServiceKind,
                        ["depth"] = transfer.Depth
                    }
                });
            }
            Write(new JObject { ["type"] = "frame", ["row"] = row,
                ["lag"] = state.IsLagged, ["state_hex"] = StateHex(),
                ["request_transfers"] = values });
            lastRow = row;
        }

        internal void Complete(CompleteRunAudioObserver.CutoffFrontier cutoff)
        {
            if (!begun || complete) throw new InvalidOperationException(
                "The unbound S2 raw-v3 epoch is not active.");
            if (cutoff == null) throw new ArgumentNullException("cutoff");
            Write(new JObject { ["type"] = "cutoff", ["exclusive_end"] =
                lastRow < 0 ? S2AudioObserverProfile.FirstRow : lastRow + 1,
                ["state_hex"] = StateHex() });
            complete = true;
        }

        private string StateHex()
        {
            byte[] bytes = state.CaptureDriverState();
            int expected = S2AudioObserverProfile.DriverStateExclusiveEnd
                - S2AudioObserverProfile.DriverStateStart;
            if (bytes == null || bytes.Length != expected)
                throw new InvalidDataException("The S2 raw-v3 state is not exact.");
            char[] hex = new char[bytes.Length * 2];
            const string alphabet = "0123456789abcdef";
            for (int i = 0; i < bytes.Length; i++)
            { hex[i * 2] = alphabet[bytes[i] >> 4]; hex[i * 2 + 1] = alphabet[bytes[i] & 15]; }
            return new string(hex);
        }

        private void Write(JObject value)
        {
            output.Write(value.ToString(Newtonsoft.Json.Formatting.None));
            output.Write('\n');
        }
    }
}
