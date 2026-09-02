using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace OpenGGF.BizHawk.Headless
{
    internal interface IS2CompleteAudioCaptureSink
    {
        void Begin(CompleteRunAudioObserver.CutoffFrontier boundary);
        void Frame(int row, CompleteRunAudioObserver.FrameCapture frame,
            OverrideResumeDiagnosticAudio.Packet audio);
        void Complete(CompleteRunAudioObserver.CutoffFrontier cutoff);
    }

    /// <summary>
    /// Bounded Sonic 2 complete-audio capture. Native observation starts at
    /// power-on, while publication starts at the reviewed comparison boundary
    /// without resetting chip latches or the native service lifecycle.
    /// </summary>
    internal static class S2CompleteAudioCaptureRunner
    {
        internal sealed class CaptureResult
        {
            internal CaptureResult(int observedRows, int publishedRows)
            {
                ObservedRows = observedRows;
                PublishedRows = publishedRows;
            }

            internal int ObservedRows { get; private set; }
            internal int PublishedRows { get; private set; }
        }

        internal static CaptureResult CapturePinned(
            string romPath, string moviePath, string manifestPath,
            string capabilityPath, IS2CompleteAudioCaptureSink sink)
        {
            return CapturePinnedCore(romPath, moviePath, manifestPath,
                capabilityPath, sink, S2AudioObserverProfile.ExclusiveEnd,
                true);
        }

        internal static CaptureResult CaptureRawPinned(
            string romPath, string moviePath, string manifestPath,
            string capabilityPath, string outputPath)
        {
            CaptureResult result = null;
            PublishRawWithAttestation(outputPath, output =>
            {
                result = CaptureRawPinnedCore(romPath, moviePath, manifestPath,
                    capabilityPath, output, S2AudioObserverProfile.ExclusiveEnd,
                    true);
            });
            return result;
        }

        internal static CaptureResult CaptureRawBoundaryProofPinnedForTesting(
            string romPath, string moviePath, string manifestPath,
            string capabilityPath, TextWriter output)
        {
            return CaptureRawPinnedCore(romPath, moviePath, manifestPath,
                capabilityPath, output, S2AudioObserverProfile.FirstRow + 1,
                false);
        }

        internal static void PublishRawForTesting(
            string outputPath, Action<TextWriter> capture)
        {
            PublishRaw(outputPath, capture);
        }

        internal static void PublishRawWithAttestationForTesting(
            string outputPath,Action<TextWriter> capture)
        {
            PublishRawWithAttestation(outputPath,capture);
        }

        /// <summary>
        /// Pure candidate seam: the fixed observer is active around an entire
        /// row and is correlated before a caller can pass its result to the
        /// unbound raw-v3 sink. No production authority path calls this method.
        /// </summary>
        internal static IReadOnlyList<S2PreconsumptionRequestObserver.Transfer>
            CaptureRequestV3RowForTesting(string candidateManifestPath,
                IGpgxHost host, int row, Action advance,
                IEnumerable<GpgxAudioTraceEvent> nativeEvents)
        {
            if (host == null) throw new ArgumentNullException("host");
            if (advance == null) throw new ArgumentNullException("advance");
            S2PreconsumptionRequestProfile.Candidate candidate =
                S2PreconsumptionRequestProfile.LoadCandidate(candidateManifestPath);
            using (var requests = S2PreconsumptionRequestProfile.CreateObserver(
                candidate, host))
            {
                requests.BeginRow(row);
                advance();
                return requests.CorrelateRow(row, nativeEvents);
            }
        }

        private static void PublishRawWithAttestation(
            string outputPath,Action<TextWriter> capture)
        {
            if(string.IsNullOrEmpty(outputPath)||!Path.IsPathRooted(outputPath))
                throw new ArgumentException(
                    "The S2 raw staging output path must be absolute.","outputPath");
            if(capture==null)throw new ArgumentNullException("capture");
            string fullPath=Path.GetFullPath(outputPath);
            string fileName=Path.GetFileName(fullPath);
            const string suffix=".raw.jsonl";
            if(string.IsNullOrEmpty(fileName)
                ||!fileName.EndsWith(suffix,StringComparison.Ordinal))
                throw new ArgumentException(
                    "The S2 authoritative raw output must end in .raw.jsonl.",
                    "outputPath");
            string attestationName=fileName.Substring(0,
                fileName.Length-suffix.Length)+".attestation.json";
            var publisher=new NoReplacePublisher();
            using(NoReplacePublisher.StagedPublicationSet staged=
                publisher.StageAll(Path.GetDirectoryName(fullPath),
                    new[]{fileName,attestationName},writers=>
                    {
                        var hashing=new OverrideResumeRawDigestTextWriter(writers[0]);
                        capture(hashing);
                        OverrideResumeRawDigestTextWriter.Evidence evidence=
                            hashing.Finish();
                        writers[1].Write(OverrideResumeFirstDivergenceAttestation
                            .Serialize("s2",evidence,
                                "s2-native-gpgx-observer-abi4",DateTime.UtcNow));
                    }))
            {staged.Publish();}
        }

        private static void PublishRaw(
            string outputPath, Action<TextWriter> capture)
        {
            if (string.IsNullOrEmpty(outputPath) || !Path.IsPathRooted(outputPath))
                throw new ArgumentException(
                    "The S2 raw staging output path must be absolute.",
                    "outputPath");
            if (capture == null) throw new ArgumentNullException("capture");
            string fullPath = Path.GetFullPath(outputPath);
            string fileName = Path.GetFileName(fullPath);
            if (string.IsNullOrEmpty(fileName))
                throw new ArgumentException(
                    "The S2 raw staging output path must name a file.",
                    "outputPath");
            string directory = Path.GetDirectoryName(fullPath);
            var publisher = new NoReplacePublisher();
            using (NoReplacePublisher.StagedPublicationSet staged =
                publisher.StageAll(directory, new[] { fileName },
                    writers => capture(writers[0])))
            {
                staged.Publish();
            }
        }

        internal static CaptureResult CaptureBoundaryProofPinned(
            string romPath, string moviePath, string manifestPath,
            string capabilityPath, IS2CompleteAudioCaptureSink sink)
        {
            return CapturePinnedCore(romPath, moviePath, manifestPath,
                capabilityPath, sink, S2AudioObserverProfile.FirstRow + 1,
                false);
        }

        private static CaptureResult CapturePinnedCore(
            string romPath, string moviePath, string manifestPath,
            string capabilityPath, IS2CompleteAudioCaptureSink sink,
            int exclusiveEnd, bool requireExactEnd)
        {
            if (sink == null) throw new ArgumentNullException("sink");
            S2AudioObserverProfile.ValidateRom(romPath);
            Bk2Movie movie = S2AudioObserverProfile.OpenMovie(moviePath);
            S2AudioObserverProfile.LoadCapability(manifestPath, capabilityPath);
            S2AudioObserverProfile.VerifyInstallation(
                Environment.GetEnvironmentVariable("BIZHAWK_HOME"));
            using (GpgxHost host = GpgxHost.Open(romPath, movie.SyncSettings))
            {
                CompleteRunAudioObserver observer =
                    S2AudioObserverProfile.CreateObserver(
                        manifestPath, capabilityPath,
                        host.CreateAudioTraceApi());
                return CaptureCore(movie.OpenFrameStream(), host, observer,
                    sink, S2AudioObserverProfile.FirstRow, exclusiveEnd,
                    requireExactEnd);
            }
        }

        private static CaptureResult CaptureRawPinnedCore(
            string romPath, string moviePath, string manifestPath,
            string capabilityPath, TextWriter output, int exclusiveEnd,
            bool requireExactEnd)
        {
            if (output == null) throw new ArgumentNullException("output");
            S2AudioObserverProfile.ValidateRom(romPath);
            Bk2Movie movie = S2AudioObserverProfile.OpenMovie(moviePath);
            S2AudioObserverProfile.LoadCapability(manifestPath, capabilityPath);
            S2AudioObserverProfile.VerifyInstallation(
                Environment.GetEnvironmentVariable("BIZHAWK_HOME"));
            using (GpgxHost host = GpgxHost.Open(romPath, movie.SyncSettings))
            {
                CompleteRunAudioObserver observer =
                    S2AudioObserverProfile.CreateObserver(
                        manifestPath, capabilityPath,
                        host.CreateAudioTraceApi());
                var sink = new S2CompleteAudioRawSink(
                    new GpgxS2CompleteAudioStateSource(host), output);
                return CaptureCore(movie.OpenFrameStream(), host, observer,
                    sink, S2AudioObserverProfile.FirstRow, exclusiveEnd,
                    requireExactEnd);
            }
        }

        /// <summary>Deterministic synthetic seam; production uses CapturePinned.</summary>
        internal static CaptureResult CaptureIntervalForTesting(
            IEnumerable<Bk2Frame> frames, IGpgxHost host,
            CompleteRunAudioObserver observer,
            IS2CompleteAudioCaptureSink sink,
            int firstRow, int exclusiveEnd)
        {
            return CaptureCore(frames, host, observer, sink,
                firstRow, exclusiveEnd, true);
        }

        /// <summary>Deterministic synthetic seam; production uses CapturePinned.</summary>
        internal static CaptureResult CapturePrefixForTesting(
            IEnumerable<Bk2Frame> frames, IGpgxHost host,
            CompleteRunAudioObserver observer,
            IS2CompleteAudioCaptureSink sink,
            int firstRow, int exclusiveEnd)
        {
            return CaptureCore(frames, host, observer, sink,
                firstRow, exclusiveEnd, false);
        }

        private static CaptureResult CaptureCore(
            IEnumerable<Bk2Frame> frames, IGpgxHost host,
            CompleteRunAudioObserver observer,
            IS2CompleteAudioCaptureSink sink,
            int firstRow, int exclusiveEnd, bool requireExactEnd)
        {
            if (frames == null) throw new ArgumentNullException("frames");
            if (host == null) throw new ArgumentNullException("host");
            if (observer == null) throw new ArgumentNullException("observer");
            if (sink == null) throw new ArgumentNullException("sink");
            if (firstRow < 0 || exclusiveEnd <= firstRow)
                throw new ArgumentOutOfRangeException("firstRow");

            int published = 0;
            using (IEnumerator<Bk2Frame> rows = frames.GetEnumerator())
            {
                for (int row = 0; row < exclusiveEnd; row++)
                {
                    if (!rows.MoveNext())
                        throw new InvalidDataException(
                            "The S2 complete audio movie ended before row "
                            + exclusiveEnd + ".");
                    if (row == firstRow)
                        sink.Begin(observer.CaptureBoundaryFrontierAndResetPublication());
                    S1TraceCaptureRunner.ApplyFrame(rows.Current, host);
                    IOverrideResumeDiagnosticAudioHost diagnostic =
                        host as IOverrideResumeDiagnosticAudioHost;
                    OverrideResumeDiagnosticAudio.Packet audio;
                    CompleteRunAudioObserver.FrameCapture capture;
                    if (diagnostic == null)
                    {
                        capture = observer.CaptureCanonicalFrame(row, host.Advance);
                        audio = new OverrideResumeDiagnosticAudio.Packet(
                            44100, 0, new byte[0]);
                    }
                    else
                    {
                        capture = null;
                        audio = OverrideResumeDiagnosticAudio.AdvanceAndDrain(
                            new ObserverAdvanceDiagnosticAudioHost(
                                diagnostic, () => capture =
                                    observer.CaptureCanonicalFrame(
                                        row, diagnostic.AdvanceDiagnosticAudio)));
                    }
                    if (capture.Bk2Row != row)
                        throw new InvalidDataException(
                            "The S2 observer frame origin does not match the BK2 loop row.");
                    if (row >= firstRow)
                    {
                        sink.Frame(row, capture, audio);
                        published++;
                    }
                }
                if (requireExactEnd && rows.MoveNext())
                    throw new InvalidDataException(
                        "The S2 complete audio movie contains more rows than declared.");
            }
            sink.Complete(observer.CaptureCutoffFrontier());
            return new CaptureResult(exclusiveEnd, published);
        }

        /// <summary>
        /// Makes the shared pre/post drain rule own the single call while the
        /// observer still brackets that call with native BeginFrame/EndFrame.
        /// </summary>
        private sealed class ObserverAdvanceDiagnosticAudioHost
            : IOverrideResumeDiagnosticAudioHost
        {
            private readonly IOverrideResumeDiagnosticAudioHost inner;
            private readonly Action advance;

            internal ObserverAdvanceDiagnosticAudioHost(
                IOverrideResumeDiagnosticAudioHost value, Action action)
            {
                inner = value;
                advance = action;
            }

            public int DiagnosticAudioSampleRate
            { get { return inner.DiagnosticAudioSampleRate; } }

            public void AdvanceDiagnosticAudio() { advance(); }

            public short[] DrainDiagnosticAudio(out int stereoFrames)
            { return inner.DrainDiagnosticAudio(out stereoFrames); }
        }
    }
}
