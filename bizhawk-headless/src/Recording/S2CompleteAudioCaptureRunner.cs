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
        /// Opens the unbound candidate at power-on. The returned session owns
        /// the fixed callback registration and every row's native
        /// BeginFrame/advance/EndFrame/drain/correlation sequence. It is not
        /// reachable from the authenticated capture CLI while unbound.
        /// </summary>
        internal static RequestCandidateSession OpenRequestCandidateSession(
            string candidateManifestPath, IGpgxHost host,
            CompleteRunAudioObserver nativeObserver)
        {
            if (host == null) throw new ArgumentNullException("host");
            if (nativeObserver == null) throw new ArgumentNullException(
                "nativeObserver");
            S2PreconsumptionRequestProfile.Candidate candidate =
                S2PreconsumptionRequestProfile.LoadCandidate(candidateManifestPath);
            return new RequestCandidateSession(
                S2PreconsumptionRequestProfile.CreateObserver(candidate, host,
                    nativeObserver.CurrentFrameEventCount),
                nativeObserver);
        }

        /// <summary>
        /// Candidate-only power-on session. It deliberately has no frame or
        /// event-list input: only CompleteRunAudioObserver may drain the native
        /// record sequence that is correlated to the callback.
        /// </summary>
        internal sealed class RequestCandidateSession : IDisposable
        {
            private readonly S2PreconsumptionRequestObserver requests;
            private readonly CompleteRunAudioObserver nativeObserver;
            private readonly List<S2PreconsumptionRequestObserver.Transfer>
                published = new List<S2PreconsumptionRequestObserver.Transfer>();
            private int nextRow;
            private bool disposed;
            private bool completed;
            private bool failed;

            internal RequestCandidateSession(
                S2PreconsumptionRequestObserver value,
                CompleteRunAudioObserver observer)
            {
                requests = value ?? throw new ArgumentNullException("value");
                nativeObserver = observer ?? throw new ArgumentNullException(
                    "observer");
            }

            internal IReadOnlyList<S2PreconsumptionRequestObserver.Transfer>
                PublishedTransfers { get { return published.AsReadOnly(); } }

            internal IReadOnlyList<S2PreconsumptionRequestObserver.Transfer>
                AdvanceRow(int row, Action advance)
            {
                if (disposed) throw new ObjectDisposedException(
                    "RequestCandidateSession");
                if (advance == null) throw new ArgumentNullException("advance");
                if (row != nextRow)
                {
                    DisposeAfterFailure();
                    throw new InvalidDataException(
                        "The S2 request candidate cannot carry evidence across rows.");
                }
                try
                {
                    requests.BeginRow(row);
                    CompleteRunAudioObserver.FrameCapture frame =
                        nativeObserver.CaptureCanonicalFrame(row, advance);
                    IReadOnlyList<S2PreconsumptionRequestObserver.Transfer>
                        transfers = requests.CompleteOwnedRow(row, frame.RawEvents);
                    if (row >= S2AudioObserverProfile.FirstRow)
                        for (int index = 0; index < transfers.Count; index++)
                            published.Add(transfers[index]);
                    nextRow++;
                    return transfers;
                }
                catch
                {
                    DisposeAfterFailure();
                    throw;
                }
            }

            internal void Complete()
            {
                if (disposed) throw new ObjectDisposedException(
                    "RequestCandidateSession");
                if (nextRow != S2AudioObserverProfile.ExclusiveEnd)
                {
                    DisposeAfterFailure();
                    throw new InvalidDataException(
                        "The S2 request candidate ended before its full power-on interval.");
                }
                completed = true;
                Dispose();
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                requests.Dispose();
                if (!completed && !failed)
                    throw new InvalidDataException(
                        "The S2 request candidate was disposed before its full power-on interval.");
            }

            private void DisposeAfterFailure()
            {
                if (disposed) return;
                failed = true;
                try { Dispose(); }
                catch (InvalidOperationException) { }
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
