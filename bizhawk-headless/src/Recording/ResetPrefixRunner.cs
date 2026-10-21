using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BizHawk.Emulation.Cores.Consoles.Sega.gpgx;
using System.Security.Cryptography;
using BizHawk.Headless.Gpgx;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpenGGF.BizHawk.Headless
{
    // Bounded native-reset-prefix-20261021 diagnostic. Inputs: explicit locked-on S3K ROM, fixed deterministic
    // constructor profile, installed source-locked core and current six native patches. One neutral frame only.
    // Output: immutable ordered comparison records and checkpoint digests, never gameplay or fixture inputs.
    internal static class ResetPrefixRunner
    {
        internal const string Flag = "--native-reset-prefix";
        private const string BuildSchema = "openggf.gpgx-reset-prefix-observer-build.v1";
        private const string BizHawkCommit = "427556b5ef3ac437eba754d90c5e7e9096c9a8df";
        private const string GpgxCommit = "051d430d3d1b54625f9900c8f152d7f232e06daf";
        internal static readonly string[] PatchFiles = {
            "0001-buffer-z80-audio-events.patch", "0002-s3k-audio-parity-events.patch",
            "0003-s3k-chip-pcm-events.patch", "0004-m68k-stall-counters.patch",
            "0005-m68k-cpu-ledger-v2.patch", "0006-constructor-reset-prefix.patch"
        };

        internal sealed class Options
        {
            internal string RomPath, OutputDirectory;
            internal bool Observer;
        }

        internal static bool IsRequested(string[] args)
        { return args != null && args.Length > 0 && args[0] == Flag; }

        internal static Options Parse(string[] args)
        {
            if (!IsRequested(args)) throw new ArgumentException("Expected " + Flag + ".");
            FinalPcmWindowRunner.RejectDuplicateFlags(args);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 1; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length) throw new ArgumentException("Arguments must be --name value pairs after " + Flag + ".");
                values.Add(args[i], args[i + 1]);
            }
            var options = new Options { RomPath = Absolute(values, "--rom"), OutputDirectory = Absolute(values, "--output") };
            string observer;
            if (!values.TryGetValue("--observer", out observer) || (observer != "on" && observer != "off"))
                throw new ArgumentException("--observer must be explicitly on or off.");
            if (values.Count != 3) throw new ArgumentException("Only --rom, --output and --observer are accepted.");
            options.Observer = observer == "on";
            return options;
        }

        private static string Absolute(Dictionary<string, string> values, string key)
        {
            string value;
            if (!values.TryGetValue(key, out value) || string.IsNullOrEmpty(value) || !Path.IsPathRooted(value))
                throw new ArgumentException(key + " must be an explicit absolute path.");
            return Path.GetFullPath(value);
        }

        internal static string RequireNewOutput(string output)
        {
            if (LinuxPathEntry.Exists(output)) throw new IOException("--output already exists and will not be replaced.");
            string resolved = LinuxPathEntry.ResolveProposedPath(output);
            // The CLI's existing producer boundary also protects the explicit consumer/fixture roots.
            // A containing dotfiles or scratch Git repository is not one of those protected source trees.
            string producer = Path.GetFullPath(Path.Combine(ObserverSourceDirectory(), "../../.."));
            if (resolved == producer || resolved.StartsWith(producer + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ArgumentException("--output must remain outside repository source trees.");
            return resolved;
        }

        internal static string ObserverSourceDirectory()
        {
            string executable = Path.GetDirectoryName(typeof(ResetPrefixRunner).Assembly.Location);
            string source = Path.GetFullPath(Path.Combine(executable, "../../..", "bizhawk-headless/native/gpgx-audio-observer"));
            if (!File.Exists(Path.Combine(source, "source-lock.json")))
                throw new InvalidOperationException("Reset-prefix diagnostic must run from its built source checkout; native patch sources are absent.");
            return source;
        }

        // Build provenance is checked against the installed compressed core and the current source patch bytes.
        // It is a source/build identity check, not host attestation. The recorded raw hash is independently verified
        // only when the installation also carries an uncompressed core; Waterbox loads the compressed core first.
        internal static void ValidateIdentity(JObject identity, string source, string compressedSha256, long compressedSize,
            string loadedManagedSha256)
        {
            ValidateManagedCorePin(JObject.Parse(File.ReadAllText(Path.Combine(source, "source-lock.json"))), loadedManagedSha256);
            if (identity == null || (string)identity["schema"] != BuildSchema || (string)identity["series"] != "reset-prefix")
                throw new InvalidDataException("Reset-prefix native build identity schema/series differs.");
            JObject sources = identity["source"] as JObject, abi = identity["reset_prefix_abi"] as JObject;
            if (sources == null || (string)sources["bizhawk_commit"] != BizHawkCommit || (string)sources["gpgx_commit"] != GpgxCommit)
                throw new InvalidDataException("Reset-prefix native source commits differ from the pinned sources.");
            if (abi == null || (uint?)abi["version"] != 1 || (uint?)abi["record_bytes"] != 160 || (uint?)abi["capacity"] != 128)
                throw new InvalidDataException("Reset-prefix native ABI identity differs.");
            if ((string)identity["compressed_sha256"] != compressedSha256 || (long?)identity["compressed_size"] != compressedSize)
                throw new InvalidDataException("Installed compressed core differs from reset-prefix build identity.");
            if (!IsSha256((string)identity["decompressed_sha256"]) || (long?)identity["decompressed_size"] <= 0
                || identity["decompressed_size"] == null)
                throw new InvalidDataException("Reset-prefix decompressed core identity is absent or malformed.");
            JArray patches = identity["patches"] as JArray;
            if (patches == null || patches.Count != PatchFiles.Length)
                throw new InvalidDataException("Reset-prefix patch inventory differs.");
            for (int i = 0; i < PatchFiles.Length; i++)
            {
                JObject patch = patches[i] as JObject;
                if (patch == null || (string)patch["file"] != PatchFiles[i]
                    || (string)patch["sha256"] != FinalPcmWindowRunner.Sha256File(Path.Combine(source, PatchFiles[i])))
                    throw new InvalidDataException("Reset-prefix patch identity differs: " + PatchFiles[i] + ".");
            }
        }

        // A version/path report alone does not authenticate the managed GPGX constructor at427.
        // Enforce the checked-in stock byte pin, using the actual loaded GPGX assembly rather than an
        // assumed installation filename. This is artifact/module identity, not live-IL or host attestation.
        internal static void ValidateManagedCorePin(JObject sourceLock, string loadedManagedSha256)
        {
            string expected = (string)sourceLock?["stock"]?["cores_sha256"];
            if ((string)sourceLock?["schema"] != "openggf.gpgx-observer-source-lock.v1"
                || (string)sourceLock?["bizhawk"]?["commit"] != BizHawkCommit
                || !IsSha256(expected) || loadedManagedSha256 != expected)
                throw new InvalidDataException("Reset-prefix managed constructor bytes differ from source-lock stock.cores_sha256.");
        }

        internal sealed class ManagedConstructorIdentity
        {
            internal Assembly Assembly;
            internal string LoadedPath, Sha256, SourceLockSha256, ModuleVersionId;
            internal void RequireSame(ManagedConstructorIdentity other)
            {
                if (!ReferenceEquals(Assembly, other.Assembly) || LoadedPath != other.LoadedPath
                    || Sha256 != other.Sha256 || SourceLockSha256 != other.SourceLockSha256
                    || ModuleVersionId != other.ModuleVersionId)
                    throw new InvalidDataException("Reset-prefix loaded managed constructor identity changed during capture.");
            }
        }

        internal static ManagedConstructorIdentity ReadManagedConstructorIdentity(string source, string dllDirectory)
        {
            Assembly loaded = typeof(GPGX).Assembly;
            string location = loaded.Location;
            if (loaded.IsDynamic || string.IsNullOrEmpty(location)
                || loaded.GetName().Name != "BizHawk.Emulation.Cores"
                || loaded.GetName().Version != new Version(2, 11, 0, 0))
                throw new InvalidDataException("Reset-prefix loaded managed constructor assembly is unavailable or unqualified.");
            string path = LinuxPathEntry.ResolveProposedPath(location);
            string expected = LinuxPathEntry.ResolveProposedPath(Path.Combine(dllDirectory, "BizHawk.Emulation.Cores.dll"));
            if (path != expected)
                throw new InvalidDataException("Reset-prefix loaded managed constructor path differs from the explicit installation.");
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetName().Name == "BizHawk.Emulation.Cores" && !ReferenceEquals(assembly, loaded))
                    throw new InvalidDataException("Reset-prefix has ambiguous loaded managed constructor assemblies.");
            byte[] sourceLockBytes = File.ReadAllBytes(Path.Combine(source, "source-lock.json"));
            JObject sourceLock = JObject.Parse(Encoding.UTF8.GetString(sourceLockBytes));
            byte[] image = File.ReadAllBytes(path);
            string hash = Digest(image);
            ValidateManagedCorePin(sourceLock, hash); // Reject same-version wrong bytes before any Open/constructor.
            // Compare the loaded module identity with the authenticated disk image. Reflection-only loading
            // inspects metadata and cannot run the GPGX constructor. Concurrent artifact/IL mutation is unsupported.
            Guid module = loaded.ManifestModule.ModuleVersionId;
            if (module == Guid.Empty || module != Assembly.ReflectionOnlyLoad(image).ManifestModule.ModuleVersionId)
                throw new InvalidDataException("Reset-prefix loaded managed constructor module differs from authenticated bytes.");
            return new ManagedConstructorIdentity { Assembly = loaded, LoadedPath = path, Sha256 = hash,
                SourceLockSha256 = Digest(sourceLockBytes), ModuleVersionId = module.ToString("D") };
        }

        private static bool IsSha256(string text)
        {
            if (text == null || text.Length != 64) return false;
            foreach (char c in text) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
            return true;
        }

        private static JObject ReadIdentity(string root, string source, FinalPcmWindowRunner.CoreIdentity core, string loadedManagedSha256)
        {
            JObject identity = JObject.Parse(File.ReadAllText(Path.Combine(root, "gpgx-audio-observer-source/identity.json")));
            ValidateIdentity(identity, source, core.Sha256, core.Size, loadedManagedSha256);
            string raw = Path.Combine(root, "dll/gpgx.wbx");
            if (File.Exists(raw) && (FinalPcmWindowRunner.Sha256File(raw) != (string)identity["decompressed_sha256"]
                || new FileInfo(raw).Length != (long)identity["decompressed_size"]))
                throw new InvalidDataException("Installed decompressed core differs from reset-prefix build identity.");
            return identity;
        }

        private static string Digest(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        internal static int Run(string[] args, TextWriter stdout)
        {
            Options options = Parse(args);
            string output = RequireNewOutput(options.OutputDirectory), source = ObserverSourceDirectory();
            byte[] romBytes = File.ReadAllBytes(options.RomPath);
            string romSha1 = RomIdentity.ValidateSonic3kLockOn(romBytes);
            string root = Path.GetFullPath(Environment.GetEnvironmentVariable("BIZHAWK_HOME") ?? "");
            BizHawkInstallation installation = BizHawkInstallation.Validate(Environment.GetEnvironmentVariable("BIZHAWK_HOME"));
            var core = FinalPcmWindowRunner.CoreIdentity.Read(installation.DllDirectory);
            if (core.File != "gpgx.wbx.zst") throw new InvalidDataException("Reset-prefix requires the identified installed compressed core.");
            ManagedConstructorIdentity managed = ReadManagedConstructorIdentity(source, installation.DllDirectory);
            JObject identity = ReadIdentity(root, source, core, managed.Sha256);
            string identityPath = Path.Combine(root, "gpgx-audio-observer-source/identity.json");
            string identitySha256 = FinalPcmWindowRunner.Sha256File(identityPath);
            string executableSha256 = FinalPcmWindowRunner.Sha256File(typeof(ResetPrefixRunner).Assembly.Location);
            GpgxResetPrefixCapture capture = null;
            byte[] state, checkpoint;
            using (new NativeStandardOutputSilencer())
            using (GpgxHost host = GpgxHost.Open(options.RomPath, GpgxHost.CreateGhz1SyncSettings()))
            {
                managed.RequireSame(ReadManagedConstructorIdentity(source, installation.DllDirectory));
                GpgxResetPrefixNative observer = options.Observer ? GpgxResetPrefixNative.Bind(host) : null;
                try
                {
                    if (observer != null) observer.Enable();
                    host.ClearButtons(); host.Advance();
                    if (host.CompletedFrame != 1) throw new InvalidOperationException("Reset-prefix control must complete exactly one ordinary frame.");
                    if (observer != null)
                    {
                        capture = observer.Read();
                        if (!capture.IsComplete) throw new InvalidOperationException("Reset-prefix did not reach first VDP status retirement.");
                        var init = capture.Records[2];
                        if (init.SystemHardware != 0x80 || init.RegionCode != 0x80 || init.VdpPal != 0
                            || init.LinesPerFrame != 262 || init.Bios != 0 || init.IoControlLatches != 0
                            || init.ControllerPortSystems != 0x0101 || init.ControllerDevices != 0)
                            throw new InvalidOperationException("Reset-prefix native profile differs from the fixed MD/USA/NTSC/BIOS-off cold profile.");
                        var reset = capture.Records[1];
                        if (reset.Pc != Be32(romBytes, 4) || reset.GetAddressRegister(7) != Be32(romBytes, 0)
                            || capture.Records[3].Pc != reset.Pc)
                            throw new InvalidOperationException("Reset-prefix does not begin at the supplied ROM reset vectors.");
                    }
                    state = host.CloneSavestate(); checkpoint = host.CaptureDeterministicCheckpoint();
                }
                finally { if (observer != null) observer.Disable(); }
            }
            if (romSha1 != RomIdentity.ComputeSha1(File.ReadAllBytes(options.RomPath))
                || !core.SameAs(FinalPcmWindowRunner.CoreIdentity.Read(installation.DllDirectory))
                || identitySha256 != FinalPcmWindowRunner.Sha256File(identityPath)
                || executableSha256 != FinalPcmWindowRunner.Sha256File(typeof(ResetPrefixRunner).Assembly.Location))
                throw new InvalidDataException("Reset-prefix input identity changed while the diagnostic ran.");
            managed.RequireSame(ReadManagedConstructorIdentity(source, installation.DllDirectory));
            ReadIdentity(root, source, core, managed.Sha256);
            var rows = new JArray();
            if (capture != null)
                foreach (var record in capture.Records)
                {
                    var row = new JArray();
                    for (int word = 0; word < 40; word++) row.Add(record.GetWord(word));
                    rows.Add(row);
                }
            JObject initial = capture == null ? null : NativeInitial(capture);
            var report = new JObject {
                ["schema"] = "openggf.gpgx-reset-prefix-diagnostic.v1", ["execution_status"] = "completed",
                ["observer"] = options.Observer ? "on" : "off", ["ordinary_neutral_frames"] = 1,
                ["reset_prefix_completed"] = capture != null && capture.IsComplete,
                ["observation_stop"] = capture == null ? null : "first_vdp_status_read_retirement",
                ["comparison_only"] = true, ["knownness"] = "reference-emulator initialized; not physical-console power-on certainty",
                ["profile"] = new JObject {
                    ["hardware"] = "Mega Drive cartridge", ["region_setting"] = "Autodetect", ["expected_region"] = "USA NTSC",
                    ["bios"] = "disabled; NoFirmwareProvider", ["controllers"] = "two normal three-button ports",
                    ["sram"] = "fresh native constructor initialization; no user save read or write", ["input"] = "all buttons neutral",
                    ["video"] = "ordinary FrameAdvance render=false", ["sound"] = "ordinary FrameAdvance sound=false",
                    ["fm_chip"] = "MAME_YM2612", ["filter"] = "None", ["sync_settings"] = "GpgxHost.CreateGhz1SyncSettings"
                },
                ["native_initial"] = initial, ["record_words"] = 40, ["record_count"] = rows.Count, ["records"] = rows,
                ["rom_sha1"] = romSha1, ["rom_bytes"] = romBytes.Length,
                ["native_build_identity"] = identity, ["native_build_identity_sha256"] = identitySha256,
                ["installed_core_sha256"] = core.Sha256, ["installed_core_bytes"] = core.Size,
                ["installed_decompressed_core_verified"] = File.Exists(Path.Combine(root, "dll/gpgx.wbx")),
                ["harness_executable_sha256"] = executableSha256, ["bizhawk_managed_version"] = installation.ManagedVersion.ToString(),
                ["managed_cores_sha256"] = managed.Sha256,
                ["managed_cores_loaded_path"] = managed.LoadedPath, ["managed_cores_module_version_id"] = managed.ModuleVersionId,
                ["managed_source_lock_sha256"] = managed.SourceLockSha256,
                ["managed_constructor_verification"] = "source-lock stock byte pin and loaded module checked before Open, after constructor/observations and before publication",
                ["savestate_sha256"] = Digest(state), ["savestate_bytes"] = state.Length,
                ["ram_register_video_audio_checkpoint_sha256"] = Digest(checkpoint), ["checkpoint_bytes"] = checkpoint.Length,
                ["limits"] = "Prefix ends at first VDP status retirement. No title timing, AIZ, SSZ, full CPU or native heap claim."
            };
            RequireNewOutput(output);
            managed.RequireSame(ReadManagedConstructorIdentity(source, installation.DllDirectory));
            ReadIdentity(root, source, core, managed.Sha256);
            using (var staged = new NoReplacePublisher().StageAll(output, new[] { "reset-prefix.json" },
                writers => writers[0].Write(report.ToString(Formatting.Indented) + "\n"))) staged.Publish();
            stdout.WriteLine("NATIVE_RESET_PREFIX observer=" + (options.Observer ? "on" : "off")
                + " records=" + rows.Count + " savestate_sha256=" + Digest(state) + " core_sha256=" + core.Sha256);
            return 0;
        }

        private static uint Be32(byte[] bytes, int offset)
        {
            return ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16)
                | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
        }

        private static JObject NativeInitial(GpgxResetPrefixCapture capture)
        {
            var enter = capture.Records[0]; var reset = capture.Records[1]; var init = capture.Records[2];
            return new JObject {
                ["reset_enter_raw_mclk"] = enter.RawCycles, ["reset_complete_raw_mclk"] = reset.RawCycles,
                ["reset_cost_mclk"] = reset.Auxiliary, ["refresh_deadline_mclk"] = reset.RefreshDeadline,
                ["reset_pc"] = reset.Pc, ["reset_a7"] = reset.GetAddressRegister(7), ["reset_sr"] = reset.Sr,
                ["reset_irq"] = reset.Irq, ["z80_cycles"] = reset.Z80Cycles, ["z80_state"] = reset.Z80State, ["z80_bank"] = reset.Z80Bank,
                ["system_hw"] = init.SystemHardware, ["region_code"] = init.RegionCode, ["vdp_pal"] = init.VdpPal,
                ["lines_per_frame"] = init.LinesPerFrame, ["hw_version"] = init.HardwareVersion,
                ["io_control_latches"] = init.IoControlLatches, ["bios"] = init.Bios, ["rom_size"] = init.RomSize,
                ["controller_port_systems"] = init.ControllerPortSystems, ["controller_devices"] = init.ControllerDevices,
                ["sram_flags"] = init.SramFlags
            };
        }
    }
}
