using System;
using System.Runtime.InteropServices;
using BizHawk.BizInvoke;

namespace OpenGGF.BizHawk.Headless
{
    // Stall ABI v2 (native patch 0005): an observation-only 68000 CPU ledger kept natively and read only between
    // FrameAdvance calls. It never registers a callback, takes no caller-selected address or range and writes no
    // emulated state. v1 (GpgxM68kStallNative) is independent and unchanged. Binding a core without 0005 throws.
    [StructLayout(LayoutKind.Explicit, Pack = 1, Size = 12)]
    public struct GpgxM68kStall2Slot
    {
        [FieldOffset(0)] public uint Key;        // pc[23:0] | context << 24 | 1 << 31
        [FieldOffset(4)] public uint Count;
        [FieldOffset(8)] public uint Mclk;       // instruction MCLK excluding refresh and every classified stall

        public uint Pc { get { return Key & 0xFFFFFF; } }
        public uint Context { get { return (Key >> 24) & 0x7F; } }
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1, Size = 32)]
    public struct GpgxM68kStall2Accept
    {
        [FieldOffset(0)] public uint Ordinal;
        [FieldOffset(4)] public byte Level;
        [FieldOffset(5)] public byte InsideInstruction;
        [FieldOffset(6)] public byte TableIndex;
        [FieldOffset(7)] public byte Cycles;
        [FieldOffset(8)] public uint ReturnPc;
        [FieldOffset(12)] public uint FrameOrdinal;
        [FieldOffset(16)] public ulong StampMclk;
        [FieldOffset(24)] public uint ClosesBankSequence;
        [FieldOffset(28)] public uint Reserved;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1, Size = 176)]
    public struct GpgxM68kStall2Bank
    {
        public const int Kinds = 10;
        [FieldOffset(0)] public uint Sequence;
        [FieldOffset(4)] public byte OpenedByVint;
        [FieldOffset(8)] public uint OpenAcceptOrdinal;
        [FieldOffset(12)] public uint CloseAcceptOrdinal;
        [FieldOffset(16)] public uint Entries;
        [FieldOffset(20)] public uint StraddlePc;
        [FieldOffset(24)] public uint StraddleContext;
        [FieldOffset(28)] public uint StraddleMclk;
        [FieldOffset(32)] public uint CarryPc;
        [FieldOffset(36)] public uint CarryMclk;
        [FieldOffset(40)] public uint RefreshEvents;
        [FieldOffset(44)] public uint AcceptVint;
        [FieldOffset(48)] public uint AcceptHint;
        [FieldOffset(52)] public uint AcceptOther;
        [FieldOffset(56)] public uint IrqDelayInline;
        [FieldOffset(60)] public uint SyntheticRte;
        [FieldOffset(64)] public uint AbandonedFrames;
        [FieldOffset(72)] public ulong Executed;
        [FieldOffset(80)] public ulong OpenStampMclk;
        [FieldOffset(88)] public ulong CloseStampMclk;
        [FieldOffset(96)] public ulong KindZ80Bus;
        [FieldOffset(104)] public ulong KindFifo;
        [FieldOffset(112)] public ulong KindDma;
        [FieldOffset(120)] public ulong KindDmaRewind;
        [FieldOffset(128)] public ulong KindZ80Area;
        [FieldOffset(136)] public ulong KindLockup;
        [FieldOffset(144)] public ulong KindStopped;
        [FieldOffset(152)] public ulong KindAcceptVint;
        [FieldOffset(160)] public ulong KindAcceptHint;
        [FieldOffset(168)] public ulong KindAcceptOther;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1, Size = 40)]
    public struct GpgxM68kStall2Frame
    {
        [FieldOffset(0)] public uint AbiVersion;
        [FieldOffset(4)] public uint Flags;
        [FieldOffset(8)] public uint FrameOrdinal;
        [FieldOffset(12)] public uint VintAccepts;
        [FieldOffset(16)] public uint RingPending;
        [FieldOffset(20)] public uint ClosedBanks;
        [FieldOffset(24)] public uint OpenBankSequence;
        [FieldOffset(28)] public uint Context;
        [FieldOffset(32)] public ulong StampMclk;
    }

    public abstract class GpgxM68kStall2Departures
    {
        [BizImport(CallingConvention.Cdecl)] public abstract uint gpgx_m68k_stall2_abi_version();
        [BizImport(CallingConvention.Cdecl)] public abstract int gpgx_m68k_stall2_layout([Out] uint[] layout, uint count);
        [BizImport(CallingConvention.Cdecl)] public abstract int gpgx_m68k_stall2_enable(uint abiVersion);
        [BizImport(CallingConvention.Cdecl)] public abstract int gpgx_m68k_stall2_read_frame(out GpgxM68kStall2Frame frame, uint size);
        [BizImport(CallingConvention.Cdecl)]
        public abstract int gpgx_m68k_stall2_take_accepts([Out] GpgxM68kStall2Accept[] records, uint size, uint capacity,
            out uint count);
        [BizImport(CallingConvention.Cdecl)]
        public abstract int gpgx_m68k_stall2_take_bank(out GpgxM68kStall2Bank header, uint headerSize,
            [Out] GpgxM68kStall2Slot[] slots, uint slotSize, uint capacity, out uint available);
        [BizImport(CallingConvention.Cdecl)] public abstract int gpgx_m68k_stall2_disable();
    }

    public sealed class GpgxM68kStall2Native
    {
        public const uint AbiVersion = 2;
        public const uint FlagDiscontinuityMask = 0xFF00;
        private readonly GpgxM68kStall2Departures departures;
        private readonly GpgxHost host;
        private readonly uint[] layout = new uint[8];
        private long enabledGeneration = -1;

        private GpgxM68kStall2Native(GpgxHost host, GpgxM68kStall2Departures departures)
        {
            this.host = host;
            this.departures = departures;
            if (departures.gpgx_m68k_stall2_abi_version() != AbiVersion || departures.gpgx_m68k_stall2_layout(layout, 8) != 0)
                throw new InvalidOperationException("GPGX stall ABI v2 identity differs.");
            if (layout[0] != Marshal.SizeOf(typeof(GpgxM68kStall2Slot)) || layout[1] != Marshal.SizeOf(typeof(GpgxM68kStall2Accept))
                || layout[2] != Marshal.SizeOf(typeof(GpgxM68kStall2Bank)) || layout[3] != Marshal.SizeOf(typeof(GpgxM68kStall2Frame)))
                throw new InvalidOperationException("GPGX stall ABI v2 layout differs from the managed structs.");
        }

        public uint SlotsPerBank { get { return layout[4]; } }
        public uint MaxLoad { get { return layout[5]; } }
        public uint Banks { get { return layout[6]; } }
        public uint RingCapacity { get { return layout[7]; } }

        public static GpgxM68kStall2Native Bind(GpgxHost host)
        {
            if (host == null) throw new ArgumentNullException("host");
            return new GpgxM68kStall2Native(host, host.BindDiagnosticDeparture<GpgxM68kStall2Departures>());
        }

        public void Enable()
        {
            int result = departures.gpgx_m68k_stall2_enable(AbiVersion);
            if (result != 0) throw new InvalidOperationException("GPGX stall ABI v2 enable failed: " + result);
            enabledGeneration = host.LoadGeneration;
        }

        // Between FrameAdvance calls only. Always returns the sticky flags (the take calls refuse data while set).
        public GpgxM68kStall2Frame ReadFrame()
        {
            RequireSameGeneration();
            GpgxM68kStall2Frame frame;
            int result = departures.gpgx_m68k_stall2_read_frame(out frame, (uint)Marshal.SizeOf(typeof(GpgxM68kStall2Frame)));
            if (result != 0) throw new InvalidOperationException("GPGX stall ABI v2 frame read failed: " + result);
            return frame;
        }

        public GpgxM68kStall2Accept[] TakeAccepts()
        {
            RequireSameGeneration();
            var records = new GpgxM68kStall2Accept[RingCapacity];
            uint count;
            int result = departures.gpgx_m68k_stall2_take_accepts(records, (uint)Marshal.SizeOf(typeof(GpgxM68kStall2Accept)),
                RingCapacity, out count);
            if (result != 0) throw new InvalidOperationException("GPGX stall ABI v2 acceptance read refused: " + result);
            Array.Resize(ref records, (int)count);
            return records;
        }

        // Returns false when no closed interval is waiting.
        public bool TryTakeBank(out GpgxM68kStall2Bank header, out GpgxM68kStall2Slot[] slots)
        {
            RequireSameGeneration();
            var buffer = new GpgxM68kStall2Slot[MaxLoad];
            uint available;
            int result = departures.gpgx_m68k_stall2_take_bank(out header, (uint)Marshal.SizeOf(typeof(GpgxM68kStall2Bank)),
                buffer, (uint)Marshal.SizeOf(typeof(GpgxM68kStall2Slot)), MaxLoad, out available);
            if (result != 0) throw new InvalidOperationException("GPGX stall ABI v2 interval read refused: " + result);
            if (available == 0) { slots = null; return false; }
            if (header.Entries > MaxLoad) throw new InvalidOperationException("GPGX stall ABI v2 entry count exceeds capacity.");
            Array.Resize(ref buffer, (int)header.Entries);
            slots = buffer;
            return true;
        }

        public void Disable()
        {
            int result = departures.gpgx_m68k_stall2_disable();
            if (result != 0) throw new InvalidOperationException("GPGX stall ABI v2 disable failed: " + result);
            enabledGeneration = -1;
        }

        private void RequireSameGeneration()
        {
            if (enabledGeneration < 0) throw new InvalidOperationException("GPGX stall ABI v2 is not enabled.");
            if (host.LoadGeneration != enabledGeneration)
                throw new InvalidOperationException("A savestate was loaded after stall ABI v2 was enabled; the span is invalid.");
        }
    }
}
