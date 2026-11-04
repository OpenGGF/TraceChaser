using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>
    /// Ordered semantic load-phase observations, buffered until the existing
    /// recorder routes the completed physical row to its owning segment.
    /// Origin: OpenGGF 2026-11-03 synchronous-load timing contract extension.
    /// No counter, register, decoder progress or gameplay value is exported.
    /// </summary>
    public sealed class InitializationTimingEventEngine
    {
        // Capture safety bound, not a model of elapsed load work.
        private const int MaxEventsPerRow = 64;
        private static readonly Regex Fingerprint = new Regex("\\Asha256:[0-9a-f]{64}\\z");
        private readonly List<Observation> pending = new List<Observation>();
        private long loadOrdinal;
        private long? firstRepresentedLoad;
        private int phaseOrdinal;
        private PhaseIdentity active;

        public string ActivePhase { get { return active == null ? null : active.Phase; } }
        internal long CurrentRepresentedLoadOrdinal
        {
            get { return loadOrdinal == 0 ? 0 : loadOrdinal - (firstRepresentedLoad ?? loadOrdinal) + 1; }
        }

        private sealed class PhaseIdentity
        {
            internal long Load;
            internal int Ordinal;
            internal string Phase;
            internal string Fingerprint;
        }

        private sealed class Observation
        {
            internal PhaseIdentity Identity;
            internal string Operation;
        }

        public void BeginLoad()
        {
            if (active != null) throw new InvalidOperationException("load began before the active phase resumed");
            loadOrdinal = checked(loadOrdinal + 1);
            phaseOrdinal = 0;
        }

        public void BeginPhase(string phase, string submissionFingerprint)
        {
            if (phase != "display_clear" && phase != "level_data_decode"
                && phase != "level_setup" && phase != "initial_placement" && phase != "initial_objects" && phase != "initial_presentation")
                throw new ArgumentException("unsupported initialization phase", "phase");
            if (submissionFingerprint == null || !Fingerprint.IsMatch(submissionFingerprint))
                throw new ArgumentException("invalid ROM-backed operation fingerprint", "submissionFingerprint");
            if (loadOrdinal == 0 || active != null)
                throw new InvalidOperationException("phase has no load or overlaps another phase");
            active = new PhaseIdentity { Load = loadOrdinal, Ordinal = checked(++phaseOrdinal),
                Phase = phase, Fingerprint = submissionFingerprint };
        }

        public void ObserveZeroRoutineInterrupt()
        {
            if (active != null) Add(active, "vint_0");
        }

        public void ObserveOrdinaryInterrupt()
        {
            if (active != null)
                throw new InvalidOperationException("ordinary interrupt inside a synchronous initialization phase");
        }

        public void ResumePhase()
        {
            if (active == null) throw new InvalidOperationException("no active initialization phase");
            Add(active, "resume");
            active = null;
        }

        private void Add(PhaseIdentity identity, string operation)
        {
            if (pending.Count >= MaxEventsPerRow)
                throw new InvalidOperationException("initialization observation row overflow");
            pending.Add(new Observation { Identity = identity, Operation = operation });
        }

        public void FlushFrame(int rawFrame, TextWriter writer)
        {
            if (rawFrame < 0) throw new ArgumentOutOfRangeException("rawFrame");
            if (writer == null) throw new ArgumentNullException("writer");
            if (active != null) Add(active, "hold");
            if (pending.Count != 0 && !firstRepresentedLoad.HasValue)
                firstRepresentedLoad = pending[0].Identity.Load;
            for (int sequence = 0; sequence < pending.Count; sequence++)
            {
                Observation observation = pending[sequence];
                PhaseIdentity identity = observation.Identity;
                writer.Write("{\"event\":\"load_phase_timing\",\"raw_frame\":");
                writer.Write(rawFrame.ToString(CultureInfo.InvariantCulture));
                writer.Write(",\"sequence\":"); writer.Write(sequence.ToString(CultureInfo.InvariantCulture));
                writer.Write(",\"load_ordinal\":");
                writer.Write((identity.Load - firstRepresentedLoad.Value + 1).ToString(CultureInfo.InvariantCulture));
                writer.Write(",\"phase_ordinal\":"); writer.Write(identity.Ordinal.ToString(CultureInfo.InvariantCulture));
                writer.Write(",\"phase\":\""); writer.Write(identity.Phase);
                writer.Write("\",\"submission_fingerprint\":\""); writer.Write(identity.Fingerprint);
                writer.Write("\",\"operation\":\""); writer.Write(observation.Operation);
                writer.Write("\"}\n");
            }
            pending.Clear();
        }

        /// <summary>Unrepresented rows have no input authority; retain only the live phase identity.</summary>
        public void DiscardUnrepresentedFrame() { pending.Clear(); }

        public void VerifyComplete()
        {
            if (active != null || pending.Count != 0)
                throw new InvalidOperationException("incomplete initialization timing observations");
        }
    }
}
