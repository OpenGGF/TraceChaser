using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>
    /// Dedicated readiness observations for represented native loads outside
    /// structural segments. Origin: OpenGGF synchronous-load task, 2026-11-04.
    /// Input is an actual queue completion, never interstitial provenance.
    /// </summary>
    internal sealed class InitializationHardwareTimingWriter
    {
        private static readonly Regex Fingerprint = new Regex("\\Asha256:[0-9a-f]{64}\\z");
        private readonly TextWriter output;

        internal InitializationHardwareTimingWriter(TextWriter output)
        {
            if (output == null) throw new ArgumentNullException("output");
            this.output = output;
        }

        internal void ObserveCompletion(int rawFrame, long? loadOrdinal,
            bool structuralSegmentOwns, string boundary, string kind,
            long ordinal, string fingerprint)
        {
            // Cold bootstrap, ordinary gaps and per-segment observations have
            // no authority in this companion. Their existing writers are unchanged.
            if (structuralSegmentOwns || !loadOrdinal.HasValue) return;
            if (rawFrame < 0 || loadOrdinal.Value <= 0 || ordinal < 0)
                throw new ArgumentOutOfRangeException("rawFrame", "invalid initialization completion identity");
            if (boundary != "vint_service" && boundary != "post_objects" && boundary != "pre_main_loop")
                throw new ArgumentException("unsupported initialization completion boundary", "boundary");
            if (kind != "kos_module_queue" && kind != "kos_decompression_queue")
                throw new ArgumentException("unsupported initialization completion kind", "kind");
            if (fingerprint == null || !Fingerprint.IsMatch(fingerprint))
                throw new ArgumentException("invalid initialization completion fingerprint", "fingerprint");
            output.Write("{\"event\":\"hardware_work_completed\",\"raw_frame\":");
            output.Write(rawFrame.ToString(CultureInfo.InvariantCulture));
            output.Write(",\"boundary\":\""); output.Write(boundary);
            output.Write("\",\"kind\":\""); output.Write(kind);
            output.Write("\",\"ordinal\":"); output.Write(ordinal.ToString(CultureInfo.InvariantCulture));
            output.Write(",\"submission_fingerprint\":\""); output.Write(fingerprint);
            output.Write("\",\"load_ordinal\":"); output.Write(loadOrdinal.Value.ToString(CultureInfo.InvariantCulture));
            output.Write("}\n");
        }
    }
}
