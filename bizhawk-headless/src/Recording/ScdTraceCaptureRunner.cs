using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>Main-CPU view of 2M Word RAM, where the Sonic CD stage program runs.</summary>
    public interface IScdWordRamReader
    {
        byte ReadWordRam2MByte(int offset);
    }

    public sealed class ScdTraceCaptureResult
    {
        public ScdTraceCaptureResult(int offset, int rows, ScdTraceStart start)
        {
            Bk2FrameOffset = offset;
            TraceFrameCount = rows;
            Start = start;
        }

        public int Bk2FrameOffset { get; private set; }
        public int TraceFrameCount { get; private set; }
        public ScdTraceStart Start { get; private set; }
    }

    /// <summary>
    /// Sonic CD (USA) stage trace capture (lane K2, 2026-11-12), modelled on
    /// <see cref="S1TraceCaptureRunner"/>. BK2 rows are applied in order. The
    /// recording arms on the first completed frame where the stage engine is
    /// in its main loop: levelStarted = 1 (set just before Level_MainLoop,
    /// src/Level/Main.asm:329), gameMode bit 7 clear (initialization done,
    /// Main.asm:328), the player slot holds ObjSonic (id 1) with
    /// oPlayerMoveLock 0, and neither demoMode nor debugMode is set. As in S1
    /// that detection frame is not recorded; row N is the state after BK2
    /// row (offset + N) is applied and one frame completes. The stage
    /// program's 256-byte MMD header in Word RAM is fingerprinted at arm;
    /// recording stops without a partial row when levelStarted clears, the
    /// level re-initializes, the player object leaves slot 0 or that header
    /// changes (another program was loaded), or at movie end with the S1
    /// Lua-parity rule. Row state is read-only RAM polling after each
    /// FrameAdvance. The one execute hook is <see cref="ScdVIntServiceObserver"/>'s
    /// count of actual V-INT entries, which only feeds the separate
    /// vint_service_timing.jsonl companion (always written, empty when every
    /// row's VBlank was serviced).
    /// </summary>
    public static class ScdTraceCaptureRunner
    {
        public static ScdTraceCaptureResult Capture(
            Bk2Movie movie,
            IGpgxHost host,
            IScdWordRamReader wordRam,
            ScdTraceInputs inputs,
            string recordingDate,
            TextWriter physicsCsv,
            TextWriter auxStateJsonl,
            TextWriter metadataJson,
            TextWriter vintServiceTimingJsonl)
        {
            if (movie == null) throw new ArgumentNullException("movie");
            if (host == null) throw new ArgumentNullException("host");
            if (wordRam == null) throw new ArgumentNullException("wordRam");
            if (inputs == null) throw new ArgumentNullException("inputs");
            if (recordingDate == null) throw new ArgumentNullException("recordingDate");
            if (physicsCsv == null || auxStateJsonl == null || metadataJson == null)
                throw new ArgumentNullException("physicsCsv");
            if (vintServiceTimingJsonl == null) throw new ArgumentNullException("vintServiceTimingJsonl");

            using (IEnumerator<Bk2Frame> frames = movie.OpenFrameStream().GetEnumerator())
            {
                int offset = -1;
                int consumed = 0;
                ScdTraceStart start = null;
                while (offset < 0)
                {
                    if (!frames.MoveNext())
                        throw new InvalidDataException("Start detection never fired: the movie ended after "
                            + consumed + " input rows without reaching the Sonic CD stage main loop.");
                    S1TraceCaptureRunner.ApplyFrame(frames.Current, host);
                    host.Advance();
                    consumed++;
                    RequireCompleted(host, consumed);
                    if (InStageMainLoop(host) && S1Ram.U16(host, ScdRam.DemoMode) == 0
                        && S1Ram.U16(host, ScdRam.DebugMode) == 0
                        && S1Ram.U16(host, ScdRam.PlayerBase + ScdRam.OffPlayerMoveLock) == 0)
                    {
                        offset = host.CompletedFrame;
                        start = new ScdTraceStart
                        {
                            ZoneId = S1Ram.U8(host, ScdRam.Zone),
                            ActRaw = S1Ram.U8(host, ScdRam.Act),
                            TimeZoneRaw = S1Ram.U8(host, ScdRam.TimeZone),
                            GoodFuture = S1Ram.U8(host, ScdRam.GoodFuture),
                            StartX = S1Ram.U16(host, ScdRam.PlayerBase + S1Ram.OffXPos),
                            StartY = S1Ram.U16(host, ScdRam.PlayerBase + S1Ram.OffYPos),
                            RngSeed = S1Ram.U32(host, ScdRam.RngSeed),
                            StageProgramHeaderSha256 = StageProgramHeaderSha256(wordRam)
                        };
                    }
                }

                physicsCsv.Write(ScdTraceCsvWriter.Header);
                physicsCsv.Write('\n');
                var aux = new ScdAuxEventEngine();
                int row = 0;
                // V-INT entry is observed from row 0's frame on; the detection frame is not a row.
                using (ScdVIntServiceObserver vint = ScdVIntServiceObserver.Attach(host, wordRam))
                {
                    while (true)
                    {
                        if ((long)offset + row + 1 >= movie.FrameCount) break;
                        if (!frames.MoveNext())
                            throw new InvalidDataException("Movie input stream ended before row "
                                + (offset + row) + " of " + movie.FrameCount + ".");
                        Bk2Frame frame = frames.Current;
                        S1TraceCaptureRunner.ApplyFrame(frame, host);
                        host.Advance();
                        RequireCompleted(host, offset + row + 1);
                        if (!InStageMainLoop(host)
                            || StageProgramHeaderSha256(wordRam) != start.StageProgramHeaderSha256)
                        {
                            vint.DiscardFrame();
                            break;
                        }
                        physicsCsv.Write(ScdTraceCsvWriter.FormatRow(row, S1InputMask.FromFrame(frame), host));
                        physicsCsv.Write('\n');
                        foreach (string line in aux.ProcessFrame(row, host, host.IsLagged, host.LagCount))
                        {
                            auxStateJsonl.Write(line);
                            auxStateJsonl.Write('\n');
                        }
                        string vintEvent = vint.CloseRow(row);
                        if (vintEvent != null)
                        {
                            vintServiceTimingJsonl.Write(vintEvent);
                            vintServiceTimingJsonl.Write('\n');
                        }
                        row++;
                    }
                }
                metadataJson.Write(ScdTraceMetadataWriter.Format(start, inputs, offset, row, recordingDate));
                return new ScdTraceCaptureResult(offset, row, start);
            }
        }

        /// <summary>Stage engine is past LevelStart initialization with Sonic in slot 0.</summary>
        public static bool InStageMainLoop(IGpgxHost host)
        {
            return S1Ram.U8(host, ScdRam.LevelStarted) == 1
                && (S1Ram.U8(host, ScdRam.GameMode) & 0x80) == 0
                && S1Ram.U8(host, ScdRam.PlayerBase) == ScdRam.PlayerObjectId;
        }

        public static string StageProgramHeaderSha256(IScdWordRamReader wordRam)
        {
            var header = new byte[ScdRam.MmdHeaderBytes];
            for (int i = 0; i < header.Length; i++) header[i] = wordRam.ReadWordRam2MByte(i);
            using (SHA256 sha = SHA256.Create())
                return "sha256:" + BitConverter.ToString(sha.ComputeHash(header)).Replace("-", "").ToLowerInvariant();
        }

        private static void RequireCompleted(IGpgxHost host, int expected)
        {
            if (host.CompletedFrame != expected)
                throw new InvalidOperationException("GPGX CompletedFrame is " + host.CompletedFrame
                    + "; expected " + expected + ".");
        }
    }

    /// <summary>
    /// <c>--scd-trace</c> command: boots the explicit USA disc and BIOS,
    /// replays one BK2 and publishes physics.csv, aux_state.jsonl,
    /// vint_service_timing.jsonl and metadata.json (last) into a new --output directory by staging and
    /// rename. Payloads are left uncompressed: this is a scratch producer,
    /// and publication into a consumer fixture tree is a separate approved
    /// step.
    /// </summary>
    internal static class ScdTraceCommand
    {
        internal const string Flag = "--scd-trace";

        // Sonic CD (USA) user-supplied media identity (media-inventory, 2026-10-23).
        internal const string UsaCueSha256 = "2095ccb49d635333ed0dee8bf12b8b43f61a2330dd2fdd24b6a9189e1195fc7e";
        internal const string UsaDataTrackSha256 = "fed168f4048a8867a6795c98bee1fdffd25dcb33e2bd8ce5913c7780dd9028fc";
        // Sega CD Model 1 US v1.10 (BizHawk FirmwareDatabase GEN+CD_BIOS_US, "Acceptable").
        internal const string UsaBiosSha1 = "f4f315adcef9b8feb0364c21ab7f0eaf5457f3ed";
        internal const string UsaFirmwareId = "GEN+CD_BIOS_US";

        internal static bool IsRequested(string[] args)
        {
            return args != null && args.Length > 0 && args[0] == Flag;
        }

        internal static int Run(string[] args, TextWriter stdout)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 1; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length || !args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException("Arguments must be --name value pairs after " + Flag + ".");
                values.Add(args[i], args[i + 1]);
            }
            string cue = Absolute(values, "--cue");
            string bios = Absolute(values, "--bios");
            string moviePath = Absolute(values, "--movie");
            string output = Absolute(values, "--output");
            if (values.Count != 4)
                throw new ArgumentException("Only --cue, --bios, --movie and --output are accepted.");
            if (Directory.Exists(output) || File.Exists(output) || LinuxPathEntry.IsSymbolicLink(output))
                throw new IOException("--output must not already exist: " + output);

            var inputs = new ScdTraceInputs
            {
                CueSha256 = Sha256File(cue),
                DataTrackSha256 = Sha256File(DataTrackPath(cue)),
                BiosSha1 = UsaBiosSha1,
                MovieSha256 = Sha256File(moviePath)
            };
            if (inputs.CueSha256 != UsaCueSha256 || inputs.DataTrackSha256 != UsaDataTrackSha256)
                throw new InvalidDataException("Disc is not the pinned Sonic CD (USA) image (cue "
                    + inputs.CueSha256 + ", data track " + inputs.DataTrackSha256 + ").");
            Bk2Movie movie = Bk2Reader.Read(moviePath);

            string parent = Path.GetDirectoryName(output);
            string staging = Path.Combine(parent, "." + Path.GetFileName(output) + ".partial");
            if (Directory.Exists(staging)) throw new IOException("Staging directory already exists: " + staging);
            Directory.CreateDirectory(staging);
            ScdTraceCaptureResult result;
            var encoding = new UTF8Encoding(false);
            using (GpgxHost host = GpgxHost.OpenMegaCd(cue, bios, UsaFirmwareId, UsaBiosSha1, movie.SyncSettings))
            using (var physics = new StreamWriter(Path.Combine(staging, "physics.csv"), false, encoding))
            using (var aux = new StreamWriter(Path.Combine(staging, "aux_state.jsonl"), false, encoding))
            using (var vint = new StreamWriter(Path.Combine(staging, ScdVIntServiceObserver.FileName), false, encoding))
            {
                var metadata = new StringWriter(CultureInfo.InvariantCulture);
                result = ScdTraceCaptureRunner.Capture(movie, host, host, inputs,
                    DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    physics, aux, metadata, vint);
                physics.Flush();
                aux.Flush();
                vint.Flush();
                File.WriteAllText(Path.Combine(staging, "metadata.json"), metadata.ToString(), encoding);
            }
            Directory.Move(staging, output);
            stdout.WriteLine("SCD_TRACE bk2_frame_offset=" + result.Bk2FrameOffset
                + " trace_frame_count=" + result.TraceFrameCount
                + " stage_file=" + ScdTraceMetadataWriter.StageFile(result.Start.ZoneId,
                    result.Start.ActRaw, result.Start.TimeZoneRaw, result.Start.GoodFuture)
                + " output=" + output);
            return 0;
        }

        /// <summary>The first FILE of the CUE, resolved inside the CUE's directory.</summary>
        internal static string DataTrackPath(string cue)
        {
            foreach (string line in File.ReadAllLines(cue))
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith("FILE \"", StringComparison.OrdinalIgnoreCase)) continue;
                int close = trimmed.IndexOf('"', 6);
                if (close < 0) break;
                string name = trimmed.Substring(6, close - 6);
                if (Path.IsPathRooted(name) || name.Contains("/") || name.Contains(".."))
                    throw new InvalidDataException("CUE track must stay in the CUE directory: " + name);
                return Path.Combine(Path.GetDirectoryName(cue), name);
            }
            throw new InvalidDataException("CUE has no FILE entry: " + cue);
        }

        private static string Absolute(Dictionary<string, string> values, string name)
        {
            string value;
            if (!values.TryGetValue(name, out value) || !Path.IsPathRooted(value))
                throw new ArgumentException(name + " must be an explicit absolute path.");
            return value;
        }

        private static string Sha256File(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
