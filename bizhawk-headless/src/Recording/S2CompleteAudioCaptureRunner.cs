using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>
    /// Candidate-only host surface. No production host implements this until
    /// a fresh capability binds the fixed native patch and v3 inventory.
    /// </summary>
    internal interface IS2RequestAwareRawV3CandidateHost : IGpgxHost,
        ICpuRegisterReader, IS2CompleteAudioStateSource
    {
        IGpgxAudioTraceApi CreateRequestCandidateAudioTraceApi();
    }

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
    internal static partial class S2CompleteAudioCaptureRunner
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
        /// Closed unbound producer. It owns the authenticated base profile,
        /// the one fixed candidate hook, callback registration, row advance,
        /// native drain, request correlation, state capture, and raw-v3 sink.
        /// It is deliberately unreachable from the authenticated CLI.
        /// </summary>
        internal static RequestAwareRawV3Candidate
            OpenRequestAwareRawV3Candidate(
                string candidateManifestPath, string baseServiceManifestPath,
                IS2RequestAwareRawV3CandidateHost host, TextWriter output)
        {
            return RequestAwareRawV3Candidate.Open(candidateManifestPath,
                baseServiceManifestPath, host, output);
        }

        /// <summary>
        /// Friend-test interval seam. It changes only publication bounds; the
        /// closed observer, callback correlation, and raw-v3 producer remain
        /// the production candidate implementations.
        /// </summary>
        internal static RequestAwareRawV3Candidate
            OpenRequestAwareRawV3CandidateForTesting(
                string candidateManifestPath, string baseServiceManifestPath,
                IS2RequestAwareRawV3CandidateHost host, TextWriter output,
                int firstRow, int exclusiveEnd)
        {
            return RequestAwareRawV3Candidate.Open(candidateManifestPath,
                baseServiceManifestPath, host, output, firstRow, exclusiveEnd);
        }

        /// <summary>
        /// The bounded window producer the request-window command drives. The
        /// interval and the recording identity are both supplied by the caller,
        /// so no window and no movie is baked into this harness.
        /// </summary>
        internal static RequestAwareRawV3Candidate
            OpenRequestAwareRawV3CandidateForWindow(
                string candidateManifestPath, string baseServiceManifestPath,
                IS2RequestAwareRawV3CandidateHost host, TextWriter output,
                int firstRow, int exclusiveEnd, string recordingSha256)
        {
            return RequestAwareRawV3Candidate.Open(candidateManifestPath,
                baseServiceManifestPath, host, output, firstRow, exclusiveEnd,
                recordingSha256);
        }

        internal sealed partial class RequestAwareRawV3Candidate : IDisposable
        {
            private readonly IS2RequestAwareRawV3CandidateHost host;
            private readonly CompleteRunAudioObserver nativeObserver;
            private readonly S2PreconsumptionRequestObserver requests;
            private readonly RawV3Sink sink;
            private int nextRow;
            private bool completed;
            private bool failed;
            private bool disposed;
            private readonly int firstRow;
            private readonly int exclusiveEnd;

            private RequestAwareRawV3Candidate(
                IS2RequestAwareRawV3CandidateHost candidateHost,
                CompleteRunAudioObserver observer,
                S2PreconsumptionRequestObserver requestObserver,
                TextWriter output, int sourceFirstRow, int sourceExclusiveEnd,
                string recordingSha256)
            {
                host = candidateHost
                    ?? throw new ArgumentNullException("candidateHost");
                nativeObserver = observer
                    ?? throw new ArgumentNullException("observer");
                requests = requestObserver
                    ?? throw new ArgumentNullException("requestObserver");
                if (sourceFirstRow < 0
                    || sourceExclusiveEnd <= sourceFirstRow)
                    throw new ArgumentOutOfRangeException("sourceFirstRow");
                firstRow = sourceFirstRow;
                exclusiveEnd = sourceExclusiveEnd;
                sink = new RawV3Sink(host,
                    output ?? throw new ArgumentNullException("output"),
                    firstRow, exclusiveEnd, recordingSha256);
            }

            internal static RequestAwareRawV3Candidate Open(
                string candidateManifestPath, string baseServiceManifestPath,
                IS2RequestAwareRawV3CandidateHost candidateHost,
                TextWriter output)
            {
                return Open(candidateManifestPath, baseServiceManifestPath,
                    candidateHost, output, S2AudioObserverProfile.FirstRow,
                    S2AudioObserverProfile.ExclusiveEnd);
            }

            internal static RequestAwareRawV3Candidate Open(
                string candidateManifestPath, string baseServiceManifestPath,
                IS2RequestAwareRawV3CandidateHost candidateHost,
                TextWriter output, int sourceFirstRow, int sourceExclusiveEnd)
            {
                return Open(candidateManifestPath, baseServiceManifestPath,
                    candidateHost, output, sourceFirstRow, sourceExclusiveEnd,
                    S2AudioObserverProfile.MovieSha256);
            }

            internal static RequestAwareRawV3Candidate Open(
                string candidateManifestPath, string baseServiceManifestPath,
                IS2RequestAwareRawV3CandidateHost candidateHost,
                TextWriter output, int sourceFirstRow, int sourceExclusiveEnd,
                string recordingSha256)
            {
                if (candidateHost == null)
                    throw new ArgumentNullException("candidateHost");
                if (output == null) throw new ArgumentNullException("output");
                S2PreconsumptionRequestProfile.Candidate candidate =
                    S2PreconsumptionRequestProfile.LoadCandidate(
                        candidateManifestPath);
                CompleteRunAudioObserver observer =
                    S2PreconsumptionRequestProfile.CreateObserver(candidate,
                        baseServiceManifestPath, candidateHost
                            .CreateRequestCandidateAudioTraceApi());
                try
                {
                    return new RequestAwareRawV3Candidate(candidateHost,
                        observer, new S2PreconsumptionRequestObserver(
                            candidate, candidateHost, observer,
                            sourceExclusiveEnd), output, sourceFirstRow,
                            sourceExclusiveEnd, recordingSha256);
                }
                catch
                {
                    try { observer.DiscardCutoffState(); }
                    catch { }
                    throw;
                }
            }

            internal void AdvanceRow(int row, Bk2Frame frame)
            {
                if (disposed) throw new ObjectDisposedException(
                    "RequestAwareRawV3Candidate");
                if (frame == null) throw new ArgumentNullException("frame");
                if (row != nextRow)
                {
                    CleanupAfterFailure();
                    throw new InvalidDataException(
                        "The closed S2 raw-v3 producer cannot carry evidence across rows.");
                }
                try
                {
                    if (row == firstRow)
                        sink.Begin(nativeObserver
                            .CaptureBoundaryFrontierAndResetPublication());
                    S1TraceCaptureRunner.ApplyFrame(frame, host);
                    S2PreconsumptionRequestObserver.OwnedRow owned;
                    OverrideResumeDiagnosticAudio.Packet audio;
                    IOverrideResumeDiagnosticAudioHost diagnostic =
                        host as IOverrideResumeDiagnosticAudioHost;
                    if (diagnostic == null)
                    {
                        owned = requests.AdvanceOwnedRow(row, host.Advance);
                        audio = new OverrideResumeDiagnosticAudio.Packet(
                            44100, 0, new byte[0]);
                    }
                    else
                    {
                        owned = null;
                        audio = OverrideResumeDiagnosticAudio.AdvanceAndDrain(
                            new ObserverAdvanceDiagnosticAudioHost(diagnostic,
                                () => owned = requests.AdvanceOwnedRow(row,
                                    diagnostic.AdvanceDiagnosticAudio)));
                    }
                    if (owned == null || owned.Frame == null
                        || owned.Frame.Bk2Row != row)
                        throw new InvalidDataException(
                            "The closed S2 producer lost its owned frame origin.");
                    if (row >= firstRow)
                        sink.Frame(row, owned.Frame, audio, owned.Transfers);
                    nextRow++;
                }
                catch
                {
                    CleanupAfterFailure();
                    throw;
                }
            }

            internal void Complete()
            {
                if (disposed) throw new ObjectDisposedException(
                    "RequestAwareRawV3Candidate");
                if (nextRow != exclusiveEnd)
                {
                    Dispose();
                    throw new InvalidDataException(
                        "The closed S2 raw-v3 producer ended early.");
                }
                try
                {
                    sink.Complete(nativeObserver.CaptureCutoffFrontier());
                    requests.Complete();
                    nativeObserver.DiscardCutoffState();
                    completed = true;
                    disposed = true;
                }
                catch
                {
                    CleanupAfterFailure();
                    throw;
                }
            }

            public void Dispose()
            {
                if (disposed) return;
                bool rejectEarly = !completed && !failed;
                disposed = true;
                Exception first = null;
                try { requests.Dispose(); }
                catch (Exception error) { first = error; }
                try { nativeObserver.DiscardCutoffState(); }
                catch (Exception error) { if (first == null) first = error; }
                if (rejectEarly)
                    throw first as InvalidDataException
                        ?? new InvalidDataException(
                        "The closed S2 raw-v3 producer was disposed before its full power-on interval.");
                if (first != null) throw first;
            }

            private void CleanupAfterFailure()
            {
                if (disposed) return;
                failed = true;
                try { requests.Dispose(); }
                catch { }
                try { nativeObserver.DiscardCutoffState(); }
                catch { }
                disposed = true;
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
