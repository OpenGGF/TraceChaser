using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>
    /// Diagnostic 68000 CPU-ledger window (stall ABI v2, native patch 0005). It starts from an explicit ingress
    /// savestate, advances frames with no input, and drains the native ledger only between FrameAdvance calls:
    /// completed VInt-to-VInt intervals (per-PC instruction counts and MCLK in main and interrupt context, classified
    /// stall MCLK, refresh, acceptance costs) and every interrupt acceptance. It registers no callback, selects no
    /// address or range and writes no emulated state.
    ///
    /// The ingress savestate and its provenance come from outside TraceChaser. For OpenGGF's S3K ending research it
    /// is a declared-setup state produced by a consumer-side tool; its provenance must say so
    /// (<c>ingress_kind</c>), and outputs carry that label. Authentic movie-only coverage is a separate campaign.
    ///
    /// Fail closed: any sticky native flag, a savestate load after enable, a missing, empty, out-of-order or
    /// mis-joined interval, more than one VInt in a frame, or a core/ingress identity mismatch stops the run before
    /// <c>metadata.json</c>, which is written last by temporary file and rename and is the completion marker.
    /// Originating task: OpenGGF S3K Kos CPU-budget research (stall ABI v2), 2026-10-17.
    /// </summary>
    internal static class M68kLedgerWindowRunner
    {
        internal const string Flag = "--m68k-ledger-window";
        private const string S3kRomSha1 = "cfbf98c36c776677290a872547ac47c53d2761d6";
        private const int S3kLevelFrameCounter = 0xFE04;   // Level_frame_counter, a frame label only

        /// <summary>The only ingress kind accepted until an authentic (movie-only) ingress producer exists.</summary>
        internal const string DeclaredSetupIngress = "declared_setup";

        /// <summary>
        /// Checks the ingress provenance against the actual inputs and returns the region the state was produced
        /// under ("ntsc" keeps the movie's SyncSettings region; "pal" forces Europe, exactly as the producer did).
        /// </summary>
        internal static string ValidateIngress(JObject provenance, string stateSha256, string coreSha256,
            string romSha1, string movieSha256)
        {
            if (provenance == null) throw new InvalidDataException("Ingress provenance is missing.");
            string kind = (string)provenance["ingress_kind"];
            if (kind != DeclaredSetupIngress)
                throw new InvalidDataException("Ingress kind " + (kind ?? "(absent)") + " is not accepted; only "
                    + DeclaredSetupIngress + " ingress exists.");
            if ((string)provenance["state_sha256"] != stateSha256) throw new InvalidDataException("Ingress state SHA-256 differs from its provenance.");
            if ((string)provenance["core_sha256"] != coreSha256) throw new InvalidDataException("Ingress state was produced on another core.");
            if ((string)provenance["rom_sha1"] != romSha1) throw new InvalidDataException("Ingress state was produced from another ROM.");
            if ((string)provenance["movie_sha256"] != movieSha256) throw new InvalidDataException("Ingress state was produced from another movie.");
            string region = (string)provenance["region"];
            if (region != "ntsc" && region != "pal") throw new InvalidDataException("Ingress provenance region must be ntsc or pal.");
            return region;
        }

        private static void WriteDurable(string path, byte[] bytes)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        internal static bool IsRequested(string[] args)
        {
            return args != null && args.Length > 0 && args[0] == Flag;
        }

        internal sealed class Options
        {
            internal string RomPath, MoviePath, IngressState, IngressProvenance, OutputDirectory;
            internal int Frames;
            internal bool Ledger = true;
            internal int SaveStateWhenLabelExceeds = -1;
            internal int AudioLabelLo = -1, AudioLabelHi = -1;
        }

        internal static Options Parse(string[] args)
        {
            FinalPcmWindowRunner.RejectDuplicateFlags(args);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 1; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length) throw new ArgumentException("Arguments must be --name value pairs after " + Flag + ".");
                values.Add(args[i], args[i + 1]);
            }
            var o = new Options
            {
                RomPath = Absolute(values, "--rom"),
                MoviePath = Absolute(values, "--movie"),
                IngressState = Absolute(values, "--ingress-state"),
                IngressProvenance = Absolute(values, "--ingress-provenance"),
                OutputDirectory = Absolute(values, "--output"),
                Frames = Bounded(values, "--frames", 1, 216000)
            };
            int known = 6;
            string ledger;
            if (values.TryGetValue("--ledger", out ledger))
            {
                if (ledger != "on" && ledger != "off") throw new ArgumentException("--ledger must be on or off.");
                o.Ledger = ledger == "on";
                known++;
            }
            if (values.ContainsKey("--save-state-when-label-exceeds"))
            {
                o.SaveStateWhenLabelExceeds = Bounded(values, "--save-state-when-label-exceeds", 0, 65535);
                known++;
            }
            string window;
            if (values.TryGetValue("--audio-label-window", out window))
            {
                string[] parts = window.Split('-');
                int lo, hi;
                if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out lo)
                    || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out hi) || hi < lo || hi > 65535)
                    throw new ArgumentException("--audio-label-window must be LO-HI.");
                o.AudioLabelLo = lo; o.AudioLabelHi = hi;
                known++;
            }
            if (values.Count != known) throw new ArgumentException("Unknown argument for " + Flag + ".");
            return o;
        }

        private static string Absolute(Dictionary<string, string> values, string name)
        {
            string value;
            if (!values.TryGetValue(name, out value) || string.IsNullOrEmpty(value) || !Path.IsPathRooted(value))
                throw new ArgumentException(name + " must be an explicit absolute path.");
            return value;
        }

        private static int Bounded(Dictionary<string, string> values, string name, int lo, int hi)
        {
            string text; int value;
            if (!values.TryGetValue(name, out text) || !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)
                || value < lo || value > hi)
                throw new ArgumentException(name + " must be an integer in " + lo + ".." + hi + ".");
            return value;
        }

        /// <summary>Checks one closed interval against the previous one; returns the next expected sequence.</summary>
        internal static void RequireJoined(GpgxM68kStall2Bank bank, GpgxM68kStall2Bank? previous, uint expectedSequence)
        {
            if (bank.Sequence != expectedSequence)
                throw new InvalidDataException("Interval " + bank.Sequence + " arrived where " + expectedSequence + " was expected.");
            if (bank.Entries == 0 || bank.Executed == 0)
                throw new InvalidDataException("Interval " + bank.Sequence + " is empty.");
            if (bank.CloseStampMclk <= bank.OpenStampMclk)
                throw new InvalidDataException("Interval " + bank.Sequence + " does not advance in time.");
            if (previous == null)
            {
                if (bank.OpenedByVint != 0 || bank.Sequence != 0)
                    throw new InvalidDataException("The first interval must be the partial one opened by enable.");
                return;
            }
            GpgxM68kStall2Bank p = previous.Value;
            if (bank.OpenedByVint != 1 || bank.OpenAcceptOrdinal != p.CloseAcceptOrdinal || bank.OpenStampMclk != p.CloseStampMclk)
                throw new InvalidDataException("Interval " + bank.Sequence + " does not join the previous interval.");
            if ((p.StraddlePc == 0xFFFFFFFF) != (bank.CarryPc == 0xFFFFFFFF) || (p.StraddlePc != 0xFFFFFFFF && p.StraddlePc != bank.CarryPc))
                throw new InvalidDataException("Interval " + bank.Sequence + " carry does not match the previous straddle.");
        }

        internal static int Run(string[] args, TextWriter stdout)
        {
            Options o = Parse(args);
            FinalPcmWindowRunner.RequireUsableOutput(o.OutputDirectory);
            BizHawkInstallation installation = BizHawkInstallation.Validate(Environment.GetEnvironmentVariable("BIZHAWK_HOME"));
            FinalPcmWindowRunner.CoreIdentity core = FinalPcmWindowRunner.CoreIdentity.Read(installation.DllDirectory);
            byte[] romBytes = File.ReadAllBytes(o.RomPath);
            string romSha1 = RomIdentity.ComputeSha1(romBytes).ToLowerInvariant();
            byte[] state = File.ReadAllBytes(o.IngressState);
            string stateSha256 = Hex(SHA256.Create().ComputeHash(state));
            JObject provenance = JObject.Parse(File.ReadAllText(o.IngressProvenance));
            string movieSha256 = FinalPcmWindowRunner.Sha256File(o.MoviePath);
            string region = ValidateIngress(provenance, stateSha256, core.Sha256, romSha1, movieSha256);
            string ingressKind = DeclaredSetupIngress;
            string hostSha256 = FinalPcmWindowRunner.Sha256File(typeof(M68kLedgerWindowRunner).Assembly.Location);
            bool s3k = romSha1 == S3kRomSha1;
            Bk2Movie movie = Bk2Reader.Read(o.MoviePath);
            if (region == "pal")
                movie.SyncSettings.Region = global::BizHawk.Emulation.Cores.Consoles.Sega.gpgx.LibGPGX.Region.Europe;
            Directory.CreateDirectory(o.OutputDirectory);
            string dir = o.OutputDirectory;
            uint expectedSequence = 0, intervals = 0, accepts = 0;
            GpgxM68kStall2Bank? previous = null;
            long audioFrames = 0;
            string audioSha;
            using (SHA256 audioHash = SHA256.Create())
            {
                using (var frames = Writer(Path.Combine(dir, "frames.jsonl")))
                using (var intervalOut = Writer(Path.Combine(dir, "intervals.jsonl")))
                using (var slotOut = Writer(Path.Combine(dir, "interval-slots.csv")))
                using (var acceptOut = Writer(Path.Combine(dir, "accepts.jsonl")))
                using (var audioWindow = new FileStream(Path.Combine(dir, "audio-window.s16"), FileMode.CreateNew))
                using (GpgxHost host = GpgxHost.Open(o.RomPath, movie.SyncSettings))
                {
                    host.LoadSavestate(state);
                    GpgxM68kStall2Native ledger = GpgxM68kStall2Native.Bind(host);
                    if (o.Ledger) ledger.Enable();
                    slotOut.Write("sequence,pc,context,count,mclk\n");
                    bool saved = false;
                    for (int f = 0; f < o.Frames; f++)
                    {
                        host.ClearButtons();
                        host.AdvanceDiagnosticAudio();
                        int stereo;
                        short[] samples = host.DrainDiagnosticAudio(out stereo);
                        var pcm = new byte[samples.Length * 2];
                        Buffer.BlockCopy(samples, 0, pcm, 0, pcm.Length);
                        audioHash.TransformBlock(pcm, 0, pcm.Length, null, 0);
                        audioFrames += stereo;
                        int label = s3k ? (host.ReadMainRamByte(S3kLevelFrameCounter) << 8 | host.ReadMainRamByte(S3kLevelFrameCounter + 1)) : -1;
                        if (o.AudioLabelLo >= 0 && label >= o.AudioLabelLo && label <= o.AudioLabelHi) audioWindow.Write(pcm, 0, pcm.Length);
                        if (o.SaveStateWhenLabelExceeds >= 0 && !saved && label > o.SaveStateWhenLabelExceeds)
                        {
                            WriteDurable(Path.Combine(dir, "state-after-label.bin"), host.CloneSavestate());
                            saved = true;
                        }
                        string frameRow = "{\"frame\": " + f + ", \"completed_frame\": " + host.CompletedFrame + ", \"label\": " + label;
                        if (o.Ledger)
                        {
                            GpgxM68kStall2Frame nf = ledger.ReadFrame();
                            if (nf.Flags != 0) throw new InvalidDataException("Native ledger flags 0x" + nf.Flags.ToString("X") + " at frame " + f + ".");
                            GpgxM68kStall2Accept[] taken = ledger.TakeAccepts();
                            int vints = 0;
                            foreach (GpgxM68kStall2Accept a in taken)
                            {
                                if (a.Ordinal != accepts) throw new InvalidDataException("Acceptance ordinal gap at " + a.Ordinal + ".");
                                accepts++;
                                if (a.Level == 6) vints++;
                                acceptOut.Write("{\"ordinal\": " + a.Ordinal + ", \"level\": " + a.Level + ", \"inside_instruction\": "
                                    + a.InsideInstruction + ", \"table_index\": " + a.TableIndex + ", \"cycles\": " + a.Cycles
                                    + ", \"return_pc\": " + a.ReturnPc + ", \"stamp_mclk\": " + a.StampMclk + ", \"frame\": " + f
                                    + ", \"label\": " + label + ", \"closes_bank_sequence\": " + (a.ClosesBankSequence == 0xFFFFFFFF ? -1 : (long)a.ClosesBankSequence) + "}\n");
                            }
                            if (vints > 1) throw new InvalidDataException(vints + " VInt acceptances in frame " + f + ".");
                            GpgxM68kStall2Bank bank; GpgxM68kStall2Slot[] slots;
                            while (ledger.TryTakeBank(out bank, out slots))
                            {
                                RequireJoined(bank, previous, expectedSequence);
                                expectedSequence++;
                                previous = bank;
                                if (bank.OpenedByVint == 1) intervals++;
                                intervalOut.Write(IntervalJson(bank, f, label) + "\n");
                                foreach (GpgxM68kStall2Slot s in slots)
                                    slotOut.Write(bank.Sequence + "," + s.Pc + "," + s.Context + "," + s.Count + "," + s.Mclk + "\n");
                            }
                            frameRow += ", \"vint_accepts\": " + vints + ", \"stamp_mclk\": " + nf.StampMclk + ", \"context\": " + nf.Context;
                        }
                        frames.Write(frameRow + "}\n");
                    }
                    if (o.SaveStateWhenLabelExceeds >= 0 && !saved) throw new InvalidDataException("The label never exceeded the save threshold.");
                    WriteDurable(Path.Combine(dir, "state-end.bin"), host.CloneSavestate());
                    if (o.Ledger)
                    {
                        if (host.LoadGeneration != 1) throw new InvalidDataException("A savestate was loaded during the window.");
                        ledger.Disable();
                    }
                    foreach (var w in new StreamWriter[] { frames, intervalOut, slotOut, acceptOut })
                    {
                        w.Flush();
                        ((FileStream)w.BaseStream).Flush(true);
                    }
                    audioWindow.Flush(true);
                }
                audioHash.TransformFinalBlock(new byte[0], 0, 0);
                audioSha = Hex(audioHash.Hash);
            }
            if (!core.SameAs(FinalPcmWindowRunner.CoreIdentity.Read(installation.DllDirectory)))
                throw new IOException("The GPGX core file changed during the run.");
            var meta = new JObject
            {
                ["schema"] = "openggf.m68k-ledger-window.v1",
                ["ingress_kind"] = ingressKind,
                ["ingress_state_sha256"] = stateSha256,
                ["ingress_provenance"] = provenance,
                ["region"] = region,
                ["host_sha256"] = hostSha256,
                ["rom_sha1"] = romSha1,
                ["movie_sha256"] = movieSha256,
                ["frames"] = o.Frames,
                ["ledger"] = o.Ledger,
                ["intervals_complete"] = intervals,
                ["acceptances"] = accepts,
                ["audio_stereo_frames"] = audioFrames,
                ["audio_sha256"] = audioSha,
                ["core_file"] = core.File,
                ["core_size"] = core.Size,
                ["core_sha256"] = core.Sha256,
                ["bizhawk_managed_version"] = installation.ManagedVersion.ToString(),
                ["files_sha256"] = new JObject()
            };
            foreach (string name in new[] { "frames.jsonl", "intervals.jsonl", "interval-slots.csv", "accepts.jsonl", "audio-window.s16", "state-end.bin", "state-after-label.bin" })
                if (File.Exists(Path.Combine(dir, name))) ((JObject)meta["files_sha256"])[name] = FinalPcmWindowRunner.Sha256File(Path.Combine(dir, name));
            string marker = Path.Combine(dir, "metadata.json"), temporary = Path.Combine(dir, ".metadata.json.partial");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(meta.ToString() + "\n");
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                File.Move(temporary, marker);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            stdout.WriteLine("M68K_LEDGER_WINDOW frames=" + o.Frames + " intervals=" + intervals + " acceptances=" + accepts
                + " ledger=" + (o.Ledger ? "on" : "off") + " audio_sha256=" + audioSha + " core_sha256=" + core.Sha256);
            return 0;
        }

        private static StreamWriter Writer(string path)
        {
            return new StreamWriter(new FileStream(path, FileMode.CreateNew), new UTF8Encoding(false));
        }

        private static string IntervalJson(GpgxM68kStall2Bank b, int frame, int label)
        {
            var o = new JObject
            {
                ["sequence"] = b.Sequence, ["complete"] = b.OpenedByVint == 1, ["frame"] = frame, ["label"] = label,
                ["open_accept_ordinal"] = b.OpenAcceptOrdinal == 0xFFFFFFFF ? -1 : (long)b.OpenAcceptOrdinal,
                ["close_accept_ordinal"] = b.CloseAcceptOrdinal, ["entries"] = b.Entries, ["executed"] = b.Executed,
                ["open_stamp_mclk"] = b.OpenStampMclk, ["close_stamp_mclk"] = b.CloseStampMclk,
                ["straddle_pc"] = b.StraddlePc == 0xFFFFFFFF ? -1 : (long)b.StraddlePc, ["straddle_context"] = b.StraddleContext,
                ["straddle_mclk"] = b.StraddleMclk,
                ["carry_pc"] = b.CarryPc == 0xFFFFFFFF ? -1 : (long)b.CarryPc, ["carry_mclk"] = b.CarryMclk,
                ["refresh_events"] = b.RefreshEvents, ["accept_vint"] = b.AcceptVint, ["accept_hint"] = b.AcceptHint,
                ["accept_other"] = b.AcceptOther, ["irq_delay_inline"] = b.IrqDelayInline, ["synthetic_rte"] = b.SyntheticRte,
                ["abandoned_frames"] = b.AbandonedFrames,
                ["kind_mclk"] = new JObject
                {
                    ["z80_bus"] = b.KindZ80Bus, ["fifo"] = b.KindFifo, ["dma"] = b.KindDma, ["dma_rewind"] = b.KindDmaRewind,
                    ["z80_area"] = b.KindZ80Area, ["lockup"] = b.KindLockup, ["stopped"] = b.KindStopped,
                    ["accept_vint"] = b.KindAcceptVint, ["accept_hint"] = b.KindAcceptHint, ["accept_other"] = b.KindAcceptOther
                }
            };
            return o.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string Hex(byte[] digest)
        {
            var builder = new StringBuilder(digest.Length * 2);
            foreach (byte b in digest) builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }
    }
}
