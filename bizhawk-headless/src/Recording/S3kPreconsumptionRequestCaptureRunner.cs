using System;
using System.Collections.Generic;
using System.IO;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>
    /// Power-on capture loop for the fixed Sonic 3&amp;K Sonic/Tails
    /// pre-consumption music-mailbox diagnostic. It validates the entire
    /// 466,334-row movie identity and then captures exactly the [0,5400)
    /// prefix.
    ///
    /// The sink begins from <c>CaptureCutoffFrontier()</c> rather than
    /// <c>CaptureBoundaryFrontierAndResetPublication()</c>: the latter starts a
    /// new native publication epoch unconditionally, which at row zero would
    /// pre-empt the normal SndDrvInit arming lifecycle this profile relies on.
    ///
    /// This runner has no CLI route, capability, or install verification, so it
    /// confers no capture authority. Publication of a fixture remains a
    /// separate reviewed operation.
    /// </summary>
    internal static class S3kPreconsumptionRequestCaptureRunner
    {
        internal sealed class CaptureResult
        {
            internal CaptureResult(int observedRows, int publishedRows,
                int requestCount)
            {
                ObservedRows = observedRows;
                PublishedRows = publishedRows;
                RequestCount = requestCount;
            }

            internal int ObservedRows { get; private set; }
            internal int PublishedRows { get; private set; }
            internal int RequestCount { get; private set; }
        }

        internal static CaptureResult CaptureRawPinned(
            string romPath, string moviePath, string manifestPath,
            string outputPath)
        {
            if (string.IsNullOrEmpty(outputPath)
                || !Path.IsPathRooted(outputPath))
                throw new ArgumentException(
                    "The S3K request raw output path must be absolute.",
                    "outputPath");
            string fullPath = Path.GetFullPath(outputPath);
            string fileName = Path.GetFileName(fullPath);
            if (string.IsNullOrEmpty(fileName))
                throw new ArgumentException(
                    "The S3K request raw output path must name a file.",
                    "outputPath");
            CaptureResult result = null;
            var publisher = new NoReplacePublisher();
            using (NoReplacePublisher.StagedPublicationSet staged =
                publisher.StageAll(Path.GetDirectoryName(fullPath),
                    new[] { fileName },
                    writers =>
                    {
                        result = CaptureRawCore(romPath, moviePath,
                            manifestPath, writers[0],
                            S3kPreconsumptionRequestProfile.ExclusiveEnd);
                    }))
            {
                staged.Publish();
            }
            return result;
        }

        /// <summary>
        /// Disposable non-authoritative smoke seam. It writes to a caller-owned
        /// writer, captures a shortened prefix, and can never reach a fixture
        /// destination because it performs no publication.
        /// </summary>
        internal static CaptureResult CaptureRawSmokePrefix(
            string romPath, string moviePath, string manifestPath,
            TextWriter output, int exclusiveEnd)
        {
            if (exclusiveEnd <= 0
                || exclusiveEnd > S3kPreconsumptionRequestProfile.ExclusiveEnd)
                throw new ArgumentOutOfRangeException("exclusiveEnd");
            return CaptureRawCore(romPath, moviePath, manifestPath, output,
                exclusiveEnd);
        }

        private static CaptureResult CaptureRawCore(
            string romPath, string moviePath, string manifestPath,
            TextWriter output, int exclusiveEnd)
        {
            if (output == null) throw new ArgumentNullException("output");
            S3kPreconsumptionRequestProfile.ValidateRom(romPath);
            Bk2Movie movie =
                S3kPreconsumptionRequestProfile.OpenMovie(moviePath);
            using (GpgxHost host = GpgxHost.Open(romPath, movie.SyncSettings))
            {
                CompleteRunAudioObserver observer =
                    S3kPreconsumptionRequestProfile.CreateObserver(
                        manifestPath, host.CreateAudioTraceApi());
                var counter = new RequestCountingSink(
                    new S3kCompleteAudioRawSink(
                        new GpgxS3kCompleteAudioStateSource(host), output,
                        S3kPreconsumptionRequestRawAuthority.Instance));
                CaptureCore(movie.OpenFrameStream(), host, observer, counter,
                    exclusiveEnd);
                return new CaptureResult(exclusiveEnd, exclusiveEnd,
                    counter.RequestCount);
            }
        }

        private static void CaptureCore(IEnumerable<Bk2Frame> frames,
            GpgxHost host, CompleteRunAudioObserver observer,
            IS3kCompleteAudioCaptureSink sink, int exclusiveEnd)
        {
            using (IEnumerator<Bk2Frame> rows = frames.GetEnumerator())
            {
                for (int row = 0; row < exclusiveEnd; row++)
                {
                    if (!rows.MoveNext())
                        throw new InvalidDataException(
                            "The S3K request movie ended before row "
                            + exclusiveEnd + ".");
                    if (row == S3kPreconsumptionRequestProfile.FirstRow)
                        sink.Begin(observer.CaptureCutoffFrontier());
                    S1TraceCaptureRunner.ApplyFrame(rows.Current, host);
                    sink.Frame(row,
                        observer.CaptureCanonicalFrame(host.Advance));
                }
            }
            sink.Complete(observer.CaptureCutoffFrontier());
        }

        private sealed class RequestCountingSink : IS3kCompleteAudioCaptureSink
        {
            private readonly IS3kCompleteAudioCaptureSink inner;

            internal RequestCountingSink(IS3kCompleteAudioCaptureSink value)
            { inner = value; }

            internal int RequestCount { get; private set; }

            public void Begin(CompleteRunAudioObserver.CutoffFrontier boundary)
            { inner.Begin(boundary); }

            public void Frame(int row,
                CompleteRunAudioObserver.FrameCapture frame)
            {
                foreach (CompleteRunAudioObserver.DriverService service
                    in frame.Services)
                    if (service.Kind
                        == S3kPreconsumptionRequestProfile.SubmissionKind)
                        RequestCount++;
                inner.Frame(row, frame);
            }

            public void Complete(CompleteRunAudioObserver.CutoffFrontier cutoff)
            { inner.Complete(cutoff); }
        }
    }
}
