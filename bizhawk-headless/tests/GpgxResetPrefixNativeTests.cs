using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using OpenGGF.BizHawk.Headless;

namespace OpenGGF.BizHawk.Headless.Tests
{
    internal static class GpgxResetPrefixNativeTests
    {
        public static void Register(ICollection<TestMain.TestCase> tests)
        {
            tests.Add(new TestMain.TestCase("GpgxResetPrefixNativeTests reject mismatched ABI and every layout word", Identity));
            tests.Add(new TestMain.TestCase("GpgxResetPrefixNativeTests preserve immutable named reset and register words", Records));
            tests.Add(new TestMain.TestCase("GpgxResetPrefixNativeTests reject gaps and reordered or incomplete instruction streams", Order));
            tests.Add(new TestMain.TestCase("GpgxResetPrefixNativeTests refuse capacity and native sticky discontinuity faults", Faults));
            tests.Add(new TestMain.TestCase("GpgxResetPrefixNativeTests loads invalidate before arm and after completion without rearming", Loads));
            tests.Add(new TestMain.TestCase("GpgxResetPrefixNativeTests late arm and disabled reads refuse provenance", ArmBoundary));
            tests.Add(new TestMain.TestCase("GpgxResetPrefixNativeTests diagnostic arguments are explicit bounded and unique", CliArguments));
            tests.Add(new TestMain.TestCase("GpgxResetPrefixNativeTests diagnostic source identity refuses core patches and commit changes", CliIdentity));
            tests.Add(new TestMain.TestCase("GpgxResetPrefixNativeTests diagnostic output refuses existing paths and repository trees", CliOutput));
            tests.Add(new TestMain.TestCase("GpgxResetPrefixNativeTests live constructor vectors clocks and first VDP retirement", LivePrefix,
                game: "s3k", kind: TestKind.Gate, serial: true, estimatedSeconds: 5));
            tests.Add(new TestMain.TestCase("GpgxResetPrefixNativeTests live observer nonperturbation and restore forward continuation", LiveControl,
                game: "s3k", kind: TestKind.Gate, serial: true, estimatedSeconds: 10));
            tests.Add(new TestMain.TestCase("GpgxResetPrefixNativeTests live same state loads reset and late arm fail closed", LiveDiscontinuity,
                game: "s3k", kind: TestKind.Gate, serial: true, estimatedSeconds: 10));
        }

        private static void Identity()
        {
            AssertEx.Equal(1u, GpgxResetPrefixNative.AbiVersion);
            AssertEx.Equal(160u, GpgxResetPrefixNative.RecordSize);
            AssertEx.Equal(40u, GpgxResetPrefixNative.RecordWords);
            AssertEx.Equal(128u, GpgxResetPrefixNative.Capacity);
            var fake = new FakeDepartures { Abi = 2 };
            AssertEx.Throws<InvalidOperationException>(() => Bind(fake), "identity");
            for (int i = 0; i < 4; i++)
            {
                fake = new FakeDepartures(); fake.Layout[i]++;
                AssertEx.Throws<InvalidOperationException>(() => Bind(fake), "layout");
            }
        }

        private static void Records()
        {
            var fake = new FakeDepartures();
            fake.Words[40 + 14] = 0x11223344; fake.Words[40 + 29] = 0xFFFFFE00;
            fake.Words[40 + 30] = 0x4E71; fake.Words[40 + 36] = 0x563412;
            fake.Words[40 + 39] = 280;
            var observer = Bind(fake); observer.Enable();
            GpgxResetPrefixCapture capture = observer.Read();
            AssertEx.Equal(false, capture.IsComplete);
            AssertEx.Equal(3, capture.Records.Count);
            GpgxResetPrefixRecord reset = capture.Records[1];
            AssertEx.Equal(GpgxResetPrefixKind.ResetComplete, reset.Kind);
            AssertEx.Equal(0x11223344u, reset.GetDataRegister(0));
            AssertEx.Equal(0xFFFFFE00u, reset.GetAddressRegister(7));
            AssertEx.Equal(0x4E71u, reset.InstructionRegister);
            AssertEx.Equal(0x563412u, reset.IoControlLatches);
            AssertEx.Equal(280u, reset.Auxiliary);
            AssertEx.Equal(1u, reset.Knownness);
            AssertEx.Throws<InvalidOperationException>(() => { uint ignored = reset.InstructionStartPc; }, "instruction events");
            AssertEx.Throws<InvalidOperationException>(() => { uint ignored = reset.SramFlags; }, "native init");
            AssertEx.Equal(0u, capture.Records[2].ControllerPortSystems);
            AssertEx.Equal(0u, capture.Records[2].ControllerDevices);
            AssertEx.Equal(0u, capture.Records[2].SramFlags);
            fake.Words[40 + 14] = 0;
            AssertEx.Equal(0x11223344u, reset.GetDataRegister(0));
            AssertEx.Throws<ArgumentOutOfRangeException>(() => reset.GetDataRegister(8), "index");
            AssertEx.Throws<ArgumentOutOfRangeException>(() => reset.GetAddressRegister(-1), "index");
            observer.Disable();
            AssertEx.Equal(1, fake.DisableCalls);
        }

        private static void Order()
        {
            var good = new FakeDepartures { Words = CompleteWords(), Status = 1 };
            var observer = Bind(good); observer.Enable();
            AssertEx.Equal(true, observer.Read().IsComplete);
            AssertEx.Equal(6, observer.Read().Records.Count); // nondestructive read
            AssertEx.Equal(0x202u, observer.Read().Records[4].Pc); // operands advanced the actual PC
            AssertEx.Equal(0x204u, observer.Read().Records[5].Pc); // retirement preserves actual continuation
            AssertEx.Equal(0x200u, observer.Read().Records[5].InstructionStartPc);
            var bad = new FakeDepartures { Words = CompleteWords(), Status = 1 };
            bad.Words[4 * 40 + 1] = 99;
            RejectRead(bad, "ordinal");
            bad = new FakeDepartures { Words = CompleteWords(), Status = 1 };
            bad.Words[3 * 40] = (uint)GpgxResetPrefixKind.Retire;
            RejectRead(bad, "instruction");
            bad = new FakeDepartures { Words = CompleteWords(), Status = 1 };
            bad.Words[4 * 40 + 10] = 0xA10008;
            RejectRead(bad, "VDP");
            bad = new FakeDepartures { Words = CompleteWords(), Status = 0 };
            RejectRead(bad, "VDP");
            bad = new FakeDepartures { Words = CompleteWords(), Status = 1 };
            bad.Words[5 * 40 + 39] = 0x204;
            RejectRead(bad, "PC");
            bad = new FakeDepartures { Words = CompleteWords(), Status = 1 };
            bad.Words[3 * 40 + 39] = 0x202;
            RejectRead(bad, "PC");
            bad = new FakeDepartures(); bad.Words[13] = 0;
            RejectRead(bad, "knownness");
            bad = new FakeDepartures { Status = 2 };
            RejectRead(bad, "status");
        }

        private static void Faults()
        {
            foreach (int result in new[] { -3, -4 })
            {
                var fake = new FakeDepartures { ReadResult = result };
                var observer = Bind(fake); observer.Enable();
                AssertEx.Throws<InvalidOperationException>(() => observer.Read(), "refused: " + result);
                fake.ReadResult = 0;
                AssertEx.Throws<InvalidOperationException>(() => observer.Enable(), "invalid");
            }
            var over = new FakeDepartures { ReportedCount = 129 };
            RejectRead(over, "capacity");
            var wrongAbi = new FakeDepartures { EnableResult = -4 };
            var failed = Bind(wrongAbi);
            AssertEx.Throws<InvalidOperationException>(() => failed.Enable(), "refused: -4");
            wrongAbi.EnableResult = 0;
            AssertEx.Throws<InvalidOperationException>(() => failed.Enable(), "invalid");
        }

        private static void Loads()
        {
            long generation = 0;
            var fake = new FakeDepartures { Words = CompleteWords(), Status = 1 };
            var observer = GpgxResetPrefixNative.BindForTests(fake, () => generation, () => 0);
            observer.Enable(); observer.Read(); generation++;
            AssertEx.Throws<InvalidOperationException>(() => observer.Read(), "savestate");
            observer.Disable();
            AssertEx.Throws<InvalidOperationException>(() => observer.Enable(), "invalid");
            AssertEx.Equal(1, fake.EnableCalls);
            observer = GpgxResetPrefixNative.BindForTests(new FakeDepartures(), () => generation, () => 0);
            AssertEx.Throws<InvalidOperationException>(() => observer.Enable(), "savestate");
        }

        private static void ArmBoundary()
        {
            var fake = new FakeDepartures();
            var observer = GpgxResetPrefixNative.BindForTests(fake, () => 0, () => 1);
            AssertEx.Throws<InvalidOperationException>(() => observer.Enable(), "first fetch");
            AssertEx.Equal(0, fake.EnableCalls);
            observer = Bind(fake);
            AssertEx.Throws<InvalidOperationException>(() => observer.Read(), "not armed");
            observer.Enable(); observer.Disable();
            AssertEx.Throws<InvalidOperationException>(() => observer.Read(), "not armed");
            AssertEx.Throws<InvalidOperationException>(() => observer.Enable(), "already consumed");
        }

        private static string RequireLiveRom()
        {
            if (Environment.GetEnvironmentVariable("OPENGGF_GPGX_RESET_PREFIX_EXPECT") != "present")
                throw new TestMain.SkipTestException("requires OPENGGF_GPGX_RESET_PREFIX_EXPECT=present and the owned source-locked core");
            string rom = Environment.GetEnvironmentVariable("S3K_ROM_PATH");
            if (string.IsNullOrEmpty(rom) || !Path.IsPathRooted(rom))
                throw new InvalidOperationException("An explicit absolute S3K_ROM_PATH is required.");
            RomIdentity.ValidateSonic3kLockOn(File.ReadAllBytes(rom));
            return rom;
        }

        private static GpgxHost Open(string rom)
        {
            // No firmware or save provider is present. This declares the stock deterministic MD profile:
            // autodetected cartridge region/VDP, BIOS off, two normal 3-button ports, fresh native SRAM.
            return GpgxHost.Open(rom, GpgxHost.CreateGhz1SyncSettings());
        }

        private static uint Be32(byte[] bytes, int offset)
        {
            return ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16)
                | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
        }

        private static void LivePrefix()
        {
            string rom = RequireLiveRom(); byte[] bytes = File.ReadAllBytes(rom);
            using (GpgxHost host = Open(rom))
            {
                var observer = GpgxResetPrefixNative.Bind(host); observer.Enable();
                GpgxResetPrefixCapture initial = observer.Read();
                AssertEx.Equal(false, initial.IsComplete); AssertEx.Equal(3, initial.Records.Count);
                var enter = initial.Records[0]; var reset = initial.Records[1]; var init = initial.Records[2];
                AssertEx.Equal(0, host.CompletedFrame);
                AssertEx.Equal(Be32(bytes, 0), reset.GetAddressRegister(7));
                AssertEx.Equal(Be32(bytes, 4), reset.Pc);
                // Pinned m68k_pulse_reset leaves CCR untouched: constructor .bss not_z_flag=0 reads as Z set.
                // The actual reference-core constructor SR is 2704, rather than a generic reset assumption 2700.
                AssertEx.Equal(0x2704u, reset.Sr); AssertEx.Equal(0u, reset.Irq);
                AssertEx.Equal(0u, init.Bios); AssertEx.Equal((uint)bytes.Length, init.RomSize);
                AssertEx.Equal(0u, init.IoControlLatches);
                AssertEx.Equal(0x80u, init.SystemHardware); AssertEx.Equal(0x80u, init.RegionCode);
                AssertEx.Equal(0u, init.VdpPal); AssertEx.Equal(262u, init.LinesPerFrame);
                AssertEx.Equal(0x0101u, init.ControllerPortSystems); AssertEx.Equal(0u, init.ControllerDevices);
                // GPGX051d genesis.c gen_reset and main CPU compiled RESET table[0] = 40*7 MCLK.
                uint phase = ((init.LinesPerFrame - 192u + 159u - 27u * init.VdpPal) * 3420u + 1004u);
                uint before = phase / 7u * 7u;
                AssertEx.Equal(before, enter.RawCycles);
                AssertEx.Equal(280u, reset.Auxiliary);
                AssertEx.Equal(before + 280u, reset.RawCycles);
                AssertEx.Equal((reset.RawCycles / 896u + 1u) * 896u, reset.RefreshDeadline);
                AssertEx.Equal(phase / 15u * 15u, reset.Z80Cycles);
                AssertEx.Equal(0u, reset.Z80State); AssertEx.Equal(0u, reset.Z80Bank);
                host.ClearButtons(); host.Advance();
                var complete = observer.Read(); AssertEx.Equal(true, complete.IsComplete);
                GpgxResetPrefixRecord first = complete.Records[3];
                AssertEx.Equal(GpgxResetPrefixKind.Fetch, first.Kind); AssertEx.Equal(Be32(bytes, 4), first.Pc);
                AssertEx.Equal(reset.RawCycles, first.RawCycles);
                AssertEx.Equal(reset.RefreshDeadline, first.RefreshDeadline);
                AssertEx.Equal((uint)((bytes[first.Pc] << 8) | bytes[first.Pc + 1]), first.InstructionRegister);
                // sonic3k.asm EntryPoint first retires LEA (System_stack).w,SP (4ff8 + signed absolute word).
                AssertEx.Equal(0x4FF8u, first.InstructionRegister);
                var firstRetire = complete.Records[4];
                AssertEx.Equal(GpgxResetPrefixKind.Retire, firstRetire.Kind);
                AssertEx.Equal(first.Pc, firstRetire.InstructionStartPc);
                AssertEx.Equal(first.Pc + 4u, firstRetire.Pc);
                uint stack = unchecked((uint)(int)(short)((bytes[first.Pc + 2] << 8) | bytes[first.Pc + 3]));
                AssertEx.Equal(stack, firstRetire.GetAddressRegister(7));
                AssertEx.Equal(GpgxResetPrefixKind.Fetch, complete.Records[5].Kind);
                AssertEx.Equal(firstRetire.Pc, complete.Records[5].Pc);
                int reads = 0;
                foreach (var record in complete.Records)
                    if (record.Kind == GpgxResetPrefixKind.Read && record.Address == 0xC00004 && record.Width == 2) reads++;
                AssertEx.Equal(1, reads);
                var last = complete.Records[complete.Records.Count - 1];
                AssertEx.Equal(GpgxResetPrefixKind.Retire, last.Kind);
                // SkipSecurity's MOVE.W (A4),D0 reads the first VDP status and retires at its next word.
                AssertEx.Equal(0x3014u, last.InstructionRegister);
                AssertEx.Equal(last.InstructionStartPc + 2u, last.Pc);
                AssertCaptureEqual(complete, observer.Read());
                observer.Disable();
            }
        }

        private static void LiveControl()
        {
            string rom = RequireLiveRom(); byte[] state, checkpoint, continued, restored;
            using (GpgxHost host = Open(rom))
            {
                host.ClearButtons(); host.Advance(); state = host.CloneSavestate();
                checkpoint = host.CaptureDeterministicCheckpoint();
                host.Advance(); continued = host.CaptureDeterministicCheckpoint();
                host.LoadSavestate(state); host.Advance(); restored = host.CaptureDeterministicCheckpoint();
            }
            using (GpgxHost host = Open(rom))
            {
                var observer = GpgxResetPrefixNative.Bind(host); observer.Enable();
                host.ClearButtons(); host.Advance(); AssertEx.Equal(true, observer.Read().IsComplete);
                AssertBytesEqual(state, host.CloneSavestate());
                AssertBytesEqual(checkpoint, host.CaptureDeterministicCheckpoint());
                host.Advance(); AssertBytesEqual(continued, host.CaptureDeterministicCheckpoint());
                host.LoadSavestate(state);
                AssertEx.Throws<InvalidOperationException>(() => observer.Read(), "savestate");
                host.Advance(); AssertBytesEqual(restored, host.CaptureDeterministicCheckpoint());
                observer.Disable();
            }
        }

        private static void LiveDiscontinuity()
        {
            string rom = RequireLiveRom();
            using (GpgxHost host = Open(rom))
            {
                var observer = GpgxResetPrefixNative.Bind(host); observer.Enable();
                byte[] same = host.CloneSavestate(); host.LoadSavestate(same);
                AssertEx.Throws<InvalidOperationException>(() => observer.Read(), "savestate");
                AssertEx.Throws<InvalidOperationException>(() => observer.Enable(), "invalid");
                observer.Disable();
            }
            using (GpgxHost host = Open(rom))
            {
                host.Advance(); var observer = GpgxResetPrefixNative.Bind(host);
                AssertEx.Throws<InvalidOperationException>(() => observer.Enable(), "first fetch");
            }
            using (GpgxHost host = Open(rom))
            {
                var observer = GpgxResetPrefixNative.Bind(host); observer.Enable();
                host.SetButton("Reset", true); host.Advance(); host.ClearButtons();
                AssertEx.Throws<InvalidOperationException>(() => observer.Read(), "refused: -4");
                AssertEx.Throws<InvalidOperationException>(() => observer.Enable(), "invalid");
                observer.Disable();
            }
        }

        private static void AssertBytesEqual(byte[] expected, byte[] actual)
        {
            AssertEx.Equal(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++)
                if (expected[i] != actual[i]) throw new InvalidOperationException("Checkpoint byte differs at " + i + ".");
        }

        private static void CliArguments()
        {
            string[] good = { ResetPrefixRunner.Flag, "--rom", "/r.gen", "--output", "/outside/new", "--observer", "on" };
            var options = ResetPrefixRunner.Parse(good);
            AssertEx.Equal("/r.gen", options.RomPath); AssertEx.Equal(true, options.Observer);
            good[6] = "off"; AssertEx.Equal(false, ResetPrefixRunner.Parse(good).Observer);
            AssertEx.Equal(true, ResetPrefixRunner.IsRequested(good));
            AssertEx.Equal(false, ResetPrefixRunner.IsRequested(new string[0]));
            AssertEx.Throws<ArgumentException>(() => ResetPrefixRunner.Parse(new string[0]), "Expected");
            AssertEx.Throws<ArgumentException>(() => ResetPrefixRunner.Parse(new[] { ResetPrefixRunner.Flag, "--rom", "/r.gen",
                "--output", "/outside/new" }), "--observer");
            foreach (string forbidden in new[] { "--movie", "--save-state", "--pc", "--frames", "--trace-profile" })
                AssertEx.Throws<ArgumentException>(() => ResetPrefixRunner.Parse(new[] { ResetPrefixRunner.Flag,
                    "--rom", "/r.gen", "--output", "/outside/new", "--observer", "on", forbidden, "x" }), "Only");
            AssertEx.Throws<ArgumentException>(() => ResetPrefixRunner.Parse(new[] { ResetPrefixRunner.Flag,
                "--rom", "/r.gen", "--output", "/outside/new", "--observer", "on", "--observer", "off" }), "Duplicate");
            AssertEx.Throws<ArgumentException>(() => ResetPrefixRunner.Parse(new[] { ResetPrefixRunner.Flag,
                "--rom", "r.gen", "--output", "/outside/new", "--observer", "on" }), "absolute");
            AssertEx.Throws<ArgumentException>(() => ResetPrefixRunner.Parse(new[] { ResetPrefixRunner.Flag,
                "--rom", "/r.gen", "--output", "outside/new", "--observer", "on" }), "absolute");
            AssertEx.Throws<ArgumentException>(() => ResetPrefixRunner.Parse(new[] { ResetPrefixRunner.Flag,
                "--rom", "/r.gen", "--output", "/outside/new", "--observer", "auto" }), "on or off");
            AssertEx.Throws<ArgumentException>(() => ResetPrefixRunner.Parse(new[] { ResetPrefixRunner.Flag, "--rom" }), "pairs");
        }

        private static JObject IdentityForTest(string source)
        {
            var patches = new JArray();
            foreach (string file in ResetPrefixRunner.PatchFiles)
                patches.Add(new JObject { ["file"] = file, ["sha256"] = FinalPcmWindowRunner.Sha256File(Path.Combine(source, file)) });
            return new JObject {
                ["schema"] = "openggf.gpgx-reset-prefix-observer-build.v1", ["series"] = "reset-prefix",
                ["reset_prefix_abi"] = new JObject { ["version"] = 1, ["record_bytes"] = 160, ["capacity"] = 128 },
                ["source"] = new JObject { ["bizhawk_commit"] = "427556b5ef3ac437eba754d90c5e7e9096c9a8df",
                    ["gpgx_commit"] = "051d430d3d1b54625f9900c8f152d7f232e06daf" },
                ["patches"] = patches, ["compressed_sha256"] = new string('a', 64), ["compressed_size"] = 123,
                ["decompressed_sha256"] = new string('b', 64), ["decompressed_size"] = 456
            };
        }

        private static void CliIdentity()
        {
            string source = ResetPrefixRunner.ObserverSourceDirectory();
            JObject identity = IdentityForTest(source);
            const string managed = "0144e6e236be68ce126eb771dcb5a9ae7c153a083fa0333f345ac37b4a60acf7";
            JObject sourceLock = JObject.Parse(File.ReadAllText(Path.Combine(source, "source-lock.json")));
            AssertEx.Equal(managed, (string)sourceLock["stock"]["cores_sha256"]);
            ResetPrefixRunner.ValidateManagedCorePin(sourceLock, managed);
            AssertEx.Throws<InvalidDataException>(() => ResetPrefixRunner.ValidateManagedCorePin(sourceLock, new string('0', 64)), "managed constructor");
            AssertEx.Throws<InvalidDataException>(() => ResetPrefixRunner.ValidateManagedCorePin(sourceLock, null), "managed constructor");
            JObject wrongSourceLock = (JObject)sourceLock.DeepClone(); wrongSourceLock.Remove("stock");
            AssertEx.Throws<InvalidDataException>(() => ResetPrefixRunner.ValidateManagedCorePin(wrongSourceLock, managed), "managed constructor");
            ResetPrefixRunner.ValidateIdentity(identity, source, new string('a', 64), 123, managed);
            AssertEx.Throws<InvalidDataException>(() => ResetPrefixRunner.ValidateIdentity(identity, source, new string('a', 64), 123,
                new string('0', 64)), "managed constructor");
            foreach (string key in new[] { "schema", "series", "compressed_sha256", "decompressed_sha256" })
            {
                identity = IdentityForTest(source); identity[key] = "wrong";
                AssertEx.Throws<InvalidDataException>(() => ResetPrefixRunner.ValidateIdentity(identity, source, new string('a', 64), 123, managed), "identity");
            }
            identity = IdentityForTest(source); identity["source"]["bizhawk_commit"] = new string('0', 40);
            AssertEx.Throws<InvalidDataException>(() => ResetPrefixRunner.ValidateIdentity(identity, source, new string('a', 64), 123, managed), "source commits");
            identity = IdentityForTest(source); identity["reset_prefix_abi"]["capacity"] = 127;
            AssertEx.Throws<InvalidDataException>(() => ResetPrefixRunner.ValidateIdentity(identity, source, new string('a', 64), 123, managed), "ABI");
            identity = IdentityForTest(source); identity["patches"][5]["sha256"] = new string('0', 64);
            AssertEx.Throws<InvalidDataException>(() => ResetPrefixRunner.ValidateIdentity(identity, source, new string('a', 64), 123, managed), "patch identity");
            identity = IdentityForTest(source); ((JArray)identity["patches"]).RemoveAt(5);
            AssertEx.Throws<InvalidDataException>(() => ResetPrefixRunner.ValidateIdentity(identity, source, new string('a', 64), 123, managed), "inventory");
        }

        private static void CliOutput()
        {
            string inside = TestScratch.CreateRootPath("reset-prefix-output");
            AssertEx.Throws<ArgumentException>(() => ResetPrefixRunner.RequireNewOutput(inside), "outside repository");
            Directory.CreateDirectory(inside);
            try { AssertEx.Throws<IOException>(() => ResetPrefixRunner.RequireNewOutput(inside), "already exists"); }
            finally { Directory.Delete(inside); }
        }

        private static void AssertCaptureEqual(GpgxResetPrefixCapture expected, GpgxResetPrefixCapture actual)
        {
            AssertEx.Equal(expected.IsComplete, actual.IsComplete); AssertEx.Equal(expected.Records.Count, actual.Records.Count);
            for (int i = 0; i < expected.Records.Count; i++)
                for (int word = 0; word < 40; word++) AssertEx.Equal(expected.Records[i].GetWord(word), actual.Records[i].GetWord(word));
        }

        private static GpgxResetPrefixNative Bind(FakeDepartures fake)
        { return GpgxResetPrefixNative.BindForTests(fake, () => 0, () => 0); }

        private static void RejectRead(FakeDepartures fake, string text)
        {
            var observer = Bind(fake); observer.Enable();
            AssertEx.Throws<InvalidOperationException>(() => observer.Read(), text);
        }

        private static uint[] InitialWords()
        {
            var words = new uint[3 * 40];
            for (uint i = 0; i < 3; i++) { words[i * 40] = i + 1; words[i * 40 + 1] = i; words[i * 40 + 13] = 1; }
            return words;
        }

        private static uint[] CompleteWords()
        {
            var words = new uint[6 * 40]; Array.Copy(InitialWords(), words, 3 * 40);
            for (uint i = 3; i < 6; i++)
            {
                words[i * 40 + 1] = i; words[i * 40 + 2] = 0x200;
                words[i * 40 + 13] = 1; words[i * 40 + 39] = 0x200;
            }
            words[3 * 40] = (uint)GpgxResetPrefixKind.Fetch;
            words[4 * 40] = (uint)GpgxResetPrefixKind.Read;
            words[4 * 40 + 2] = 0x202;
            words[4 * 40 + 10] = 0xC00004; words[4 * 40 + 12] = 2;
            words[5 * 40] = (uint)GpgxResetPrefixKind.Retire;
            words[5 * 40 + 2] = 0x204;
            return words;
        }

        private sealed class FakeDepartures : GpgxResetPrefixDepartures
        {
            public uint Abi = 1, Status, ReportedCount;
            public uint[] Layout = { 160, 128, 40, 1 };
            public uint[] Words = InitialWords();
            public int EnableResult, ReadResult, EnableCalls, DisableCalls;
            public override uint gpgx_reset_prefix_abi_version() { return Abi; }
            public override int gpgx_reset_prefix_layout(uint[] layout, uint count)
            { Array.Copy(Layout, layout, 4); return 0; }
            public override int gpgx_reset_prefix_enable(uint abi) { EnableCalls++; return EnableResult; }
            public override int gpgx_reset_prefix_read(uint[] words, uint capacityWords, out uint count, out uint status)
            {
                Array.Copy(Words, words, Words.Length); status = Status;
                count = ReportedCount == 0 ? (uint)Words.Length / 40 : ReportedCount;
                return ReadResult;
            }
            public override int gpgx_reset_prefix_disable() { DisableCalls++; return 0; }
        }
    }
}
