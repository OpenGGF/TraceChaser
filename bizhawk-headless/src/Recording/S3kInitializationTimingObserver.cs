using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>
    /// Semantic hooks for the verified locked-on Level routine. Source:
    /// skdisasm 8b2cf3c4 Level/loc_6000, loc_62CC, loc_6310, loc_6468,
    /// loc_64DC and VInt/VInt_0_Main. CPU state is never exported.
    /// </summary>
    public sealed class S3kInitializationTimingObserver : IDisposable
    {
        private readonly List<IDisposable> hooks = new List<IDisposable>();
        private readonly InitializationTimingEventEngine events = new InitializationTimingEventEngine();
        private readonly Func<string, string> fingerprint;
        private bool disposed;
        private bool loading;
        private bool initialServiceCutOpen;
        private bool presentationServiceCutOpen;
        private bool validateRemainingObjectCallbacks;
        private string lastObjectTailDiagnostic = "no object-tail callback observed";

        public S3kInitializationTimingObserver(IGpgxHost host, byte[] rom, Func<string, string> fingerprint)
        {
            if (host == null || rom == null || fingerprint == null) throw new ArgumentNullException();
            using (SHA1 sha = SHA1.Create())
            {
                string identity = BitConverter.ToString(sha.ComputeHash(rom)).Replace("-", "");
                if (identity != "CFBF98C36C776677290A872547AC47C53D2761D6")
                    throw new InvalidDataException("unsupported initialization hook ROM identity");
            }
            this.fingerprint = fingerprint;
            try
            {
                Register(host, 0x6000, () => {
                    if (loading) throw new InvalidOperationException("nested native initialization load");
                    // VInt_0_NoWater ($6FA) tests Competition_mode at $FFE8;
                    // that branch publishes SAT and is outside this contract.
                    if (S3KRam.U16(host, 0xFFE8) != 0)
                        throw new InvalidOperationException("competition initialization timing is unsupported");
                    events.BeginLoad(); Begin("display_clear"); loading = true;
                });
                Register(host, 0x62CC, () => {
                    if (loading && events.ActivePhase == "display_clear") events.ResumePhase();
                });
                Register(host, 0x6326, () => { if (loading) Begin("level_data_decode"); });
                Register(host, 0x632C, () => Transition("level_data_decode", "level_setup"));
                Register(host, 0x6468, () => {
                    if (!loading) return;
                    initialServiceCutOpen = false;
                    validateRemainingObjectCallbacks = false;
                    Transition("level_setup", "initial_placement");
                });
                // The real placement/ring-scan prefix precedes any object
                // clock/RNG reads. First Process_Sprites begins a distinct phase.
                Register(host, 0x647E, () => {
                    if (!loading) return;
                    Transition("initial_placement", "initial_objects");
                    initialServiceCutOpen = false;
                });
                // Obj_GumballMachine: callback AFTER move VInt->RNG completes.
                Register(host, 0x60C80, () => {
                    if (events.ActivePhase == "initial_objects") initialServiceCutOpen = true;
                });
                // sub_1AAFC's post-callback boundary precedes LEA/DBF.
                // If every remaining entry is empty, all object callbacks have
                // finished. Only the clock/RNG-free empty scan remains before
                // Render_Sprites, so its interrupts belong to presentation.
                Register(host, 0x1AB04, () => {
                    if (events.ActivePhase == "initial_objects" && RemainingObjectTailIsEmpty(host)) {
                        Transition("initial_objects", "initial_presentation");
                        presentationServiceCutOpen = true;
                    }
                });
                // Reference execute hook immediately before JSR (a1). Validate
                // actual later dispatches, including newly allocated children.
                Register(host, 0x1AB02, () => {
                    if (validateRemainingObjectCallbacks) ValidateIndependentObjectDispatch(host);
                });
                Register(host, 0x6484, () => {
                    if (!loading) return;
                    if (events.ActivePhase == "initial_objects")
                        Transition("initial_objects", "initial_presentation");
                    else if (events.ActivePhase != "initial_presentation")
                        throw new InvalidOperationException("initial rendering has wrong phase");
                    presentationServiceCutOpen = true;
                    validateRemainingObjectCallbacks = false;
                });
                Register(host, 0x648A, () => {
                    if (events.ActivePhase == "initial_presentation") presentationServiceCutOpen = false;
                });
                Register(host, 0x64DC, () => {
                    if (!loading) return;
                    if (events.ActivePhase != "initial_presentation")
                        throw new InvalidOperationException("native initialization release has wrong phase");
                    events.ResumePhase(); loading = false;
                });
                // Exact primary locked-on VInt prologue; vector $78 targets a RAM trampoline.
                Register(host, 0x584, () => {
                    if (events.ActivePhase != null && host.ReadMainRamByte(0xF62A) != 0)
                        events.ObserveOrdinaryInterrupt();
                });
                Register(host, 0x608, () => {
                    if (events.ActivePhase == "initial_objects" && !initialServiceCutOpen) {
                        if (!IsBetweenObjectCallbacks(host))
                            throw new InvalidOperationException("unsupported initial-object interrupt placement at BK2 row "
                                + host.CompletedFrame + "; " + lastObjectTailDiagnostic);
                        // Commuting this service past the remaining callbacks is
                        // conditional on observing their actual reviewed targets.
                        // Any later unknown target aborts the no-replace staging
                        // transaction, including rows already written to it.
                        Transition("initial_objects", "initial_presentation");
                        presentationServiceCutOpen = true;
                        validateRemainingObjectCallbacks = true;
                    }
                    if (events.ActivePhase == "initial_presentation" && !presentationServiceCutOpen)
                        throw new InvalidOperationException("unsupported initial-presentation interrupt after animation entry");
                    events.ObserveZeroRoutineInterrupt();
                });
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>ROM table selections made by the native fresh-Level routines,
        /// matching production NativeLevelLoadInputs. This reads source identity,
        /// never counters, decompressor progress, or decoded gameplay values.</summary>
        public static Func<string, string> CreateFingerprintResolver(IGpgxHost host, byte[] rom)
        {
            return phase => {
                int zone = host.ReadMainRamByte(S3KRam.Zone);
                int act = host.ReadMainRamByte(S3KRam.Act);
                int mode = S3KRam.U16(host, S3KRam.PlayerMode);
                if (act > 1 || mode > 3 || zone >= 0x20)
                    throw new InvalidDataException("unsupported native initialization table selection");
                int index = zone * 2 + act;
                var regions = new List<InitializationTimingFingerprint.RegionSelection>();
                var choices = new List<int>();
                if (phase == "display_clear") choices.Add(0);
                else if (phase == "level_data_decode") {
                    // LoadLevelLoadBlock2, loc_1C2C4 selects entry 26 for
                    // AIZ1 Tails/Knuckles or a retained starpost.
                    int blockIndex = index == 0 && (mode >= 2 || host.ReadMainRamByte(S3KRam.LastStarPostHit) != 0)
                        ? 26 : index;
                    int block = 0x91F0C + blockIndex * 24;
                    int layout = 0x9D5C0 + index * 4;
                    regions.Add(new InitializationTimingFingerprint.RegionSelection(block, 24));
                    regions.Add(new InitializationTimingFingerprint.RegionSelection(layout, 4));
                    for (int offset = 0; offset < 24; offset += 4) choices.Add(ReadPointer(rom, block + offset));
                    choices.Add(ReadPointer(rom, layout));
                } else if (phase == "level_setup") {
                    // LevelSetupArray interleaves setup and event pairs.
                    regions.Add(new InitializationTimingFingerprint.RegionSelection(0x4E458 + zone * 32 + act * 8, 8));
                    regions.Add(new InitializationTimingFingerprint.RegionSelection(0x1BCC6 + index * 8, 8));
                    choices.Add(mode);
                } else if (phase == "initial_placement" || phase == "initial_objects" || phase == "initial_presentation") {
                    regions.Add(new InitializationTimingFingerprint.RegionSelection(0x1E3D98 + index * 4, 4));
                    regions.Add(new InitializationTimingFingerprint.RegionSelection(0x1E3E58 + index * 4, 4));
                    regions.Add(new InitializationTimingFingerprint.RegionSelection((mode == 3 ? 0x1E3CD8 : 0x1E3C18) + index * 4, 4));
                    choices.Add(mode);
                } else throw new ArgumentException("unsupported initialization phase");
                return InitializationTimingFingerprint.Compute(rom, phase, regions, choices);
            };
        }

        private static int ReadPointer(byte[] rom, int address)
        {
            if (address < 0 || address + 4 > rom.Length) throw new InvalidDataException("ROM pointer outside source");
            return (rom[address + 1] << 16) | (rom[address + 2] << 8) | rom[address + 3];
        }

        private static uint RamLong(IGpgxHost host, int address)
        {
            uint value = 0;
            for (int i = 0; i < 4; i++) value = (value << 8) | host.ReadMainRamByte((address + i) & 0xFFFF);
            return value;
        }

        private bool IsBetweenObjectCallbacks(IGpgxHost host)
        {
            var cpu = host as ICpuRegisterReader;
            if (cpu == null) return false;
            int stack = (int)(cpu.ReadCpuRegister("M68K A7") & 0xFFFF);
            uint pc = RamLong(host, stack + 62);
            uint pointer = RamLong(host, stack + 32);
            uint count = RamLong(host, stack + 28) & 0xFFFF;
            // Reusable rejection evidence replaces a separate CPU-state probe.
            // These observations never enter an accepted timing event.
            lastObjectTailDiagnostic = "IRQ PC=" + pc.ToString("X6")
                + " A0=" + pointer.ToString("X8") + " D7=" + count;
            if (pc != 0x1AAFC && pc != 0x1AAFE && pc != 0x1AB00
                && pc != 0x1AB02 && pc != 0x1AB04 && pc != 0x1AB08) return false;
            if ((pointer & 0xFFFF0000) != 0xFFFF0000
                && (pointer & 0xFFFF0000) != 0x00FF0000) return false;
            int current = (int)(pointer & 0xFFFF);
            int end = S3KRam.PlayerBase + S3KRam.TotalObjectSlots * S3KRam.ObjectSlotSize;
            if (current < S3KRam.PlayerBase || current > end
                || (current - S3KRam.PlayerBase) % S3KRam.ObjectSlotSize != 0) return false;
            // At DBF the LEA has advanced A0 but D7 has not decremented.
            int remaining = (end - current) / S3KRam.ObjectSlotSize;
            if (pc != 0x1AB08) remaining--;
            return remaining >= 0 && count == (uint)remaining;
        }

        private static void ValidateIndependentObjectDispatch(IGpgxHost host)
        {
            var cpu = host as ICpuRegisterReader;
            if (cpu == null) throw new InvalidOperationException("missing native object dispatch observation");
            uint pointer = cpu.ReadCpuRegister("M68K A0");
            uint target = cpu.ReadCpuRegister("M68K A1");
            int address = (int)(pointer & 0xFFFF);
            int end = S3KRam.PlayerBase + S3KRam.TotalObjectSlots * S3KRam.ObjectSlotSize;
            if (((pointer & 0xFFFF0000) != 0xFFFF0000 && (pointer & 0xFFFF0000) != 0x00FF0000)
                || address < S3KRam.PlayerBase || address >= end
                || (address - S3KRam.PlayerBase) % S3KRam.ObjectSlotSize != 0
                || RamLong(host, address) != target)
                throw new InvalidOperationException("native object dispatch sampling boundary mismatch");
            // Reviewed closures in skdisasm 8b2cf3c4: StillSprite, TitleCard,
            // TitleCardElement/RedBanner, first SSEntryRing invocation,
            // HCZSnakeBlocks/SpinningColumn, DashDust and HCZWaveSplash.
            // These use local/level counters, never V_int_run_count/Lag_frame_count.
            // VInt_0 does not service their queued art. Unknown children fail closed.
            switch (target) {
                case 0x2B928: case 0x2D690: case 0x2D95C: case 0x2D8E2:
                case 0x6166A: case 0x256BE: case 0x32656: case 0x18B3E: case 0x1F202:
                    return;
                default:
                    throw new InvalidOperationException("unreviewed remaining initialization callback "
                        + target.ToString("X6") + " at BK2 row " + host.CompletedFrame);
            }
        }

        private bool RemainingObjectTailIsEmpty(IGpgxHost host)
        {
            // Reference-only native execution observation. No register, slot or
            // object value is exported; production must independently complete
            // its real object pass before matching the next semantic phase.
            var nativeCpu = host as ICpuRegisterReader;
            if (nativeCpu == null) return false;
            uint pointer = nativeCpu.ReadCpuRegister("M68K A0");
            uint loopCount = nativeCpu.ReadCpuRegister("M68K D7") & 0xFFFF;
            // Rejection diagnostics only: never serialized into timing input.
            lastObjectTailDiagnostic = "tail A0=" + pointer.ToString("X8") + " D7=" + loopCount;
            if ((pointer & 0xFFFF0000) != 0xFFFF0000
                && (pointer & 0xFFFF0000) != 0x00FF0000) return false;
            int current = (int)(pointer & 0xFFFF);
            int end = S3KRam.PlayerBase + S3KRam.TotalObjectSlots * S3KRam.ObjectSlotSize;
            if (current < S3KRam.PlayerBase || current >= end
                || (current - S3KRam.PlayerBase) % S3KRam.ObjectSlotSize != 0) return false;
            int next = current + S3KRam.ObjectSlotSize;
            // The actual DBF traversal, not merely the address, must end at
            // the verified SST boundary. Partial/damaged traversals fail closed.
            uint expected = (uint)((end - next) / S3KRam.ObjectSlotSize);
            if (loopCount != expected) {
                lastObjectTailDiagnostic += " expected D7=" + expected;
                return false;
            }
            for (int address = next; address < end; address += S3KRam.ObjectSlotSize) {
                for (int offset = 0; offset < 4; offset++)
                    if (host.ReadMainRamByte(address + offset) != 0) {
                        lastObjectTailDiagnostic += " remaining live head at " + address.ToString("X4");
                        return false;
                    }
            }
            return true;
        }

        private void Register(IGpgxHost host, uint address, Action callback)
        {
            IDisposable hook = host.RegisterExecuteCallback(address, callback);
            if (hook == null) throw new InvalidOperationException("missing initialization execute hook");
            hooks.Add(hook);
        }

        private void Begin(string phase) { events.BeginPhase(phase, fingerprint(phase)); }

        private void Transition(string expected, string next)
        {
            if (!loading) return;
            if (events.ActivePhase != expected)
                throw new InvalidOperationException("unexpected native initialization phase transition");
            events.ResumePhase(); Begin(next);
        }

        public void FlushFrame(int rawFrame, TextWriter output)
        {
            if (disposed) throw new ObjectDisposedException("S3kInitializationTimingObserver");
            events.FlushFrame(rawFrame, output);
        }

        public void DiscardUnrepresentedFrame() { events.DiscardUnrepresentedFrame(); }

        /// <summary>Success-path publication gate; Dispose remains failure-safe cleanup.</summary>
        public void VerifyComplete()
        {
            if (disposed) throw new ObjectDisposedException("S3kInitializationTimingObserver");
            if (loading || validateRemainingObjectCallbacks)
                throw new InvalidOperationException("incomplete native initialization or provisional callback tail");
            events.VerifyComplete();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (int index = hooks.Count - 1; index >= 0; index--) hooks[index].Dispose();
            hooks.Clear();
        }
    }
}
