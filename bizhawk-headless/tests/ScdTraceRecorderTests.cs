using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace OpenGGF.BizHawk.Headless.Tests
{
    /// <summary>
    /// Sonic CD stage recorder (lane K2, 2026-11-12): RAM sources, stage-file
    /// naming, arm predicate, row phase and stop rules. Expected values are
    /// literals derived from the scddisasm labels cited in ScdRam, not from
    /// running the recorder.
    /// </summary>
    internal static class ScdTraceRecorderTests
    {
        private const string Sync =
            "{\"o\":{\"$type\":\"BizHawk.Emulation.Cores.Consoles.Sega.gpgx.GPGX+GPGXSyncSettings, BizHawk.Emulation.Cores\","
            + "\"UseSixButton\":false,\"ControlTypeLeft\":1,\"ControlTypeRight\":1,\"Region\":0,\"ForceVDP\":0,"
            + "\"LoadBIOS\":false,\"Overscan\":0,\"GGExtra\":false,\"SMSFMSoundChip\":1,\"GenesisFMSoundChip\":0,"
            + "\"Filter\":1,\"LowPassRange\":26214,\"LowFreq\":880,\"HighFreq\":5000,\"LowGain\":1.0,"
            + "\"MidGain\":1.0,\"HighGain\":1.0,\"BackdropColor\":4294902015,\"SpritesAlwaysOnTop\":false}}";
        private const string LogKey =
            "LogKey:#Power|Reset|#P1 Up|P1 Down|P1 Left|P1 Right|P1 A|P1 B|P1 C|P1 Start|"
            + "#P2 Up|P2 Down|P2 Left|P2 Right|P2 A|P2 B|P2 C|P2 Start|";

        public static void Register(ICollection<TestMain.TestCase> tests)
        {
            tests.Add(new TestMain.TestCase("ScdTraceCsvWriter reads the Sonic CD stage globals", RowReadsCdGlobals));
            tests.Add(new TestMain.TestCase("ScdTraceMetadataWriter names disc stage files", NamesStageFiles));
            tests.Add(new TestMain.TestCase("ScdTraceCaptureRunner arms after LevelStart and stops when it re-enters", ArmsAndStops));
            tests.Add(new TestMain.TestCase("ScdTraceCaptureRunner stops when the stage program changes", StopsOnProgramChange));
            tests.Add(new TestMain.TestCase("ScdAuxEventEngine maps the CD object spawner cursor", AuxMapsSpawnerCursor));
        }

        private static void RowReadsCdGlobals()
        {
            var host = new RamBackedHost();
            host.SetWord(ScdRam.CameraX, 0x035C);
            host.SetWord(ScdRam.CameraY, 0x0710);
            host.SetWord(ScdRam.Rings, 0x0001);
            host.SetWord(ScdRam.LevelFrames, 0x0181);
            host.SetLong(ScdRam.LevelVIntCounter, 0x000101BE);
            int p = ScdRam.PlayerBase;
            host.SetWord(p + 0x08, 0x03BC);
            host.SetWord(p + 0x0A, 0x2D00);
            host.SetWord(p + 0x0C, 0x077D);
            host.SetWord(p + 0x10, 0x05C4);
            host.SetWord(p + 0x12, 0x025F);
            host.SetWord(p + 0x14, 0x0642);
            host.Ram[p + 0x1A] = 0x58;
            host.Ram[p + 0x1C] = 0x00;
            host.Ram[p + 0x22] = 0x06; // in air + rolling
            host.Ram[p + 0x24] = 0x02;
            host.Ram[p + 0x26] = 0x10;
            host.Ram[p + 0x3D] = 0x21;
            // S1's ring/frame/vblank addresses must not be the CD sources.
            host.SetWord(S1Ram.RingCount, 0xAAAA);
            host.SetWord(S1Ram.FrameCount, 0xBBBB);
            host.SetWord(S1Ram.VblankWord, 0xCCCC);
            AssertEx.Equal(
                "018E,0008,035C,0710,0001,0181,01BE,0000,1,03BC,077D,05C4,025F,0642,10,1,1,0,2D00,0000,02,06,21,00,58,"
                + "0,0000,0000,0000,0000,0000,00,0,0,0,0000,0000,00,00,00,00,00",
                ScdTraceCsvWriter.FormatRow(0x018E, 0x0008, host));
            AssertEx.Equal(42, ScdTraceCsvWriter.FormatRow(0, 0, host).Split(',').Length);
        }

        private static void NamesStageFiles()
        {
            AssertEx.Equal("R11A", ScdTraceMetadataWriter.StageFile(0, 0, 1, 0));
            AssertEx.Equal("R11B", ScdTraceMetadataWriter.StageFile(0, 0, 0, 0));
            AssertEx.Equal("R13C", ScdTraceMetadataWriter.StageFile(0, 2, 2, 1));
            AssertEx.Equal("R42D", ScdTraceMetadataWriter.StageFile(2, 1, 2, 0));
            AssertEx.Equal("R82A", ScdTraceMetadataWriter.StageFile(6, 1, 0x81, 0));
            AssertEx.Equal("present", ScdTraceMetadataWriter.TimeZoneName(0x81));
            AssertEx.Equal("ttz", ScdTraceMetadataWriter.ZoneName(2));
        }

        /// <summary>
        /// Scripted stage: frames 1-3 are a non-stage program whose stale
        /// levelStarted is 1 but the player slot is empty; frame 4 is in
        /// LevelStart (gameMode bit 7); frame 5 enters the main loop and is
        /// the unrecorded detection frame; frames 6-8 are rows 0-2; frame 9
        /// re-enters LevelStart (levelStarted 0) and ends the recording.
        /// </summary>
        private static void ArmsAndStops()
        {
            var host = new FakeS1Host((h, frame) =>
            {
                Array.Clear(h.Ram, 0, h.Ram.Length);
                h.Ram[ScdRam.LevelStarted] = (byte)(frame == 9 ? 0 : 1);
                h.Ram[ScdRam.GameMode] = (byte)(frame == 4 ? 0x80 : 0);
                h.Ram[ScdRam.PlayerBase] = (byte)(frame >= 4 ? 1 : 0);
                h.Ram[ScdRam.Zone] = 0;
                h.Ram[ScdRam.TimeZone] = 1;
                h.Ram[ScdRam.LevelFrames + 1] = (byte)(frame >= 5 ? frame - 4 : 0);
                h.Ram[ScdRam.PlayerBase + 0x09] = (byte)(0x40 + frame);
                h.IsLagged = frame == 7;
                h.LagCount = frame >= 7 ? 1 : 0;
            });
            var wordRam = new FakeWordRam();
            WithMovie(12, movie =>
            {
                var physics = new StringWriter();
                var aux = new StringWriter();
                var metadata = new StringWriter();
                ScdTraceCaptureResult result = ScdTraceCaptureRunner.Capture(movie, host, wordRam,
                    Inputs(), "2026-11-12", physics, aux, metadata);
                AssertEx.Equal(5, result.Bk2FrameOffset);
                AssertEx.Equal(3, result.TraceFrameCount);
                string[] lines = physics.ToString().Split('\n');
                AssertEx.Equal(5, lines.Length); // header, three rows, trailing empty
                AssertEx.Equal("0000", lines[1].Split(',')[0]);
                AssertEx.Equal("0046", lines[1].Split(',')[9]); // frame 6 X
                AssertEx.Equal("0002", lines[1].Split(',')[5]); // levelFrames on frame 6
                AssertEx.Equal("0048", lines[3].Split(',')[9]);
                string[] lags = aux.ToString().Split('\n').Where(l => l.Contains("\"lag_state\"")).ToArray();
                AssertEx.Equal(3, lags.Length);
                AssertEx.Equal("{\"frame\":1,\"event\":\"lag_state\",\"lagged\":true,\"lagcount\":1}", lags[1]);
                string json = metadata.ToString();
                AssertEx.Equal(true, json.Contains("\"game\": \"scd\""));
                AssertEx.Equal(true, json.Contains("\"stage_file\": \"R11A\""));
                AssertEx.Equal(true, json.Contains("\"bk2_frame_offset\": 5,"));
                AssertEx.Equal(true, json.Contains("\"trace_frame_count\": 3,"));
                AssertEx.Equal(true, json.Contains("\"start_x\": \"0x0045\""));
                AssertEx.Equal(9, host.CompletedFrame);
            });
        }

        private static void StopsOnProgramChange()
        {
            var wordRam = new FakeWordRam();
            var host = new FakeS1Host((h, frame) =>
            {
                h.Ram[ScdRam.LevelStarted] = 1;
                h.Ram[ScdRam.PlayerBase] = 1;
                if (frame == 4) wordRam.Header[0x0A] ^= 0xFF; // a new MMD entry address
            });
            WithMovie(10, movie =>
            {
                ScdTraceCaptureResult result = ScdTraceCaptureRunner.Capture(movie, host, wordRam,
                    Inputs(), "2026-11-12", new StringWriter(), new StringWriter(), new StringWriter());
                AssertEx.Equal(1, result.Bk2FrameOffset);
                AssertEx.Equal(2, result.TraceFrameCount);
            });
        }

        private static void AuxMapsSpawnerCursor()
        {
            var host = new RamBackedHost();
            host.SetWord(ScdRam.ObjPrevChunk, 0x0080);
            host.SetLong(ScdRam.ObjChunkRight, 0x0020796E);
            host.SetLong(ScdRam.ObjChunkLeft, 0x00207906);
            host.Ram[ScdRam.SavedObjFlags] = 11;
            host.Ram[ScdRam.SavedObjFlags + 1] = 1;
            host.SetWord(ScdRam.LevelFrames, 265);
            IList<string> lines = new ScdAuxEventEngine().ProcessFrame(278, host, false, 3);
            AssertEx.Equal(
                "{\"frame\":278,\"vfc\":265,\"event\":\"cursor_state\",\"opl_screen\":\"0x0080\","
                + "\"fwd_ptr\":\"0x0020796E\",\"bwd_ptr\":\"0x00207906\",\"fwd_ctr\":11,\"bwd_ctr\":1,\"dir\":\"R\"}",
                lines[lines.Count - 1]);
            AssertEx.Equal("{\"frame\":278,\"event\":\"lag_state\",\"lagged\":false,\"lagcount\":3}",
                lines[lines.Count - 2]);
        }

        private static ScdTraceInputs Inputs()
        {
            return new ScdTraceInputs { CueSha256 = "c", DataTrackSha256 = "d", BiosSha1 = "b", MovieSha256 = "m" };
        }

        private static void WithMovie(int rows, Action<Bk2Movie> body)
        {
            string directory = TestScratch.CreateRootPath("scd-trace");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "synthetic.bk2");
            try
            {
                using (var stream = File.Create(path))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, false))
                {
                    Write(archive, "Header.txt", "MovieVersion BizHawk v2.0.0\nCore Genplus-gx\nPlatform GEN\n");
                    Write(archive, "SyncSettings.json", Sync);
                    Write(archive, "Input Log.txt", "[Input]\n" + LogKey + "\n"
                        + string.Join("\n", Enumerable.Repeat("|..|...R....|........|", rows)) + "\n[/Input]\n");
                }
                body(Bk2Reader.Read(path));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static void Write(ZipArchive archive, string name, string content)
        {
            using (var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false)))
                writer.Write(content);
        }

        private sealed class FakeWordRam : IScdWordRamReader
        {
            internal readonly byte[] Header = new byte[ScdRam.MmdHeaderBytes];

            public byte ReadWordRam2MByte(int offset)
            {
                return offset < Header.Length ? Header[offset] : (byte)0;
            }
        }
    }
}
