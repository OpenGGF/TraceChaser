using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace OpenGGF.BizHawk.Headless.Tests
{
    // Synthetic host scripts are independent of captured fixture values. They
    // distinguish the last exported level row, final input advance, and cleanup.
    internal static class S3KMovieEndTests
    {
        public static void Register(ICollection<TestMain.TestCase> tests)
        {
            tests.Add(new TestMain.TestCase("S3KMovieEnd samples final advance before finalization", FinalAdvance));
            tests.Add(new TestMain.TestCase("S3KMovieEnd records actual level endpoint", Level));
            tests.Add(new TestMain.TestCase("S3KMovieEnd recognizes PreLevel but excludes save screen", PreLevel));
            tests.Add(new TestMain.TestCase("S3KMovieEnd omits unsupported raw modes", Unsupported));
            tests.Add(new TestMain.TestCase("S3KMovieEnd omits shortened capture bounds", Shortened));
            tests.Add(new TestMain.TestCase("S3KMovieEnd omits even equal explicit bounds", EqualBound));
            tests.Add(new TestMain.TestCase("S3KMovieEnd rejects input exhaustion before extended bound", Extended));
        }

        private static void FinalAdvance()
        {
            Run(0x04, 0, true, result =>
            {
                AssertMode(result, "title_screen");
                AssertEx.Equal(1, result.Segments.Count);
                AssertEx.Equal(1, result.Segments[0].Bk2FrameOffset);
                AssertEx.Equal(2, result.Segments[0].TraceFrameCount);
            });
        }

        private static void Level()
        {
            Run(0x0C, 0, false, result => AssertMode(result, "level"));
        }

        private static void PreLevel()
        {
            Run(0x8C, 0, false, result => AssertMode(result, "level"));
            // GameModes dispatches $4C to SaveScreen, despite its low nibble.
            Run(0x4C, 0, false, AssertAbsent);
            Run(0xCC, 0, false, AssertAbsent);
        }

        private static void Unsupported()
        {
            foreach (byte mode in new byte[] { 0x00, 0x08, 0x14, 0x20, 0x28, 0x34, 0x48, 0xFF })
            {
                Run(mode, 0, false, AssertAbsent);
            }
        }

        private static void Shortened()
        {
            Run(0x04, 3, false, AssertAbsent);
        }

        private static void EqualBound()
        {
            Run(0x04, 4, false, AssertAbsent);
        }

        private static void Extended()
        {
            AssertEx.Throws<InvalidDataException>(() => Run(0x04, 5, false, result =>
            {
                throw new Exception("An exhausted input stream must not publish a result.");
            }), "ran out of BK2 input");
        }

        private static void AssertMode(S3KCompleteRunCaptureResult result, string mode)
        {
            AssertEx.Equal(true, result.RunManifestJson.Contains(
                "  \"expected_movie_end_mode\": \"" + mode + "\",\n"));
        }

        private static void AssertAbsent(S3KCompleteRunCaptureResult result)
        {
            AssertEx.Equal(false, result.RunManifestJson.Contains("expected_movie_end_mode"));
        }

        private static void Run(byte terminalMode, int bound, bool mutateOnClose,
            Action<S3KCompleteRunCaptureResult> check)
        {
            string root = TestScratch.CreateRootPath("s3k-terminal-unit");
            Directory.CreateDirectory(root);
            try
            {
                string path = Path.Combine(root, "synthetic.bk2");
                using (var archive = new ZipArchive(File.Create(path), ZipArchiveMode.Create))
                {
                    Add(archive, "Header.txt", Fixture("ghz1-header.txt"));
                    Add(archive, "SyncSettings.json", Fixture("ghz1-sync-settings.json"));
                    // Four actual input rows; no invented movie-length signal.
                    Add(archive, "Input Log.txt", Fixture("ghz1-input-prefix.txt")
                        .Replace("[/Input]", "|..|........|........|\r\n[/Input]"));
                }
                Bk2Movie movie = Bk2Reader.Read(path);
                AssertEx.Equal(4, movie.FrameCount);
                var host = new FakeS1Host((h, frame) =>
                {
                    if (frame > 4) throw new Exception("Advanced beyond original inputs.");
                    // Literal ROM RAM address, deliberately independent of S3KRam.GameMode.
                    h.Ram[0xF600] = frame == 4 ? terminalMode : (byte)0x0C;
                });
                var sink = new Sink(() =>
                {
                    if (mutateOnClose) host.Ram[0xF600] = 0x00;
                });
                var result = S3KCompleteRunCaptureRunner.Capture(
                    movie, host, "synthetic-terminal", "synthetic.bk2", "2000-01-01", bound, sink);
                AssertEx.Equal(bound == 3 ? 3 : 4, host.CompletedFrame);
                AssertEx.Equal(host.CompletedFrame, host.ClearButtonsCount);
                AssertEx.Equal(true, host.ExecuteCallbackDisposed);
                check(result);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static string Fixture(string name)
        {
            return File.ReadAllText(Path.Combine(EndToEndTests.ToolDirectory, "fixtures", name));
        }

        private static void Add(ZipArchive archive, string name, string content)
        {
            using (var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false)))
                writer.Write(content);
        }

        private sealed class Sink : IS3KCompleteRunSegmentSink
        {
            private readonly Action close;
            public Sink(Action close) { this.close = close; }
            public S3KSegmentStreams BeginSegment(S3KSegmentArm arm)
            {
                return new S3KSegmentStreams(TextWriter.Null, TextWriter.Null, TextWriter.Null);
            }
            public void EndSegment(RunManifestSegment entry, string metadata) { close(); }
            public TextWriter OpenInterstitialHardwareTimingStream() { return TextWriter.Null; }
        }
    }
}
