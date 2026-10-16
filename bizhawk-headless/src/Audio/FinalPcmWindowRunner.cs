using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BizHawk.Emulation.Cores.Consoles.Sega.gpgx;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>
    /// Diagnostic producer of the BizHawk sync sound provider's final PCM for a
    /// bounded prefix of one BK2 movie, under that movie's own SyncSettings
    /// (FM chip and audio filter included). Each movie row is applied with the
    /// shared input order, the frame is advanced with sound enabled
    /// (<c>AdvanceDiagnosticAudio</c>) and the frame's interleaved stereo s16
    /// samples are drained (<c>DrainDiagnosticAudio</c>, 44.1 kHz).
    ///
    /// Outputs (explicit absolute directory outside this checkout):
    /// <c>pcm.s16</c> (all drained samples, little-endian), <c>frames.tsv</c>
    /// (row, lag flag, stereo frames, cumulative stereo frames) and
    /// <c>metadata.json</c> (ROM SHA-1, movie SHA-256, recorded settings, counts,
    /// PCM and frame-table SHA-256, the loaded core file and managed BizHawk
    /// identity). It is comparison evidence only: it registers no callbacks,
    /// writes no emulator state and selects nothing from what it observes.
    ///
    /// <c>metadata.json</c> is the completion marker. It is written last, by a
    /// temporary file and rename, only after <c>pcm.s16</c> and <c>frames.tsv</c>
    /// are flushed, closed and re-hashed from disk. A directory without it is an
    /// incomplete run, whatever else it contains. Program applies the shared
    /// producer boundary (explicit TraceChaser, consumer and fixture roots) first.
    /// Originating task: OpenGGF round-2 lane pcm-output, 2026-10-16; completion,
    /// identity and boundary fixes from native publication review r1.
    /// </summary>
    internal static class FinalPcmWindowRunner
    {
        internal const string Flag = "--final-pcm-window";

        internal static bool IsRequested(string[] args)
        {
            return args != null && args.Length > 0 && args[0] == Flag;
        }

        internal sealed class Options
        {
            internal string RomPath;
            internal string MoviePath;
            internal string OutputDirectory;
            internal int Frames;
        }

        /// <summary>Every flag, boundary flags included, may appear once.</summary>
        internal static void RejectDuplicateFlags(string[] args)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 1; i < args.Length; i += 2)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException("Arguments must be --name value pairs after " + Flag + ".");
                if (!seen.Add(args[i]))
                    throw new ArgumentException("Duplicate argument " + args[i] + ".");
            }
        }

        internal static Options Parse(string[] args)
        {
            RejectDuplicateFlags(args);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 1; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException("Arguments must be --name value pairs after " + Flag + ".");
                values.Add(args[i], args[i + 1]);
            }
            var options = new Options
            {
                RomPath = Absolute(values, "--rom"),
                MoviePath = Absolute(values, "--movie"),
                OutputDirectory = Absolute(values, "--output")
            };
            string frames;
            if (!values.TryGetValue("--frames", out frames)
                || !int.TryParse(frames, NumberStyles.None, CultureInfo.InvariantCulture, out options.Frames)
                || options.Frames < 1 || options.Frames > 216000)
                throw new ArgumentException("--frames must be an integer in 1..216000.");
            if (values.Count != 4)
                throw new ArgumentException("Only --rom, --movie, --output and --frames are accepted.");
            return options;
        }

        /// <summary>The output may be absent or an empty real directory, never a symlink.</summary>
        internal static void RequireUsableOutput(string output)
        {
            if (LinuxPathEntry.IsSymbolicLink(output))
                throw new IOException("--output must not be a symbolic link: " + output);
            if (File.Exists(output))
                throw new IOException("--output must be a directory: " + output);
            if (Directory.Exists(output) && Directory.GetFileSystemEntries(output).Length > 0)
                throw new IOException("--output must be absent or empty: " + output);
        }

        /// <summary>The core file WaterboxHost actually opens: gpgx.wbx.zst when present, else gpgx.wbx.</summary>
        internal sealed class CoreIdentity
        {
            internal string File;
            internal long Size;
            internal string Sha256;

            internal static CoreIdentity Read(string dllDirectory)
            {
                string zst = Path.Combine(dllDirectory, "gpgx.wbx.zst");
                string path = System.IO.File.Exists(zst) ? zst : Path.Combine(dllDirectory, "gpgx.wbx");
                if (!System.IO.File.Exists(path)) throw new FileNotFoundException("No GPGX core in " + dllDirectory + ".");
                return new CoreIdentity { File = Path.GetFileName(path), Size = new FileInfo(path).Length, Sha256 = Sha256File(path) };
            }

            internal bool SameAs(CoreIdentity other)
            {
                return other != null && File == other.File && Size == other.Size && Sha256 == other.Sha256;
            }
        }

        /// <summary>
        /// Verifies the closed data files against what was streamed, then publishes
        /// metadata.json by temporary file and rename. Any failure leaves no marker.
        /// </summary>
        internal static string CompleteOutputs(string directory, string streamedPcmSha256, long expectedPcmBytes,
            Func<string, string> metadataForFramesSha256)
        {
            string pcmPath = Path.Combine(directory, "pcm.s16");
            string tablePath = Path.Combine(directory, "frames.tsv");
            string marker = Path.Combine(directory, "metadata.json");
            string temporary = Path.Combine(directory, ".metadata.json.partial");
            if (System.IO.File.Exists(marker) || System.IO.File.Exists(temporary))
                throw new IOException("A completion marker already exists in " + directory + ".");
            if (new FileInfo(pcmPath).Length != expectedPcmBytes)
                throw new IOException("pcm.s16 holds " + new FileInfo(pcmPath).Length + " bytes, expected " + expectedPcmBytes + ".");
            string onDisk = Sha256File(pcmPath);
            if (onDisk != streamedPcmSha256)
                throw new IOException("pcm.s16 on disk (" + onDisk + ") differs from the streamed digest (" + streamedPcmSha256 + ").");
            string json = metadataForFramesSha256(Sha256File(tablePath));
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                System.IO.File.Move(temporary, marker);
            }
            finally
            {
                if (System.IO.File.Exists(temporary)) System.IO.File.Delete(temporary);
            }
            return onDisk;
        }

        private static string Absolute(Dictionary<string, string> values, string name)
        {
            string value;
            if (!values.TryGetValue(name, out value) || string.IsNullOrEmpty(value) || !Path.IsPathRooted(value))
                throw new ArgumentException(name + " must be an explicit absolute path.");
            return value;
        }

        /// <summary>Runs one window. Program has already removed and validated the boundary flags.</summary>
        internal static int Run(string[] args, TextWriter stdout)
        {
            Options options = Parse(args);
            RequireUsableOutput(options.OutputDirectory);
            BizHawkInstallation installation = BizHawkInstallation.Validate(Environment.GetEnvironmentVariable("BIZHAWK_HOME"));
            CoreIdentity core = CoreIdentity.Read(installation.DllDirectory);
            System.Reflection.Assembly cores = typeof(GPGX).Assembly;
            string coresVersion = cores.GetName().Version.ToString();
            string coresSha256 = Sha256File(cores.Location);
            Directory.CreateDirectory(options.OutputDirectory);
            byte[] romBytes = File.ReadAllBytes(options.RomPath);
            string romSha1 = RomIdentity.ComputeSha1(romBytes);
            Bk2Movie movie = Bk2Reader.Read(options.MoviePath);
            if (options.Frames > movie.FrameCount)
                throw new ArgumentException("--frames exceeds the movie length " + movie.FrameCount + ".");
            long totalStereoFrames = 0;
            int lagFrames = 0;
            int row = 0;
            string streamed;
            using (SHA256 pcmHash = SHA256.Create())
            {
                using (var pcm = new FileStream(Path.Combine(options.OutputDirectory, "pcm.s16"), FileMode.CreateNew))
                using (var tableFile = new FileStream(Path.Combine(options.OutputDirectory, "frames.tsv"), FileMode.CreateNew))
                using (var table = new StreamWriter(tableFile, new UTF8Encoding(false)))
                {
                    using (GpgxHost host = GpgxHost.Open(options.RomPath, movie.SyncSettings))
                    {
                        table.Write("row\tlag\tstereo_frames\tcumulative_stereo_frames\n");
                        foreach (Bk2Frame frame in movie.OpenFrameStream())
                        {
                            if (row >= options.Frames) break;
                            S1TraceCaptureRunner.ApplyFrame(frame, host);
                            host.AdvanceDiagnosticAudio();
                            int stereoFrames;
                            short[] samples = host.DrainDiagnosticAudio(out stereoFrames);
                            var bytes = new byte[samples.Length * 2];
                            for (int i = 0; i < samples.Length; i++)
                            {
                                bytes[2 * i] = (byte)(samples[i] & 0xFF);
                                bytes[2 * i + 1] = (byte)((samples[i] >> 8) & 0xFF);
                            }
                            pcm.Write(bytes, 0, bytes.Length);
                            pcmHash.TransformBlock(bytes, 0, bytes.Length, null, 0);
                            totalStereoFrames += stereoFrames;
                            if (host.IsLagged) lagFrames++;
                            table.Write(row.ToString(CultureInfo.InvariantCulture) + "\t" + (host.IsLagged ? "1" : "0") + "\t"
                                + stereoFrames.ToString(CultureInfo.InvariantCulture) + "\t"
                                + totalStereoFrames.ToString(CultureInfo.InvariantCulture) + "\n");
                            row++;
                        }
                    }
                    if (row != options.Frames)
                        throw new InvalidDataException("The movie stream ended after " + row + " rows.");
                    table.Flush();
                    tableFile.Flush(true);
                    pcm.Flush(true);
                }
                pcmHash.TransformFinalBlock(new byte[0], 0, 0);
                streamed = Hex(pcmHash.Hash);
            }
            if (!core.SameAs(CoreIdentity.Read(installation.DllDirectory)))
                throw new IOException("The GPGX core file changed during the run.");
            string movieSha256 = Sha256File(options.MoviePath);
            string pcmSha256 = CompleteOutputs(options.OutputDirectory, streamed, totalStereoFrames * 4, framesSha256 => "{\n"
                + "  \"schema\": \"openggf.final-pcm-window.v2\",\n"
                + "  \"rom_sha1\": \"" + romSha1.ToLowerInvariant() + "\",\n"
                + "  \"movie_sha256\": \"" + movieSha256 + "\",\n"
                + "  \"movie_frame_count\": " + movie.FrameCount + ",\n"
                + "  \"frames\": " + options.Frames + ",\n"
                + "  \"lag_frames\": " + lagFrames + ",\n"
                + "  \"genesis_fm_sound_chip\": \"" + movie.SyncSettings.GenesisFMSoundChip + "\",\n"
                + "  \"filter\": \"" + movie.SyncSettings.Filter + "\",\n"
                + "  \"low_pass_range\": " + movie.SyncSettings.LowPassRange + ",\n"
                + "  \"low_freq\": " + movie.SyncSettings.LowFreq + ",\n"
                + "  \"high_freq\": " + movie.SyncSettings.HighFreq + ",\n"
                + "  \"gains_x100\": [" + (short)(movie.SyncSettings.LowGain * 100) + ", "
                + (short)(movie.SyncSettings.MidGain * 100) + ", " + (short)(movie.SyncSettings.HighGain * 100) + "],\n"
                + "  \"sample_rate\": 44100,\n"
                + "  \"stereo_frames\": " + totalStereoFrames + ",\n"
                + "  \"pcm_bytes\": " + (totalStereoFrames * 4) + ",\n"
                + "  \"pcm_sha256\": \"" + streamed + "\",\n"
                + "  \"frames_tsv_sha256\": \"" + framesSha256 + "\",\n"
                + "  \"core_file\": \"" + core.File + "\",\n"
                + "  \"core_size\": " + core.Size + ",\n"
                + "  \"core_sha256\": \"" + core.Sha256 + "\",\n"
                + "  \"bizhawk_managed_version\": \"" + installation.ManagedVersion + "\",\n"
                + "  \"bizhawk_emulation_cores_version\": \"" + coresVersion + "\",\n"
                + "  \"bizhawk_emulation_cores_sha256\": \"" + coresSha256 + "\"\n"
                + "}\n");
            stdout.WriteLine("FINAL_PCM_WINDOW rows=" + row + " stereo_frames=" + totalStereoFrames
                + " lag_frames=" + lagFrames + " pcm_sha256=" + pcmSha256 + " core_sha256=" + core.Sha256);
            return 0;
        }

        internal static string Sha256File(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream input = File.OpenRead(path))
                return Hex(sha.ComputeHash(input));
        }

        private static string Hex(byte[] digest)
        {
            var builder = new StringBuilder(digest.Length * 2);
            foreach (byte b in digest) builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }
    }
}
