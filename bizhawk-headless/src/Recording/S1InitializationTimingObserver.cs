using System;
using System.Collections.Generic;
using System.IO;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>
    /// Actual ending, credits-font and credits-demo initialization boundaries,
    /// World REV01, FixBugs=0 (sonic.asm:2710-2995,3560-3634,3829-3861).
    /// Exports no CPU state, progress or gameplay value. Credits callback
    /// commutation requires the reviewed fail-closed semantic guard.
    /// </summary>
    public sealed class S1InitializationTimingObserver : IDisposable
    {
        public const uint SetupBeginPc = 0x0050FE;
        public const uint PlacementBeginPc = 0x0051F2;
        public const uint ObjectsBeginPc = 0x00521C;
        public const uint PresentationBeginPc = 0x005222;
        // BuildSprites and its clock/RNG-free reset/oscillation tail finish
        // before the first counted VBlank_Ending wait (sonic.asm:3634-3652).
        public const uint SetupEndPc = 0x00526E;
        public const uint VblankEntryPc = 0x000B10;
        public const uint CreditsSetupBeginPc = 0x0057DA;
        public const uint CreditsSetupEndPc = 0x005876;
        public const uint DemoSetupBeginPc = 0x0037BA;
        public const uint DemoPlacementBeginPc = 0x00396C;
        public const uint DemoObjectsBeginPc = 0x0039C4;
        public const uint DemoPresentationBeginPc = 0x0039CA;
        public const uint DemoSetupEndPc = 0x003A8C;
        public const uint InitialObjectDispatchPc = 0x00D9E2;
        private readonly List<IDisposable> hooks = new List<IDisposable>();
        private readonly InitializationTimingEventEngine events = new InitializationTimingEventEngine();
        private readonly Func<string, string> fingerprint;
        private readonly Func<string, string> creditsTextFingerprint;
        private bool creditsTextLoading, creditsDemoLoading;
        private readonly Func<string, string> creditsDemoFingerprint;
        private bool loading;
        private bool disposed;

        public S1InitializationTimingObserver(IGpgxHost host, byte[] rom,
            Func<bool> representedRunStarted)
            : this(host, CreateFingerprintResolver(host, rom), representedRunStarted,
                    CreateCreditsTextFingerprintResolver(host, rom), CreateCreditsDemoFingerprintResolver(host, rom), rom) { }

        internal S1InitializationTimingObserver(IGpgxHost host,
            Func<string, string> fingerprint, Func<bool> representedRunStarted,
            Func<string, string> creditsTextFingerprint = null,
            Func<string, string> creditsDemoFingerprint = null, byte[] callbackRom = null)
        {
            if (host == null || fingerprint == null || representedRunStarted == null)
                throw new ArgumentNullException();
            this.fingerprint = fingerprint;
            this.creditsTextFingerprint = creditsTextFingerprint;
            this.creditsDemoFingerprint = creditsDemoFingerprint;
            try
            {
                Register(host, SetupBeginPc, () => {
                    if (!representedRunStarted()) return;
                    if (loading) throw new InvalidOperationException("nested S1 ending load");
                    creditsTextLoading = false; creditsDemoLoading = false;
                    events.BeginLoad(); Begin("level_setup"); loading = true;
                });
                Register(host, PlacementBeginPc, () => Transition("level_setup", "initial_placement"));
                Register(host, ObjectsBeginPc, () => Transition("initial_placement", "initial_objects"));
                Register(host, PresentationBeginPc, () => Transition("initial_objects", "initial_presentation"));
                Register(host, SetupEndPc, () => {
                    if (!loading) return;
                    if (events.ActivePhase != "initial_presentation")
                        throw new InvalidOperationException("S1 ending setup ended out of order");
                    events.ResumePhase(); loading = false;
                });
                if (creditsTextFingerprint != null)
                {
                    Register(host, CreditsSetupBeginPc, () => {
                        if (!representedRunStarted()) return;
                        if (loading) throw new InvalidOperationException("nested S1 credits initialization");
                        creditsTextLoading = true; creditsDemoLoading = false;
                        events.BeginLoad(); Begin("level_setup"); loading = true;
                    });
                    Register(host, CreditsSetupEndPc, () => {
                        if (!loading || !creditsTextLoading) return;
                        events.ResumePhase(); loading = false;
                    });
                }
                if (creditsDemoFingerprint != null)
                {
                    Register(host, DemoSetupBeginPc, () => {
                        if (!representedRunStarted() || (S1Ram.U16(host, 0xFFF0) & 0x8000) == 0) return;
                        if (loading) throw new InvalidOperationException("nested S1 credits demo initialization");
                        creditsTextLoading = false; creditsDemoLoading = true;
                        events.BeginLoad(); Begin("level_setup"); loading = true;
                    });
                    Register(host, DemoPlacementBeginPc, () => { if (creditsDemoLoading) Transition("level_setup", "initial_placement"); });
                    Register(host, DemoObjectsBeginPc, () => { if (creditsDemoLoading) Transition("initial_placement", "initial_objects"); });
                    Register(host, DemoPresentationBeginPc, () => { if (creditsDemoLoading) Transition("initial_objects", "initial_presentation"); });
                    Register(host, DemoSetupEndPc, () => {
                        if (!loading || !creditsDemoLoading) return;
                        if (events.ActivePhase != "initial_presentation") throw new InvalidOperationException("credits demo setup ended out of order");
                        events.ResumePhase(); loading = false;
                    });
                    if (callbackRom != null) Register(host, InitialObjectDispatchPc, () => {
                        if (!loading || !creditsDemoLoading || events.ActivePhase != "initial_objects") return;
                        var cpu = host as ICpuRegisterReader;
                        if (cpu == null) throw new InvalidOperationException("S1 callback qualification needs native register observation");
                        int address = (int)(cpu.ReadCpuRegister("M68K A0") & 0xFFFF);
                        int id = host.ReadMainRamByte(address), routine = host.ReadMainRamByte(address + 0x24);
                        if (address < 0xD000 || address >= 0xF000 || (address & 0x3F) != 0 || id == 0
                            || (cpu.ReadCpuRegister("M68K A1") & 0xFFFFFF) != U32(callbackRom, 0xDA10 + (id - 1) * 4))
                            throw new InvalidOperationException("unproven S1 initial callback owner");
                        S1InitialObjectTimingGuard.Validate(id, routine, host.ReadMainRamByte(address + 0x25),
                            S1Ram.U16(host, 0xD008), S1Ram.U16(host, address + 8));
                    });
                    // LZWindTunnels / WaterSlides conditional counter reads
                    // (LZWaterFeatures.asm:376,519). Never commute across an
                    // unproven selected branch, even when its sound is inaudible.
                    foreach (uint pc in new uint[] { 0x3E7E, 0x3FAC }) Register(host, pc, () => {
                        if (loading && creditsDemoLoading && events.ActivePhase == "level_setup")
                            throw new InvalidOperationException("unproven S1 setup VBlank read");
                    });
                }
                Register(host, VblankEntryPc, () => {
                    if (!loading) return;
                    if (host.ReadMainRamByte(0xF62A) != 0) events.ObserveOrdinaryInterrupt();
                    if (events.ActivePhase == "initial_objects" && !creditsDemoLoading)
                        throw new InvalidOperationException("unproven interrupt inside S1 initial object callbacks");
                    // VBlank_Lag (sonic.asm:712-754) in GM_Ending services
                    // music and v_vblank_count, never PLC/DPLC or gameplay.
                    events.ObserveZeroRoutineInterrupt();
                });
            }
            catch { Dispose(); throw; }
        }

        public static Func<string, string> CreateCreditsDemoFingerprintResolver(IGpgxHost host, byte[] rom)
        {
            if (host == null || rom == null) throw new ArgumentNullException();
            RomIdentity.ValidateSonic1Rev01(rom);
            int? selectedPage = null;
            return phase => {
                if (phase == "level_setup") selectedPage = S1Ram.U16(host, 0xFFF4) - 1;
                if (!selectedPage.HasValue || selectedPage.Value < 0 || selectedPage.Value >= 8)
                    throw new InvalidDataException("invalid native credits demo selection");
                int page = selectedPage.Value, demoLevel = 0x5906 + page * 2;
                int zoneAct = U16(rom, demoLevel), zone = zoneAct >> 8, act = zoneAct & 0xFF;
                if (host.ReadMainRamByte(S1Ram.Zone) != zone || host.ReadMainRamByte(S1Ram.Act) != act)
                    throw new InvalidDataException("native credits demo sources changed");
                int index = zone * 4 + act, layouts = 0x68B96, layout = layouts + index * 6;
                int collision = 0x4102 + zone * 4, demoInput = 0x40A2 + page * 4;
                var regions = new List<InitializationTimingFingerprint.RegionSelection> {
                    new InitializationTimingFingerprint.RegionSelection(demoLevel, 2),
                    new InitializationTimingFingerprint.RegionSelection(0x607A + page * 4, 4),
                    new InitializationTimingFingerprint.RegionSelection(0x1DD16 + zone * 16, 16),
                    new InitializationTimingFingerprint.RegionSelection(layout, 6),
                    new InitializationTimingFingerprint.RegionSelection(0x6B000 + index * 4, 4),
                    new InitializationTimingFingerprint.RegionSelection(0x5F2A + index * 12, 12),
                    new InitializationTimingFingerprint.RegionSelection(collision, 4),
                    new InitializationTimingFingerprint.RegionSelection(demoInput, 4)
                };
                if (page == 3) regions.Add(new InitializationTimingFingerprint.RegionSelection(0x5916, 36));
                return InitializationTimingFingerprint.Compute(rom, phase, regions,
                    new List<int> { layouts + U16(rom, layout), layouts + U16(rom, layout + 2),
                        (int)U32(rom, collision), (int)U32(rom, demoInput) });
            };
        }
        private static uint U32(byte[] rom, int address) { return (uint)U16(rom, address) << 16 | (uint)U16(rom, address + 2); }

        public static Func<string, string> CreateCreditsTextFingerprintResolver(IGpgxHost host, byte[] rom)
        {
            if (host == null || rom == null) throw new ArgumentNullException();
            RomIdentity.ValidateSonic1Rev01(rom);
            return phase => {
                if (phase != "level_setup") throw new InvalidDataException("invalid credits setup phase");
                int page = S1Ram.U16(host, 0xFFF4);
                if (page < 0 || page > 8) throw new InvalidDataException("invalid native credits page");
                int maps = 0x17A8E;
                int row = maps + page * 2;
                int frame = maps + U16(rom, row);
                int demo = 0x5906 + page * 2;
                int zone = U16(rom, demo) >> 8;
                int font = 0x6203A;
                var regions = new List<InitializationTimingFingerprint.RegionSelection> {
                    new InitializationTimingFingerprint.RegionSelection(font, 2),
                    new InitializationTimingFingerprint.RegionSelection(row, 2),
                    new InitializationTimingFingerprint.RegionSelection(frame, 1 + rom[frame] * 5),
                    new InitializationTimingFingerprint.RegionSelection(0x2178, 8),
                    new InitializationTimingFingerprint.RegionSelection(demo, 2),
                    new InitializationTimingFingerprint.RegionSelection(0x1DD16 + zone * 16, 16)
                };
                return InitializationTimingFingerprint.Compute(rom, phase, regions,
                    new List<int> { 0xB400, maps, U16(rom, font) & 0x7fff });
            };
        }

        public static Func<string, string> CreateFingerprintResolver(IGpgxHost host, byte[] rom)
        {
            if (host == null || rom == null) throw new ArgumentNullException();
            RomIdentity.ValidateSonic1Rev01(rom);
            ValidateObserverCuts(rom);
            int? selectedAct = null;
            return phase => {
                // GM_Ending chooses good/bad resources from emerald progress,
                // after the clear prefix. Freeze that native source selection;
                // no clock or comparison field participates in identity.
                if (phase == "level_setup") selectedAct = host.ReadMainRamByte(S1Ram.Emeralds) == 6 ? 0 : 1;
                if (!selectedAct.HasValue) throw new InvalidOperationException("S1 ending sources not selected");
                if (phase != "level_setup" && (host.ReadMainRamByte(S1Ram.Zone) != 6
                        || host.ReadMainRamByte(S1Ram.Act) != selectedAct.Value))
                    throw new InvalidDataException("native ending source selection changed");
                int index = 6 * 4 + selectedAct.Value;
                int header = 0x1DD16 + 6 * 16;
                int plcOffset = 0x1DD86 + 0x1C * 2;
                int plc = 0x1DD86 + U16(rom, plcOffset);
                int count = (short)U16(rom, plc) + 1;
                if (count < 0) throw new InvalidDataException("invalid ending PLC count");
                int layout = 0x68B96 + index * 6;
                var regions = new List<InitializationTimingFingerprint.RegionSelection> {
                    new InitializationTimingFingerprint.RegionSelection(header, 16),
                    new InitializationTimingFingerprint.RegionSelection(plcOffset, 2),
                    new InitializationTimingFingerprint.RegionSelection(plc, 2 + count * 6),
                    new InitializationTimingFingerprint.RegionSelection(layout, 6),
                    new InitializationTimingFingerprint.RegionSelection(0x6B000 + index * 4, 4),
                    new InitializationTimingFingerprint.RegionSelection(0x0611E + index * 4, 4),
                    new InitializationTimingFingerprint.RegionSelection(0x05F2A + index * 12, 12)
                };
                var choices = new List<int> { 0x68B96 + U16(rom, layout),
                    0x68B96 + U16(rom, layout + 2), 0x61822, 0x64A00 };
                return InitializationTimingFingerprint.Compute(rom, phase, regions, choices);
            };
        }

        internal static void ValidateObserverCuts(byte[] rom)
        {
            // Independently checked against the shipped REV01 instructions.
            var cuts = new Dictionary<uint, string> {
                { SetupBeginPc, "43F8D000" }, { PlacementBeginPc, "11FC0001D000" },
                { ObjectsBeginPc, "4EB90000D9C6" }, { PresentationBeginPc, "4EB90000DCEC" },
                { SetupEndPc, "11FC0018F62A" }, { VblankEntryPc, "48E7FFFE" },
                { CreditsSetupBeginPc, "4DF900C00004" }, { CreditsSetupEndPc, "31FC0078F614" },
                { DemoSetupBeginPc, "4A78FFF0" }, { DemoPlacementBeginPc, "11FC0001D000" },
                { DemoObjectsBeginPc, "4EB90000D9C6" }, { DemoPresentationBeginPc, "4EB90000DCEC" },
                { DemoSetupEndPc, "323C0003" }, { InitialObjectDispatchPc, "4E91" },
                { 0x3E7E, "1038FE0F" }, { 0x3FAC, "1038FE0F" }
            };
            foreach (var cut in cuts)
                for (int i = 0; i < cut.Value.Length / 2; i++)
                    if (rom[cut.Key + i] != Convert.ToByte(cut.Value.Substring(i * 2, 2), 16))
                        throw new InvalidDataException("S1 initialization opcode mismatch at " + cut.Key.ToString("X6"));
        }

        private static int U16(byte[] rom, int address)
        {
            if (address < 0 || address + 2 > rom.Length) throw new InvalidDataException("ending source outside ROM");
            return (rom[address] << 8) | rom[address + 1];
        }
        private void Register(IGpgxHost host, uint pc, Action action)
        {
            var registration = host.RegisterExecuteCallback(pc, action);
            if (registration == null) throw new InvalidOperationException("missing S1 initialization hook");
            hooks.Add(registration);
        }
        private void Begin(string phase) { events.BeginPhase(phase, (creditsDemoLoading ? creditsDemoFingerprint : creditsTextLoading ? creditsTextFingerprint : fingerprint)(phase)); }
        private void Transition(string expected, string next)
        {
            if (!loading) return;
            if (events.ActivePhase != expected) throw new InvalidOperationException("S1 initialization phase reordered");
            events.ResumePhase(); Begin(next);
        }
        public void FlushFrame(int rawFrame, TextWriter output) { events.FlushFrame(rawFrame, output); }
        public void VerifyComplete()
        {
            if (loading) throw new InvalidOperationException("incomplete S1 ending initialization");
            events.VerifyComplete();
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (int i = hooks.Count - 1; i >= 0; i--) hooks[i].Dispose();
            hooks.Clear();
        }
    }
}
