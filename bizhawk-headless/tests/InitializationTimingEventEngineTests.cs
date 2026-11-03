using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace OpenGGF.BizHawk.Headless.Tests
{
    internal static class InitializationTimingEventEngineTests
    {
        private const string Fp = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        public static void Register(ICollection<TestMain.TestCase> tests)
        {
            tests.Add(new TestMain.TestCase("InitializationTiming native same-row service and phase ordering", SameRowOrder));
            tests.Add(new TestMain.TestCase("InitializationTiming no-service row is explicit hold", NoService));
            tests.Add(new TestMain.TestCase("InitializationTiming unrepresented prefix retains live identity", UnrepresentedPrefix));
            tests.Add(new TestMain.TestCase("InitializationTiming ordinary handler cannot masquerade as zero service", OrdinaryHandlerRejected));
            tests.Add(new TestMain.TestCase("InitializationTiming bounded row overflow fails capture", Overflow));
            tests.Add(new TestMain.TestCase("InitializationTiming invalid identity and overlapping phase rejected", InvalidIdentity));
            tests.Add(new TestMain.TestCase("InitializationTiming operation fingerprint matches independent Java vector", FingerprintVector));
            tests.Add(new TestMain.TestCase("InitializationTiming initial-object service requires proven semantic cut", InitialObjectCuts, game: "s3k"));
            tests.Add(new TestMain.TestCase("InitializationTiming actual remaining callbacks validate relocated service", ReviewedRemainingCallbacks, game: "s3k"));
            tests.Add(new TestMain.TestCase("InitializationTiming capped phases and provisional tails roll back publication", CappedInitializationRollsBack, game: "s3k"));
            tests.Add(new TestMain.TestCase("InitializationTiming empty SST tail can resume objects without skipping live work", EmptyObjectTail, game: "s3k"));
            tests.Add(new TestMain.TestCase("InitializationTiming verified native hook service and phase ordering", NativeHooks, game: "s3k"));
        }

        private static InitializationTimingEventEngine Started()
        {
            var observer = new InitializationTimingEventEngine();
            observer.BeginLoad();
            observer.BeginPhase("level_setup", Fp);
            return observer;
        }

        private static void SameRowOrder()
        {
            var observer = Started();
            observer.ObserveZeroRoutineInterrupt();
            observer.ObserveZeroRoutineInterrupt();
            observer.ResumePhase();
            observer.BeginPhase("initial_objects", Fp);
            observer.ResumePhase();
            var writer = new StringWriter();
            observer.FlushFrame(42, writer);
            string[] rows = writer.ToString().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            AssertEx.Equal(4, rows.Length);
            string[] operations = { "vint_0", "vint_0", "resume", "resume" };
            for (int i = 0; i < rows.Length; i++)
            {
                AssertEx.Equal(true, rows[i].Contains("\"sequence\":" + i));
                AssertEx.Equal(true, rows[i].Contains("\"operation\":\"" + operations[i] + "\""));
            }
            AssertEx.Equal(true, rows[2].Contains("\"phase_ordinal\":1"));
            AssertEx.Equal(true, rows[3].Contains("\"phase_ordinal\":2"));
            observer.FlushFrame(43, writer);
            AssertEx.Equal(string.Join("\n", rows) + "\n", writer.ToString());
        }

        private static void NoService()
        {
            var observer = Started();
            var writer = new StringWriter();
            observer.FlushFrame(3, writer);
            AssertEx.Equal("{\"event\":\"load_phase_timing\",\"raw_frame\":3,\"sequence\":0,\"load_ordinal\":1,\"phase_ordinal\":1,\"phase\":\"level_setup\",\"submission_fingerprint\":\"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"operation\":\"hold\"}\n", writer.ToString());
        }

        private static void UnrepresentedPrefix()
        {
            var observer = Started();
            observer.ObserveZeroRoutineInterrupt();
            observer.DiscardUnrepresentedFrame();
            observer.ResumePhase();
            var writer = new StringWriter();
            observer.FlushFrame(0, writer);
            AssertEx.Equal(false, writer.ToString().Contains("vint_0"));
            AssertEx.Equal(true, writer.ToString().Contains("\"phase_ordinal\":1"));
            observer.BeginLoad();
            observer.BeginPhase("display_clear", Fp);
            writer.GetStringBuilder().Clear();
            observer.FlushFrame(1, writer);
            AssertEx.Equal(true, writer.ToString().Contains("\"load_ordinal\":2"));
            AssertEx.Equal(true, writer.ToString().Contains("\"phase_ordinal\":1"));
        }

        private static void OrdinaryHandlerRejected()
        {
            var observer = Started();
            AssertEx.Throws<InvalidOperationException>(observer.ObserveOrdinaryInterrupt, "ordinary interrupt");
            observer.ResumePhase();
            observer.ObserveOrdinaryInterrupt();
        }

        private static void Overflow()
        {
            var observer = Started();
            for (int i = 0; i < 64; i++) observer.ObserveZeroRoutineInterrupt();
            var writer = new StringWriter();
            AssertEx.Throws<InvalidOperationException>(() => observer.FlushFrame(0, writer), "overflow");
            AssertEx.Equal(string.Empty, writer.ToString());
        }

        private static void InvalidIdentity()
        {
            var observer = new InitializationTimingEventEngine();
            AssertEx.Throws<InvalidOperationException>(() => observer.BeginPhase("display_clear", Fp), "no load");
            observer.BeginLoad();
            AssertEx.Throws<ArgumentException>(() => observer.BeginPhase("other", Fp), "unsupported");
            AssertEx.Throws<ArgumentException>(() => observer.BeginPhase("display_clear", "bad"), "fingerprint");
            observer.BeginPhase("display_clear", Fp);
            AssertEx.Throws<InvalidOperationException>(() => observer.BeginPhase("level_setup", Fp), "overlaps");
            AssertEx.Throws<InvalidOperationException>(observer.BeginLoad, "before");
        }

        private static void FingerprintVector()
        {
            byte[] rom = new byte[16];
            for (int i = 0; i < rom.Length; i++) rom[i] = (byte)i;
            var regions = new[] { new InitializationTimingFingerprint.RegionSelection(2, 3),
                new InitializationTimingFingerprint.RegionSelection(12, 2) };
            AssertEx.Equal("sha256:0af7440ad54b63621f8f2792ec056bc43c6d3dafeee757187a30dbe2bc789b74",
                InitializationTimingFingerprint.Compute(rom, "level_data_decode", regions, new[] { 2, 0 }));
            AssertEx.Equal(false,
                InitializationTimingFingerprint.Compute(rom, "level_data_decode", regions, new[] { 2, 0 })
                    == InitializationTimingFingerprint.Compute(rom, "level_setup", regions, new[] { 2, 0 }));
        }

        private static void InitialObjectCuts()
        {
            string path = Environment.GetEnvironmentVariable("S3K_ROM_PATH");
            if (string.IsNullOrEmpty(path)) throw new TestMain.SkipTestException("S3K_ROM_PATH is not set");
            byte[] rom = File.ReadAllBytes(path);
            var host = new FakeS1Host(null);
            using (var observer = new S3kInitializationTimingObserver(host, rom, phase => Fp)) {
                host.Ram[0xFFE9] = 1;
                AssertEx.Throws<InvalidOperationException>(() => host.FireExecuteCallback(0x6000), "competition initialization");
                host.Ram[0xFFE9] = 0;
                host.FireExecuteCallback(0x6000); host.FireExecuteCallback(0x62CC);
                host.FireExecuteCallback(0x6326); host.FireExecuteCallback(0x632C);
                host.FireExecuteCallback(0x6468);
                host.FireExecuteCallback(0x608); // Real placement/ring work, before object execution.
                host.FireExecuteCallback(0x647E);
                AssertEx.Throws<InvalidOperationException>(() => host.FireExecuteCallback(0x608), "unsupported initial-object");
                host.FireExecuteCallback(0x60C80); host.FireExecuteCallback(0x608);
                host.FireExecuteCallback(0x6484); host.FireExecuteCallback(0x608);
                host.FireExecuteCallback(0x648A);
                AssertEx.Throws<InvalidOperationException>(() => host.FireExecuteCallback(0x608), "after animation entry");
                host.FireExecuteCallback(0x64DC);
                host.FireExecuteCallback(0x6000); host.FireExecuteCallback(0x62CC);
                host.FireExecuteCallback(0x6326); host.FireExecuteCallback(0x632C);
                host.FireExecuteCallback(0x6468);
                host.FireExecuteCallback(0x647E);
                AssertEx.Throws<InvalidOperationException>(() => host.FireExecuteCallback(0x608), "unsupported initial-object");
            }
        }

        private static void WriteLong(FakeS1Host host, int address, uint value)
        {
            for (int i = 0; i < 4; i++) host.Ram[(address + i) & 0xFFFF] = (byte)(value >> (24 - 8 * i));
        }

        private static void ReviewedRemainingCallbacks()
        {
            string path = Environment.GetEnvironmentVariable("S3K_ROM_PATH");
            if (string.IsNullOrEmpty(path)) throw new TestMain.SkipTestException("S3K_ROM_PATH is not set");
            byte[] rom = File.ReadAllBytes(path);
            foreach (bool malformedCount in new[] { false, true }) {
                var host = new FakeS1Host(null);
                using (var observer = new S3kInitializationTimingObserver(host, rom, phase => Fp)) {
                    host.FireExecuteCallback(0x6000); host.FireExecuteCallback(0x62CC);
                    host.FireExecuteCallback(0x6326); host.FireExecuteCallback(0x632C);
                    host.FireExecuteCallback(0x6468); host.FireExecuteCallback(0x647E);
                    host.SetCpuRegister("M68K A7", 0xFFFF8000);
                    WriteLong(host, 0x8000 + 32, 0xFFFFB206);
                    WriteLong(host, 0x8000 + 28, malformedCount ? 102U : 103U);
                    WriteLong(host, 0x8000 + 62, 0x1AB08);
                    if (malformedCount) {
                        AssertEx.Throws<InvalidOperationException>(() => host.FireExecuteCallback(0x608), "unsupported initial-object");
                        continue;
                    }
                    // A stale completed-callback observation cannot admit an
                    // interrupt whose actual saved PC is inside that callback.
                    host.SetCpuRegister("M68K A0", 0xFFFFB1BC);
                    host.SetCpuRegister("M68K D7", 103);
                    WriteLong(host, 0xB206, 0x2B928);
                    host.FireExecuteCallback(0x1AB04);
                    WriteLong(host, 0x8000 + 62, 0x2B940);
                    AssertEx.Throws<InvalidOperationException>(() => host.FireExecuteCallback(0x608), "unsupported initial-object");
                    WriteLong(host, 0x8000 + 62, 0x1AB08);
                    host.FireExecuteCallback(0x608);
                    host.SetCpuRegister("M68K A0", 0xFFFFB206);
                    host.SetCpuRegister("M68K A1", 0x2B928);
                    host.FireExecuteCallback(0x1AB02);
                    var output = new StringWriter(); observer.FlushFrame(0, output);
                    AssertEx.Equal(true, output.ToString().Contains("\"phase\":\"initial_presentation\""));
                    // A later newly allocated child invalidates already staged
                    // rows unless its actual target has a reviewed closure.
                    WriteLong(host, 0xB250, 0x123456);
                    host.SetCpuRegister("M68K A0", 0xFFFFB250);
                    host.SetCpuRegister("M68K A1", 0x123456);
                    AssertEx.Throws<InvalidOperationException>(() => host.FireExecuteCallback(0x1AB02), "unreviewed remaining");
                    WriteLong(host, 0xB250, 0x18B3E);
                    host.SetCpuRegister("M68K A1", 0x18B3E);
                    host.FireExecuteCallback(0x1AB02);
                    host.SetCpuRegister("M68K A1", 0x256BE);
                    AssertEx.Throws<InvalidOperationException>(() => host.FireExecuteCallback(0x1AB02), "sampling boundary mismatch");
                    host.FireExecuteCallback(0x6484);
                    // Ordinary gameplay callbacks after Render_Sprites entry
                    // are outside this bounded validation interval.
                    host.FireExecuteCallback(0x1AB02);
                    host.FireExecuteCallback(0x648A); host.FireExecuteCallback(0x64DC);
                    observer.FlushFrame(1, output);
                }
            }
        }

        private static void CappedInitializationRollsBack()
        {
            string romPath = Environment.GetEnvironmentVariable("S3K_ROM_PATH");
            if (string.IsNullOrEmpty(romPath)) throw new TestMain.SkipTestException("S3K_ROM_PATH is not set");
            byte[] rom = File.ReadAllBytes(romPath);
            // Use the actual producer checkout, including standalone worktrees;
            // the legacy consumer-layout TestScratch helper assumes a gitlink.
            string root = Path.Combine(EndToEndTests.TraceChaserRoot, "bizhawk-headless", ".scratch",
                "initialization-cap-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try {
                string moviePath = Path.Combine(root, "two-rows.bk2");
                using (var archive = new ZipArchive(File.Create(moviePath), ZipArchiveMode.Create)) {
                    foreach (string name in new[] { "Header.txt", "SyncSettings.json", "Input Log.txt" }) {
                        using (var writer = new StreamWriter(archive.CreateEntry(name).Open())) {
                            writer.Write(name == "Input Log.txt"
                                ? "[Input]\nLogKey:#Power|Reset|#P1 Up|P1 Down|P1 Left|P1 Right|P1 A|P1 B|P1 C|P1 Start|#P2 Up|P2 Down|P2 Left|P2 Right|P2 A|P2 B|P2 C|P2 Start|\n|..|........|........|\n|..|........|........|\n[/Input]\n"
                                : "{}");
                        }
                    }
                }
                var movie = new Bk2Movie(moviePath) { FrameCount = 2 };
                for (int scenario = 0; scenario < 3; scenario++) {
                    int selected = scenario;
                    string output = Path.Combine(root, "capture-" + scenario);
                    var host = new FakeS1Host((h, frame) => {
                        if (frame == 1) {
                            h.FireExecuteCallback(0x6000);
                            if (selected == 0) return;
                            h.FireExecuteCallback(0x62CC); h.FireExecuteCallback(0x6326);
                            h.FireExecuteCallback(0x632C); h.FireExecuteCallback(0x6468);
                            h.FireExecuteCallback(0x647E);
                            h.SetCpuRegister("M68K A7", 0xFFFF8000);
                            WriteLong(h, 0x803E, 0x1AB08);
                            WriteLong(h, 0x8020, 0xFFFFB206);
                            WriteLong(h, 0x801C, 103);
                            h.FireExecuteCallback(0x608);
                        }
                        if (frame == 2 && selected == 2) {
                            h.SetCpuRegister("M68K A0", 0xFFFFB206);
                            h.SetCpuRegister("M68K A1", 0x2B928);
                            WriteLong(h, 0xB206, 0x2B928);
                            h.FireExecuteCallback(0x1AB02);
                            h.FireExecuteCallback(0x6484); h.FireExecuteCallback(0x648A);
                            h.FireExecuteCallback(0x64DC);
                        }
                    });
                    host.Ram[S3KRam.GameMode] = S3KRam.GameModeLevel;
                    using (var session = new NoReplacePublisher().OpenSession(output))
                    using (var sink = new S3KStagedSegmentSink(session)) {
                        TextWriter timing = sink.OpenInitializationTimingStream();
                        Action capture = () => S3KCompleteRunCaptureRunner.Capture(movie, host,
                            "cap-test", "two-rows.bk2", "2026-11-03", 2, rom, sink, false, timing);
                        if (scenario < 2) {
                            AssertEx.Throws<InvalidOperationException>(capture, "incomplete native initialization");
                        } else {
                            capture(); sink.CompleteRunFiles(); session.Complete().Publish();
                        }
                    }
                    AssertEx.Equal(scenario == 2 ? 1 : 0,
                        Directory.GetFiles(output, "initialization_timing.jsonl", SearchOption.AllDirectories).Length);
                    if (scenario < 2) AssertEx.Equal(0, Directory.GetFiles(output, "*", SearchOption.AllDirectories).Length);
                }
            } finally { Directory.Delete(root, true); }
        }

        private static void EmptyObjectTail()
        {
            string path = Environment.GetEnvironmentVariable("S3K_ROM_PATH");
            if (string.IsNullOrEmpty(path)) throw new TestMain.SkipTestException("S3K_ROM_PATH is not set");
            var host = new FakeS1Host(null);
            using (var observer = new S3kInitializationTimingObserver(host, File.ReadAllBytes(path), phase => Fp)) {
                host.FireExecuteCallback(0x6000); host.FireExecuteCallback(0x62CC);
                host.FireExecuteCallback(0x6326); host.FireExecuteCallback(0x632C);
                host.FireExecuteCallback(0x6468); host.FireExecuteCallback(0x647E);
                int next = S3KRam.PlayerBase + S3KRam.ObjectSlotSize * 100;
                host.SetCpuRegister("M68K A0", (uint)(0xFFFF0000 | (next - S3KRam.ObjectSlotSize)));
                host.SetCpuRegister("M68K D7", 10);
                host.Ram[next + S3KRam.ObjectSlotSize + 3] = 1;
                host.FireExecuteCallback(0x1AB04);
                AssertEx.Throws<InvalidOperationException>(() => host.FireExecuteCallback(0x608), "unsupported initial-object");
                host.Ram[next + S3KRam.ObjectSlotSize + 3] = 0;
                host.SetCpuRegister("M68K A0", (uint)(0xFFFF0000 | (next + 1)));
                host.FireExecuteCallback(0x1AB04);
                AssertEx.Throws<InvalidOperationException>(() => host.FireExecuteCallback(0x608), "unsupported initial-object");
                host.SetCpuRegister("M68K A0", (uint)(0xFFFF0000 | (next - S3KRam.ObjectSlotSize)));
                host.SetCpuRegister("M68K D7", 10);
                host.SetCpuRegister("M68K D7", 11);
                host.FireExecuteCallback(0x1AB04);
                AssertEx.Throws<InvalidOperationException>(() => host.FireExecuteCallback(0x608), "unsupported initial-object");
                host.SetCpuRegister("M68K D7", 10);
                // An IRQ can precede the next LEA/DBF execute callback.
                host.FireExecuteCallback(0x1AB04); host.FireExecuteCallback(0x608);
                host.FireExecuteCallback(0x6484); host.FireExecuteCallback(0x648A); host.FireExecuteCallback(0x64DC);
                var output = new StringWriter(); observer.FlushFrame(0, output);
                AssertEx.Equal(true, output.ToString().Contains("\"phase\":\"initial_presentation\""));
                AssertEx.Equal(false, output.ToString().Contains("saved_a0"));
            }
        }

        private static void NativeHooks()
        {
            var host = new FakeS1Host(null);
            AssertEx.Throws<InvalidDataException>(() => new S3kInitializationTimingObserver(
                host, new byte[16], phase => Fp), "ROM identity");
            AssertEx.Equal(false, host.ExecuteCallbackAddress.HasValue);
            string path = Environment.GetEnvironmentVariable("S3K_ROM_PATH");
            if (string.IsNullOrEmpty(path)) throw new TestMain.SkipTestException("S3K_ROM_PATH is not set");
            byte[] rom = File.ReadAllBytes(path);
            using (var observer = new S3kInitializationTimingObserver(host, rom, phase => Fp))
            {
                host.FireExecuteCallback(0x6468);
                host.FireExecuteCallback(0x64DC); // Shared routines outside a fresh load create no work.
                host.FireExecuteCallback(0x6000);
                var output = new StringWriter();
                observer.FlushFrame(0, output);
                AssertEx.Equal(false, output.ToString().Contains("vint_0"));
                host.FireExecuteCallback(0x584);
                host.FireExecuteCallback(0x608);
                host.FireExecuteCallback(0x62CC);
                host.FireExecuteCallback(0x62CC); // Another title iteration creates no phase.
                host.FireExecuteCallback(0x6326);
                host.FireExecuteCallback(0x632C);
                observer.FlushFrame(1, output);
                string[] lines = output.ToString().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                AssertEx.Equal(5, lines.Length);
                AssertEx.Equal(true, lines[1].Contains("\"operation\":\"vint_0\""));
                AssertEx.Equal(true, lines[2].Contains("\"phase\":\"display_clear\""));
                AssertEx.Equal(true, lines[3].Contains("\"phase\":\"level_data_decode\""));
                AssertEx.Equal(true, lines[4].Contains("\"phase\":\"level_setup\""));
                host.Ram[0xF62A] = 0x14;
                AssertEx.Throws<InvalidOperationException>(() => host.FireExecuteCallback(0x584), "ordinary interrupt");
                host.Ram[0xF62A] = 0;
                host.FireExecuteCallback(0x6468);
                host.FireExecuteCallback(0x647E);
                host.FireExecuteCallback(0x6484);
                host.FireExecuteCallback(0x648A);
                host.FireExecuteCallback(0x64DC);
                observer.FlushFrame(2, output);
            }
            AssertEx.Equal(true, host.ExecuteCallbackDisposed);
        }
    }
}
