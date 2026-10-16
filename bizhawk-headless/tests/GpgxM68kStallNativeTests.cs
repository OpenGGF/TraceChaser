using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenGGF.BizHawk.Headless;

namespace OpenGGF.BizHawk.Headless.Tests
{
    // Stall ABI v1 (native patch 0004). The layout test always runs. The live test needs an explicit expectation about
    // the installed core, because the same runner is used against stock, 0001-0003 and 0004 installations:
    //   OPENGGF_GPGX_STALL_EXPECT=present  the core under BIZHAWK_HOME carries 0004
    //   OPENGGF_GPGX_STALL_EXPECT=absent   it does not, and binding must throw instead of reading zeros
    internal static class GpgxM68kStallNativeTests
    {
        public static void Register(ICollection<TestMain.TestCase> tests)
        {
            tests.Add(new TestMain.TestCase("GpgxM68kStallNativeTests pin stall ABI v1 layout", PinsLayout));
            string expect = Environment.GetEnvironmentVariable("OPENGGF_GPGX_STALL_EXPECT");
            if (expect == "present")
                tests.Add(new TestMain.TestCase("GpgxM68kStallNativeTests live counters fail closed and stay monotonic",
                    LiveCountersPresent, game: "s3k", serial: true, estimatedSeconds: 10.0));
            else if (expect == "absent")
                tests.Add(new TestMain.TestCase("GpgxM68kStallNativeTests binding without 0004 throws",
                    BindingAbsentThrows, game: "s3k", serial: true, estimatedSeconds: 5.0));
        }

        private static void PinsLayout()
        {
            AssertEx.Equal(80, Marshal.SizeOf(typeof(GpgxM68kStallSnapshotV1)));
            AssertEx.Equal(16, (int)Marshal.OffsetOf(typeof(GpgxM68kStallSnapshotV1), "FrameBaseMclk"));
            AssertEx.Equal(32, (int)Marshal.OffsetOf(typeof(GpgxM68kStallSnapshotV1), "Z80BusMclk"));
            AssertEx.Equal(56, (int)Marshal.OffsetOf(typeof(GpgxM68kStallSnapshotV1), "RefreshEvents"));
            AssertEx.Equal(72, (int)Marshal.OffsetOf(typeof(GpgxM68kStallSnapshotV1), "DmaFreezeMclk"));
            AssertEx.Equal(1u, GpgxM68kStallNative.AbiVersion);
            AssertEx.Equal(80u, GpgxM68kStallNative.SnapshotSize);
        }

        private static GpgxHost OpenS3k()
        {
            string rom = Environment.GetEnvironmentVariable("S3K_ROM_PATH");
            if (string.IsNullOrEmpty(rom)) throw new InvalidOperationException("S3K_ROM_PATH is required.");
            return GpgxHost.Open(rom, GpgxHost.CreateGhz1SyncSettings());
        }

        private static void LiveCountersPresent()
        {
            using (GpgxHost host = OpenS3k())
            {
                GpgxM68kStallNative stall = GpgxM68kStallNative.Bind(host);
                GpgxM68kStallSnapshotV1 s;
                AssertEx.Equal(-1, stall.TryReadRaw(out s, GpgxM68kStallNative.SnapshotSize));
                AssertEx.Equal(-2, stall.TryReadRaw(out s, GpgxM68kStallNative.SnapshotSize - 8));
                AssertEx.Equal(-1, stall.RejectAbi(2));
                AssertEx.Throws<InvalidOperationException>(() => stall.Read(), "unavailable: -1");
                stall.Enable();
                GpgxM68kStallSnapshotV1 first = stall.Read();
                AssertEx.Equal(0u, first.FrameOrdinal);
                AssertEx.Equal(0ul, first.RefreshEvents);
                ulong previous = first.FrameBaseMclk + first.M68kCycles;
                for (int frame = 1; frame <= 120; frame++)
                {
                    host.Advance();
                    GpgxM68kStallSnapshotV1 next = stall.Read();
                    ulong stamp = next.FrameBaseMclk + next.M68kCycles;
                    if (stamp <= previous) throw new InvalidOperationException("stall stamp did not advance at frame " + frame);
                    AssertEx.Equal((uint)frame, next.FrameOrdinal);
                    previous = stamp;
                    s = next;
                }
                // Refresh delays occur every frame the 68000 runs (one per 128 cycles at an instruction fetch).
                if (s.RefreshEvents == 0) throw new InvalidOperationException("no refresh events over 120 frames");
                stall.Disable();
                AssertEx.Equal(-1, stall.TryReadRaw(out s, GpgxM68kStallNative.SnapshotSize));
                stall.Enable();
                GpgxM68kStallSnapshotV1 reset = stall.Read();
                AssertEx.Equal(0u, reset.FrameOrdinal);
                AssertEx.Equal(0ul, reset.FrameBaseMclk);
                AssertEx.Equal(0ul, reset.RefreshEvents);
                AssertEx.Equal(0ul, reset.Z80BusAccessCount);
                stall.Disable();
            }
        }

        private static void BindingAbsentThrows()
        {
            using (GpgxHost host = OpenS3k())
            {
                try
                {
                    GpgxM68kStallNative.Bind(host);
                }
                catch (Exception)
                {
                    return;
                }
                throw new InvalidOperationException("A core without 0004 bound the stall ABI.");
            }
        }
    }
}
