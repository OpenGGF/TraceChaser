using System;
using System.Collections.Generic;
using System.IO;

namespace OpenGGF.BizHawk.Headless.Tests
{
    internal static class S1InitializationTimingObserverTests
    {
        private static readonly string Fingerprint = "sha256:" + new string('a', 64);
        public static void Register(ICollection<TestMain.TestCase> tests)
        {
            tests.Add(new TestMain.TestCase("S1InitializationTimingObserver observes credits demo phases", CreditsDemo));
            tests.Add(new TestMain.TestCase("S1InitializationTimingObserver rejects unproven initial callback states", CallbackGuard));
            tests.Add(new TestMain.TestCase("S1InitializationTimingObserver observes the actual credits font setup", CreditsSetup));
            tests.Add(new TestMain.TestCase("S1InitializationTimingObserver records real setup and assembly order", RecordsPhases));
            tests.Add(new TestMain.TestCase("S1InitializationTimingObserver excludes cold bootstrap", ExcludesBootstrap));
            tests.Add(new TestMain.TestCase("S1InitializationTimingObserver rejects ordinary loading interrupts", RejectsOrdinary));
            tests.Add(new TestMain.TestCase("S1InitializationTimingObserver rejects unproven object interrupts", RejectsObjectInterrupt));
            tests.Add(new TestMain.TestCase("S1InitializationTimingObserver rejects reordered phases", RejectsReordering));
            if (Environment.GetEnvironmentVariable("S1_ROM_PATH") != null) {
                tests.Add(new TestMain.TestCase("S1InitializationTimingObserver matches independent retail source vectors", RetailVectors));
                tests.Add(new TestMain.TestCase("S1InitializationTimingObserver pins every native cut opcode", RetailOpcodePins));
            }
        }
        private static S1InitializationTimingObserver Open(FakeS1Host host, bool represented = true)
        {
            return new S1InitializationTimingObserver(host, phase => Fingerprint, () => represented);
        }
        private static void RetailOpcodePins()
        {
            var rom = File.ReadAllBytes(Environment.GetEnvironmentVariable("S1_ROM_PATH"));
            S1InitializationTimingObserver.ValidateObserverCuts(rom);
            rom[S1InitializationTimingObserver.DemoObjectsBeginPc] ^= 1;
            AssertEx.Throws<InvalidDataException>(() => S1InitializationTimingObserver.ValidateObserverCuts(rom), "opcode mismatch");
        }

        private static void CreditsDemo()
        {
            var host = new FakeS1Host(null); var output = new StringWriter();
            host.Ram[0xFFF0] = 0x80;
            using (var observer = new S1InitializationTimingObserver(host, phase => Fingerprint,
                    () => true, null, phase => "sha256:" + new string('c', 64))) {
                host.FireExecuteCallback(S1InitializationTimingObserver.DemoSetupBeginPc);
                observer.FlushFrame(10, output);
                host.FireExecuteCallback(S1InitializationTimingObserver.DemoPlacementBeginPc);
                host.FireExecuteCallback(S1InitializationTimingObserver.DemoObjectsBeginPc);
                host.FireExecuteCallback(S1InitializationTimingObserver.VblankEntryPc);
                host.FireExecuteCallback(S1InitializationTimingObserver.DemoPresentationBeginPc);
                host.FireExecuteCallback(S1InitializationTimingObserver.DemoSetupEndPc);
                observer.FlushFrame(11, output); observer.VerifyComplete();
            }
            AssertEx.Equal(true, output.ToString().Contains("sha256:" + new string('c', 64)));
            AssertEx.Equal(true, output.ToString().Contains("\"phase\":\"initial_objects\""));
        }
        private static void CallbackGuard()
        {
            S1InitialObjectTimingGuard.Validate(0x55, 0, 0, 0xEA0, 0xF40);
            S1InitialObjectTimingGuard.Validate(0x25, 2, 0, 0, 0);
            AssertEx.Throws<InvalidOperationException>(() => S1InitialObjectTimingGuard.Validate(0x55,0,0,0xF20,0xF40), "Basaran");
            AssertEx.Throws<InvalidOperationException>(() => S1InitialObjectTimingGuard.Validate(0x2D,2,0,0,0), "unproven");
            AssertEx.Throws<InvalidOperationException>(() => S1InitialObjectTimingGuard.Validate(0x37,0,0,0,0), "unproven");
        }

        private static void CreditsSetup()
        {
            var host = new FakeS1Host(null); var output = new StringWriter();
            using (var observer = new S1InitializationTimingObserver(host, phase => Fingerprint,
                    () => true, phase => "sha256:" + new string('b', 64))) {
                host.FireExecuteCallback(S1InitializationTimingObserver.CreditsSetupBeginPc);
                observer.FlushFrame(10, output);
                host.FireExecuteCallback(S1InitializationTimingObserver.VblankEntryPc);
                host.FireExecuteCallback(S1InitializationTimingObserver.CreditsSetupEndPc);
                observer.FlushFrame(11, output); observer.VerifyComplete();
            }
            AssertEx.Equal(true, output.ToString().Contains("sha256:" + new string('b', 64)));
            AssertEx.Equal(true, output.ToString().Contains("\"operation\":\"vint_0\""));
            AssertEx.Equal(true, output.ToString().Contains("\"operation\":\"resume\""));
        }

        private static void RecordsPhases()
        {
            var host = new FakeS1Host(null); var output = new StringWriter();
            using (var observer = Open(host)) {
                host.FireExecuteCallback(S1InitializationTimingObserver.SetupBeginPc);
                observer.FlushFrame(10, output);
                host.FireExecuteCallback(S1InitializationTimingObserver.VblankEntryPc);
                observer.FlushFrame(11, output);
                host.FireExecuteCallback(S1InitializationTimingObserver.PlacementBeginPc);
                host.FireExecuteCallback(S1InitializationTimingObserver.ObjectsBeginPc);
                host.FireExecuteCallback(S1InitializationTimingObserver.PresentationBeginPc);
                host.FireExecuteCallback(S1InitializationTimingObserver.SetupEndPc);
                observer.FlushFrame(12, output); observer.VerifyComplete();
            }
            string[] rows = output.ToString().Split(new[] {'\n'}, StringSplitOptions.RemoveEmptyEntries);
            AssertEx.Equal(7, rows.Length);
            AssertEx.Equal(true, rows[0].Contains("\"raw_frame\":10,\"sequence\":0,\"load_ordinal\":1,\"phase_ordinal\":1"));
            AssertEx.Equal(true, rows[0].Contains("\"operation\":\"hold\""));
            AssertEx.Equal(true, rows[1].Contains("\"operation\":\"vint_0\""));
            AssertEx.Equal(true, rows[2].Contains("\"operation\":\"hold\""));
            AssertEx.Equal(true, rows[6].Contains("\"phase_ordinal\":4,\"phase\":\"initial_presentation\""));
            AssertEx.Equal(true, rows[6].Contains("\"operation\":\"resume\""));
        }
        private static void ExcludesBootstrap()
        {
            var host = new FakeS1Host(null); var output = new StringWriter();
            using (var observer = Open(host, false)) {
                host.FireExecuteCallback(S1InitializationTimingObserver.SetupBeginPc);
                host.FireExecuteCallback(S1InitializationTimingObserver.PlacementBeginPc);
                observer.FlushFrame(0, output); observer.VerifyComplete();
            }
            AssertEx.Equal("", output.ToString());
        }
        private static void RejectsOrdinary()
        {
            var host = new FakeS1Host(null);
            using (var observer = Open(host)) {
                host.FireExecuteCallback(S1InitializationTimingObserver.SetupBeginPc);
                host.Ram[0xF62A] = 0x18;
                AssertEx.Throws<InvalidOperationException>(() => host.FireExecuteCallback(
                    S1InitializationTimingObserver.VblankEntryPc), "ordinary interrupt");
            }
        }
        private static void RejectsObjectInterrupt()
        {
            var host = new FakeS1Host(null);
            using (var observer = Open(host)) {
                host.FireExecuteCallback(S1InitializationTimingObserver.SetupBeginPc);
                host.FireExecuteCallback(S1InitializationTimingObserver.PlacementBeginPc);
                host.FireExecuteCallback(S1InitializationTimingObserver.ObjectsBeginPc);
                AssertEx.Throws<InvalidOperationException>(() => host.FireExecuteCallback(
                    S1InitializationTimingObserver.VblankEntryPc), "unproven interrupt");
            }
        }
        private static void RejectsReordering()
        {
            var host = new FakeS1Host(null);
            using (var observer = Open(host)) {
                host.FireExecuteCallback(S1InitializationTimingObserver.SetupBeginPc);
                AssertEx.Throws<InvalidOperationException>(() => host.FireExecuteCallback(
                    S1InitializationTimingObserver.ObjectsBeginPc), "reordered");
            }
        }
        private static void RetailVectors()
        {
            byte[] rom = File.ReadAllBytes(Environment.GetEnvironmentVariable("S1_ROM_PATH"));
            var host = new FakeS1Host(null); host.Ram[S1Ram.Emeralds] = 6;
            var identity = S1InitializationTimingObserver.CreateFingerprintResolver(host, rom);
            AssertEx.Equal("sha256:5c7daa68eb01774b384e9b28e932da4e52914b5dfa1c3ca2edc71c4be0e04464", identity("level_setup"));
            host.Ram[S1Ram.Zone] = 6;
            AssertEx.Equal("sha256:397ccaa27bf7a281ca27ad68744b46341de4fc40ee40c7052a5fc8da6f6511cb", identity("initial_objects"));
            host.Ram[S1Ram.Act] = 1;
            AssertEx.Throws<InvalidDataException>(() => identity("initial_objects"), "selection changed");
            var demoHost = new FakeS1Host(null);
            demoHost.Ram[0xFFF4] = 0; demoHost.Ram[0xFFF5] = 1;
            var demoFingerprint = S1InitializationTimingObserver.CreateCreditsDemoFingerprintResolver(demoHost,
                File.ReadAllBytes(Environment.GetEnvironmentVariable("S1_ROM_PATH")));
            AssertEx.Equal("sha256:1bfcf6efe647cbab9512cfd74b92e106e866ba66717ff13a56095c50d85f2db0", demoFingerprint("level_setup"));
            var textHost = new FakeS1Host(null);
            AssertEx.Equal("sha256:2cc3baa64021206ae0768093d53508edca93c1513854977038a9005c4c6da7c9",
                S1InitializationTimingObserver.CreateCreditsTextFingerprintResolver(textHost,
                    File.ReadAllBytes(Environment.GetEnvironmentVariable("S1_ROM_PATH")))("level_setup"));
        }
    }
}
