using System;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using BizHawk.BizInvoke;

namespace OpenGGF.BizHawk.Headless
{
    public enum GpgxResetPrefixKind : uint
    {
        ResetEnter = 1, ResetComplete = 2, NativeInitComplete = 3,
        Fetch = 4, Retire = 5, Read = 6, Write = 7
    }

    // The fixed 40-word native record is copied into an immutable value snapshot. Knownness=1 means initialized
    // by this reference emulator, never a claim about arbitrary physical console power-on RAM/register contents.
    public sealed class GpgxResetPrefixRecord
    {
        private readonly uint[] words;
        internal GpgxResetPrefixRecord(uint[] source, int offset)
        { words = new uint[40]; Array.Copy(source, offset, words, 0, words.Length); }

        public GpgxResetPrefixKind Kind { get { return (GpgxResetPrefixKind)words[0]; } }
        public uint Ordinal { get { return words[1]; } }
        // Current native PC: operand reads and retirement can advance it beyond the fetched instruction.
        public uint Pc { get { return words[2]; } }
        public uint RawCycles { get { return words[3]; } }
        public uint RefreshDeadline { get { return words[4]; } }
        public uint Sr { get { return words[5]; } }
        public uint Irq { get { return words[6]; } }
        public uint Z80Cycles { get { return words[7]; } }
        public uint Z80State { get { return words[8]; } }
        public uint Z80Bank { get { return words[9]; } }
        public uint Address { get { return words[10]; } }
        public uint Value { get { return words[11]; } }
        public uint Width { get { return words[12]; } }
        // Kind NativeInitComplete reuses the bus payload for the source-owned controller/SRAM profile.
        public uint ControllerPortSystems { get { RequireInitProfile(); return words[10]; } }
        public uint ControllerDevices { get { RequireInitProfile(); return words[11]; } }
        public uint SramFlags { get { RequireInitProfile(); return words[12]; } }
        public uint Knownness { get { return words[13]; } }
        public uint InstructionRegister { get { return words[30]; } }
        public uint SystemHardware { get { return words[31]; } }
        public uint RegionCode { get { return words[32]; } }
        public uint VdpPal { get { return words[33]; } }
        public uint LinesPerFrame { get { return words[34]; } }
        public uint HardwareVersion { get { return words[35]; } }
        public uint IoControlLatches { get { return words[36]; } }
        public uint Bios { get { return words[37]; } }
        public uint RomSize { get { return words[38]; } }
        public uint Auxiliary { get { return words[39]; } }
        public uint InstructionStartPc
        {
            get
            {
                if (Kind < GpgxResetPrefixKind.Fetch || Kind > GpgxResetPrefixKind.Write)
                    throw new InvalidOperationException("Instruction-start PC is present only on instruction events.");
                return words[39];
            }
        }
        public uint GetDataRegister(int index) { RequireRegister(index); return words[14 + index]; }
        public uint GetAddressRegister(int index) { RequireRegister(index); return words[22 + index]; }
        public uint GetWord(int index)
        {
            if (index < 0 || index >= words.Length) throw new ArgumentOutOfRangeException("index");
            return words[index];
        }
        private static void RequireRegister(int index)
        { if (index < 0 || index > 7) throw new ArgumentOutOfRangeException("index"); }
        private void RequireInitProfile()
        {
            if (Kind != GpgxResetPrefixKind.NativeInitComplete)
                throw new InvalidOperationException("Controller/SRAM profile is present only on native init completion.");
        }
    }

    public sealed class GpgxResetPrefixCapture
    {
        internal GpgxResetPrefixCapture(GpgxResetPrefixRecord[] records, bool complete)
        { Records = Array.AsReadOnly(records); IsComplete = complete; }
        public ReadOnlyCollection<GpgxResetPrefixRecord> Records { get; private set; }
        public bool IsComplete { get; private set; }
    }

    public abstract class GpgxResetPrefixDepartures
    {
        [BizImport(CallingConvention.Cdecl)] public abstract uint gpgx_reset_prefix_abi_version();
        [BizImport(CallingConvention.Cdecl)] public abstract int gpgx_reset_prefix_layout([Out] uint[] layout, uint count);
        [BizImport(CallingConvention.Cdecl)] public abstract int gpgx_reset_prefix_enable(uint abiVersion);
        [BizImport(CallingConvention.Cdecl)] public abstract int gpgx_reset_prefix_read([Out] uint[] words,
            uint capacityWords, out uint records, out uint status);
        [BizImport(CallingConvention.Cdecl)] public abstract int gpgx_reset_prefix_disable();
    }

    // Diagnostic only. Native init captures genuine constructor reset provenance before any CPU retirement;
    // Enable only arms its already-existing cold boundary. It does not reset the emulator or clear sticky faults.
    // Read only between FrameAdvance calls; these comparison records must never hydrate gameplay or a replay.
    public sealed class GpgxResetPrefixNative
    {
        public const uint AbiVersion = 1, RecordSize = 160, RecordWords = 40, Capacity = 128;
        private readonly GpgxResetPrefixDepartures departures;
        private readonly Func<long> loadGeneration;
        private readonly Func<int> completedFrame;
        private bool armed, consumed, invalid;
        private long armedGeneration;

        private GpgxResetPrefixNative(GpgxResetPrefixDepartures departures, Func<long> loadGeneration,
            Func<int> completedFrame)
        {
            if (departures == null) throw new ArgumentNullException("departures");
            this.departures = departures; this.loadGeneration = loadGeneration; this.completedFrame = completedFrame;
            if (departures.gpgx_reset_prefix_abi_version() != AbiVersion)
                throw new InvalidOperationException("GPGX reset-prefix ABI identity differs from v1.");
            var layout = new uint[4];
            if (departures.gpgx_reset_prefix_layout(layout, 4) != 0 || layout[0] != RecordSize
                || layout[1] != Capacity || layout[2] != RecordWords || layout[3] != AbiVersion)
                throw new InvalidOperationException("GPGX reset-prefix layout differs from v1/160/128/40.");
        }

        public static GpgxResetPrefixNative Bind(GpgxHost host)
        {
            if (host == null) throw new ArgumentNullException("host");
            return new GpgxResetPrefixNative(host.BindDiagnosticDeparture<GpgxResetPrefixDepartures>(),
                () => host.LoadGeneration, () => host.CompletedFrame);
        }

        internal static GpgxResetPrefixNative BindForTests(GpgxResetPrefixDepartures departures,
            Func<long> loadGeneration, Func<int> completedFrame)
        { return new GpgxResetPrefixNative(departures, loadGeneration, completedFrame); }

        public void Enable()
        {
            RequireValid();
            if (loadGeneration() != 0) Fail("A savestate was loaded before reset-prefix arm; provenance is invalid.");
            if (completedFrame() != 0) Fail("Reset-prefix arm must precede the first fetch; a frame has already completed.");
            if (consumed) Fail("The reset-prefix constructor boundary was already consumed.");
            int result = departures.gpgx_reset_prefix_enable(AbiVersion);
            if (result != 0) Fail("GPGX reset-prefix enable refused: " + result);
            armedGeneration = loadGeneration(); armed = true; consumed = true;
        }

        public GpgxResetPrefixCapture Read()
        {
            RequireValid();
            if (!armed) throw new InvalidOperationException("GPGX reset-prefix is not armed.");
            if (loadGeneration() != armedGeneration)
                Fail("A savestate was loaded after reset-prefix arm; provenance is invalid.");
            var words = new uint[Capacity * RecordWords];
            uint count, status;
            int result = departures.gpgx_reset_prefix_read(words, (uint)words.Length, out count, out status);
            if (result != 0) Fail("GPGX reset-prefix read refused: " + result);
            if (count > Capacity) Fail("GPGX reset-prefix count exceeds capacity.");
            if (status > 1) Fail("GPGX reset-prefix returned an unknown status.");
            try { return Validate(words, count, status); }
            catch (InvalidOperationException) { invalid = true; throw; }
        }

        // Ends observation, including a faulted session. It does not make a second cold arm legitimate or restore
        // provenance after any host load, even a same-state load whose CPU timing boundary fields are identical.
        public void Disable()
        {
            int result = departures.gpgx_reset_prefix_disable();
            if (result != 0) Fail("GPGX reset-prefix disable refused: " + result);
            armed = false;
        }

        private void RequireValid()
        { if (invalid) throw new InvalidOperationException("GPGX reset-prefix session is invalid and cannot be rearmed."); }
        private void Fail(string message) { invalid = true; throw new InvalidOperationException(message); }

        private static GpgxResetPrefixCapture Validate(uint[] words, uint count, uint status)
        {
            if (count < 3) throw new InvalidOperationException("Reset-prefix lacks the complete constructor reset provenance.");
            var records = new GpgxResetPrefixRecord[count];
            bool instruction = false, firstVdp = false; uint pc = 0;
            for (uint i = 0; i < count; i++)
            {
                var record = new GpgxResetPrefixRecord(words, checked((int)(i * RecordWords))); records[i] = record;
                if (record.Ordinal != i) throw new InvalidOperationException("Reset-prefix ordinal gap or reordering.");
                if (record.Knownness != 1) throw new InvalidOperationException("Reset-prefix knownness is not emulator-initialized.");
                if (i < 3)
                {
                    if ((uint)record.Kind != i + 1) throw new InvalidOperationException("Reset-prefix constructor order differs.");
                    continue;
                }
                if (firstVdp && !instruction) throw new InvalidOperationException("Reset-prefix extends past the first VDP retirement.");
                if (record.Kind == GpgxResetPrefixKind.Fetch)
                {
                    if (instruction) throw new InvalidOperationException("Reset-prefix fetch overlaps an instruction.");
                    if (record.InstructionStartPc != record.Pc)
                        throw new InvalidOperationException("Reset-prefix fetch start PC differs from the current PC.");
                    instruction = true; pc = record.Pc;
                }
                else if (record.Kind == GpgxResetPrefixKind.Retire || record.Kind == GpgxResetPrefixKind.Read
                    || record.Kind == GpgxResetPrefixKind.Write)
                {
                    if (!instruction) throw new InvalidOperationException("Reset-prefix event has no fetched instruction.");
                    if (record.InstructionStartPc != pc) throw new InvalidOperationException("Reset-prefix instruction-start PC differs.");
                    if (record.Kind == GpgxResetPrefixKind.Retire) instruction = false;
                    else
                    {
                        if (record.Width != 1 && record.Width != 2 && record.Width != 4)
                            throw new InvalidOperationException("Reset-prefix bus width is unsupported.");
                        if (record.Kind == GpgxResetPrefixKind.Read && record.Address == 0xC00004 && record.Width == 2)
                        {
                            if (firstVdp) throw new InvalidOperationException("Reset-prefix repeats the first VDP status read.");
                            firstVdp = true;
                        }
                    }
                }
                else throw new InvalidOperationException("Reset-prefix event kind is unsupported.");
            }
            if (instruction) throw new InvalidOperationException("Reset-prefix contains an unretired instruction.");
            if ((status == 1) != firstVdp) throw new InvalidOperationException("Reset-prefix completion disagrees with first VDP status read.");
            return new GpgxResetPrefixCapture(records, status == 1);
        }
    }
}
