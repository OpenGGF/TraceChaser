using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

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
    /// PCM SHA-256). It is comparison evidence only: it registers no callbacks,
    /// writes no emulator state and selects nothing from what it observes.
    /// Originating task: OpenGGF round-2 lane pcm-output, 2026-10-16.
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

        internal static Options Parse(string[] args, string harnessRoot)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 1; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length || !args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException("Arguments must be --name value pairs after " + Flag + ".");
                values[args[i]] = args[i + 1];
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
            if (harnessRoot != null)
            {
                string root = Path.GetFullPath(harnessRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string output = Path.GetFullPath(options.OutputDirectory) + Path.DirectorySeparatorChar;
                if (output.StartsWith(root, StringComparison.Ordinal))
                    throw new ArgumentException("--output must be outside the TraceChaser checkout.");
            }
            return options;
        }

        private static string Absolute(Dictionary<string, string> values, string name)
        {
            string value;
            if (!values.TryGetValue(name, out value) || string.IsNullOrEmpty(value) || !Path.IsPathRooted(value))
                throw new ArgumentException(name + " must be an explicit absolute path.");
            return value;
        }

        internal static int Run(string[] args, TextWriter stdout, string harnessRoot)
        {
            Options options = Parse(args, harnessRoot);
            if (Directory.Exists(options.OutputDirectory) && Directory.GetFileSystemEntries(options.OutputDirectory).Length > 0)
                throw new IOException("--output must be absent or empty: " + options.OutputDirectory);
            Directory.CreateDirectory(options.OutputDirectory);
            byte[] romBytes = File.ReadAllBytes(options.RomPath);
            string romSha1 = RomIdentity.ComputeSha1(romBytes);
            Bk2Movie movie = Bk2Reader.Read(options.MoviePath);
            if (options.Frames > movie.FrameCount)
                throw new ArgumentException("--frames exceeds the movie length " + movie.FrameCount + ".");
            long totalStereoFrames = 0;
            int lagFrames = 0;
            using (SHA256 pcmHash = SHA256.Create())
            using (var pcm = new FileStream(Path.Combine(options.OutputDirectory, "pcm.s16"), FileMode.CreateNew))
            using (var table = new StreamWriter(Path.Combine(options.OutputDirectory, "frames.tsv"), false, new UTF8Encoding(false)))
            using (GpgxHost host = GpgxHost.Open(options.RomPath, movie.SyncSettings))
            {
                table.Write("row\tlag\tstereo_frames\tcumulative_stereo_frames\n");
                int row = 0;
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
                if (row != options.Frames)
                    throw new InvalidDataException("The movie stream ended after " + row + " rows.");
                pcmHash.TransformFinalBlock(new byte[0], 0, 0);
                string json = "{\n"
                    + "  \"schema\": \"openggf.final-pcm-window.v1\",\n"
                    + "  \"rom_sha1\": \"" + romSha1.ToLowerInvariant() + "\",\n"
                    + "  \"movie_sha256\": \"" + Sha256File(options.MoviePath) + "\",\n"
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
                    + "  \"pcm_sha256\": \"" + Hex(pcmHash.Hash) + "\"\n"
                    + "}\n";
                File.WriteAllText(Path.Combine(options.OutputDirectory, "metadata.json"), json, new UTF8Encoding(false));
                stdout.WriteLine("FINAL_PCM_WINDOW rows=" + row + " stereo_frames=" + totalStereoFrames
                    + " lag_frames=" + lagFrames + " pcm_sha256=" + Hex(pcmHash.Hash));
            }
            return 0;
        }

        private static string Sha256File(string path)
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
