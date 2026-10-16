using System;
using System.Runtime.InteropServices;
using BizHawk.BizInvoke;

namespace OpenGGF.BizHawk.Headless
{
    // Observation-only 68000 stall counters from native patch 0004-m68k-stall-counters.patch (stall ABI v1, 80 bytes),
    // independent of the audio-trace, S3K parity and S3K PCM ABIs. Diagnostic only: no capture, profile or fixture
    // consumes it, and nothing it reports may feed emulation or a replay. Binding a core without 0004 throws, so a stock
    // or 0001-0003 core can never be read as zero counters. Counters are not savestated: a loadstate inside a measured
    // span invalidates that span.
    [StructLayout(LayoutKind.Explicit, Pack = 1, Size = 80)]
    public struct GpgxM68kStallSnapshotV1
    {
        [FieldOffset(0)] public uint AbiVersion;
        [FieldOffset(4)] public uint StructSize;
        [FieldOffset(8)] public uint FrameOrdinal;       // frame-end timing rebases since enable
        [FieldOffset(12)] public uint M68kCycles;        // MCLK from the current frame start (GPGX m68k.cycles)
        [FieldOffset(16)] public ulong FrameBaseMclk;    // FrameBaseMclk + M68kCycles is monotonic
        [FieldOffset(24)] public ulong Z80BusAccessCount;
        [FieldOffset(32)] public ulong Z80BusMclk;
        [FieldOffset(40)] public ulong FifoWaitCount;
        [FieldOffset(48)] public ulong FifoWaitMclk;
        [FieldOffset(56)] public ulong RefreshEvents;
        [FieldOffset(64)] public ulong DmaFreezeCount;
        [FieldOffset(72)] public ulong DmaFreezeMclk;
    }

    public abstract class GpgxM68kStallDepartures
    {
        [BizImport(CallingConvention.Cdecl)] public abstract uint gpgx_m68k_stall_abi_version();
        [BizImport(CallingConvention.Cdecl)] public abstract uint gpgx_m68k_stall_snapshot_size();
        [BizImport(CallingConvention.Cdecl)] public abstract int gpgx_m68k_stall_enable(uint abiVersion);
        [BizImport(CallingConvention.Cdecl)] public abstract int gpgx_m68k_stall_read(out GpgxM68kStallSnapshotV1 snapshot, uint size);
        [BizImport(CallingConvention.Cdecl)] public abstract int gpgx_m68k_stall_disable();
    }

    public sealed class GpgxM68kStallNative
    {
        public const uint AbiVersion = 1;
        public const uint SnapshotSize = 80;
        private readonly GpgxM68kStallDepartures departures;

        private GpgxM68kStallNative(GpgxM68kStallDepartures departures)
        {
            this.departures = departures;
            if (departures.gpgx_m68k_stall_abi_version() != AbiVersion
                || departures.gpgx_m68k_stall_snapshot_size() != SnapshotSize
                || Marshal.SizeOf(typeof(GpgxM68kStallSnapshotV1)) != SnapshotSize)
                throw new InvalidOperationException("GPGX 68000 stall counter API identity differs from v1/80.");
        }

        // Throws when the loaded core does not export the stall ABI.
        public static GpgxM68kStallNative Bind(GpgxHost host)
        {
            if (host == null) throw new ArgumentNullException("host");
            return new GpgxM68kStallNative(host.BindDiagnosticDeparture<GpgxM68kStallDepartures>());
        }

        public void Enable()
        {
            int result = departures.gpgx_m68k_stall_enable(AbiVersion);
            if (result != 0)
                throw new InvalidOperationException("GPGX 68000 stall counters rejected ABI v1: " + result);
        }

        // Read inside a managed execute callback (an instruction boundary) or between frames.
        public GpgxM68kStallSnapshotV1 Read()
        {
            GpgxM68kStallSnapshotV1 snapshot;
            int result = departures.gpgx_m68k_stall_read(out snapshot, SnapshotSize);
            if (result != 0)
                throw new InvalidOperationException("GPGX 68000 stall counters are unavailable: " + result);
            if (snapshot.AbiVersion != AbiVersion || snapshot.StructSize != SnapshotSize)
                throw new InvalidOperationException("GPGX 68000 stall snapshot identity differs from v1/80.");
            return snapshot;
        }

        // Exposes the raw result so callers can prove a read before enable fails instead of returning zeros.
        public int TryReadRaw(out GpgxM68kStallSnapshotV1 snapshot, uint size)
        {
            return departures.gpgx_m68k_stall_read(out snapshot, size);
        }

        public int RejectAbi(uint abiVersion)
        {
            if (abiVersion == AbiVersion) throw new ArgumentException("Only an unsupported ABI may be probed.", "abiVersion");
            return departures.gpgx_m68k_stall_enable(abiVersion);
        }

        public void Disable()
        {
            int result = departures.gpgx_m68k_stall_disable();
            if (result != 0)
                throw new InvalidOperationException("GPGX 68000 stall counters did not disable: " + result);
        }
    }
}
