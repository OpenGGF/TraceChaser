using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using OpenGGF.BizHawk.Headless;

namespace OpenGGF.BizHawk.Headless.Tests
{
    // Stall ABI v2 (native patch 0005) and the callback-free ledger runner. Layout, interval joining and argument
    // tests always run. The live test needs OPENGGF_GPGX_STALL2_EXPECT=present|absent plus S3K_ROM_PATH, because the
    // same runner is used against stock, 0001-0004 and 0001-0005 installations.
    internal static class GpgxM68kStall2NativeTests
    {
        public static void Register(ICollection<TestMain.TestCase> tests)
        {
            tests.Add(new TestMain.TestCase("GpgxM68kStall2NativeTests pin stall ABI v2 layouts", PinsLayouts));
            tests.Add(new TestMain.TestCase("GpgxM68kStall2NativeTests join only complete consecutive intervals", JoinsIntervals));
            tests.Add(new TestMain.TestCase("GpgxM68kStall2NativeTests ledger runner arguments are explicit and unique", ParsesArguments));
            string expect = Environment.GetEnvironmentVariable("OPENGGF_GPGX_STALL2_EXPECT");
            if (expect == "present")
                tests.Add(new TestMain.TestCase("GpgxM68kStall2NativeTests live intervals join and a loadstate invalidates the span",
                    LivePresent, game: "s3k", serial: true, estimatedSeconds: 10.0));
            else if (expect == "absent")
                tests.Add(new TestMain.TestCase("GpgxM68kStall2NativeTests binding without 0005 throws",
                    BindingAbsentThrows, game: "s3k", serial: true, estimatedSeconds: 5.0));
        }

        private static void PinsLayouts()
        {
            AssertEx.Equal(12, Marshal.SizeOf(typeof(GpgxM68kStall2Slot)));
            AssertEx.Equal(32, Marshal.SizeOf(typeof(GpgxM68kStall2Accept)));
            AssertEx.Equal(176, Marshal.SizeOf(typeof(GpgxM68kStall2Bank)));
            AssertEx.Equal(40, Marshal.SizeOf(typeof(GpgxM68kStall2Frame)));
            AssertEx.Equal(16, (int)Marshal.OffsetOf(typeof(GpgxM68kStall2Accept), "StampMclk"));
            AssertEx.Equal(72, (int)Marshal.OffsetOf(typeof(GpgxM68kStall2Bank), "Executed"));
            AssertEx.Equal(96, (int)Marshal.OffsetOf(typeof(GpgxM68kStall2Bank), "KindZ80Bus"));
            AssertEx.Equal(168, (int)Marshal.OffsetOf(typeof(GpgxM68kStall2Bank), "KindAcceptOther"));
            AssertEx.Equal(32, (int)Marshal.OffsetOf(typeof(GpgxM68kStall2Frame), "StampMclk"));
        }

        private static GpgxM68kStall2Bank Bank(uint sequence, uint openOrdinal, uint closeOrdinal, ulong open, ulong close)
        {
            return new GpgxM68kStall2Bank
            {
                Sequence = sequence, OpenedByVint = (byte)(sequence == 0 ? 0 : 1),
                OpenAcceptOrdinal = sequence == 0 ? 0xFFFFFFFF : openOrdinal, CloseAcceptOrdinal = closeOrdinal,
                Entries = 3, Executed = 10, OpenStampMclk = open, CloseStampMclk = close,
                StraddlePc = 0xFFFFFFFF, CarryPc = 0xFFFFFFFF
            };
        }

        private static void JoinsIntervals()
        {
            GpgxM68kStall2Bank b0 = Bank(0, 0, 0, 100, 900), b1 = Bank(1, 0, 1, 900, 1800), b2 = Bank(2, 1, 3, 1800, 2700);
            M68kLedgerWindowRunner.RequireJoined(b0, null, 0);
            M68kLedgerWindowRunner.RequireJoined(b1, b0, 1);
            M68kLedgerWindowRunner.RequireJoined(b2, b1, 2);
            Expect(() => M68kLedgerWindowRunner.RequireJoined(b1, null, 1));              // first must be the partial one
            Expect(() => M68kLedgerWindowRunner.RequireJoined(b2, b0, 1));              // gap in sequence
            GpgxM68kStall2Bank empty = b1; empty.Entries = 0; empty.Executed = 0;
            Expect(() => M68kLedgerWindowRunner.RequireJoined(empty, b0, 1));
            GpgxM68kStall2Bank stalled = b1; stalled.CloseStampMclk = stalled.OpenStampMclk;
            Expect(() => M68kLedgerWindowRunner.RequireJoined(stalled, b0, 1));
            GpgxM68kStall2Bank wrongOrdinal = b2; wrongOrdinal.OpenAcceptOrdinal = 2;
            Expect(() => M68kLedgerWindowRunner.RequireJoined(wrongOrdinal, b1, 2));
            GpgxM68kStall2Bank wrongStamp = b2; wrongStamp.OpenStampMclk = 1799;
            Expect(() => M68kLedgerWindowRunner.RequireJoined(wrongStamp, b1, 2));
            GpgxM68kStall2Bank straddled = b1; straddled.StraddlePc = 0x1C42;
            GpgxM68kStall2Bank carried = b2; carried.CarryPc = 0x1C42;
            M68kLedgerWindowRunner.RequireJoined(carried, straddled, 2);
            Expect(() => M68kLedgerWindowRunner.RequireJoined(b2, straddled, 2));        // straddle without carry
            GpgxM68kStall2Bank wrongCarry = carried; wrongCarry.CarryPc = 0x1C44;
            Expect(() => M68kLedgerWindowRunner.RequireJoined(wrongCarry, straddled, 2));
        }

        private static void ParsesArguments()
        {
            string[] good = { M68kLedgerWindowRunner.Flag, "--rom", "/r.gen", "--movie", "/m.bk2", "--ingress-state", "/s.bin",
                "--ingress-provenance", "/s.json", "--output", "/o", "--frames", "40", "--ledger", "off",
                "--save-state-when-label-exceeds", "4420", "--audio-label-window", "4396-4420" };
            M68kLedgerWindowRunner.Options o = M68kLedgerWindowRunner.Parse(good);
            AssertEx.Equal(40, o.Frames);
            AssertEx.Equal(false, o.Ledger);
            AssertEx.Equal(4420, o.SaveStateWhenLabelExceeds);
            AssertEx.Equal(4396, o.AudioLabelLo);
            Expect(() => M68kLedgerWindowRunner.Parse(new[] { M68kLedgerWindowRunner.Flag, "--rom", "/r.gen", "--rom", "/r.gen",
                "--movie", "/m.bk2", "--ingress-state", "/s.bin", "--ingress-provenance", "/s.json", "--output", "/o", "--frames", "4" }));
            Expect(() => M68kLedgerWindowRunner.Parse(new[] { M68kLedgerWindowRunner.Flag, "--rom", "/r.gen", "--movie", "/m.bk2",
                "--ingress-state", "/s.bin", "--ingress-provenance", "/s.json", "--output", "/o", "--frames", "4", "--pc", "1C1A" }));
            Expect(() => M68kLedgerWindowRunner.Parse(new[] { M68kLedgerWindowRunner.Flag, "--rom", "/r.gen", "--movie", "/m.bk2",
                "--ingress-state", "s.bin", "--ingress-provenance", "/s.json", "--output", "/o", "--frames", "4" }));
            Expect(() => M68kLedgerWindowRunner.Parse(new[] { M68kLedgerWindowRunner.Flag, "--rom", "/r.gen", "--movie", "/m.bk2",
                "--ingress-state", "/s.bin", "--ingress-provenance", "/s.json", "--output", "/o", "--frames", "4", "--ledger", "maybe" }));
        }

        private static GpgxHost OpenS3k()
        {
            string rom = Environment.GetEnvironmentVariable("S3K_ROM_PATH");
            if (string.IsNullOrEmpty(rom)) throw new InvalidOperationException("S3K_ROM_PATH is required.");
            return GpgxHost.Open(rom, GpgxHost.CreateGhz1SyncSettings());
        }

        private static void LivePresent()
        {
            using (GpgxHost host = OpenS3k())
            {
                GpgxM68kStall2Native ledger = GpgxM68kStall2Native.Bind(host);
                Expect(() => ledger.ReadFrame());                                          // not enabled
                byte[] state = host.CloneSavestate();
                ledger.Enable();
                GpgxM68kStall2Bank? previous = null;
                uint sequence = 0, complete = 0;
                for (int f = 0; f < 240; f++)
                {
                    host.Advance();
                    GpgxM68kStall2Frame frame = ledger.ReadFrame();
                    AssertEx.Equal(0u, frame.Flags);
                    ledger.TakeAccepts();
                    GpgxM68kStall2Bank bank; GpgxM68kStall2Slot[] slots;
                    while (ledger.TryTakeBank(out bank, out slots))
                    {
                        M68kLedgerWindowRunner.RequireJoined(bank, previous, sequence++);
                        previous = bank;
                        if (bank.OpenedByVint == 1) complete++;
                        ulong sum = bank.CarryMclk + 14UL * bank.RefreshEvents + bank.KindZ80Bus + bank.KindFifo + bank.KindDma
                            + bank.KindZ80Area + bank.KindLockup + bank.KindStopped + bank.KindAcceptVint + bank.KindAcceptHint
                            + bank.KindAcceptOther - bank.KindDmaRewind;
                        foreach (GpgxM68kStall2Slot s in slots) sum += s.Mclk;
                        // every MCLK of a complete interval is attributed (no unclassified time between instructions)
                        if (bank.OpenedByVint == 1) AssertEx.Equal(bank.CloseStampMclk - bank.OpenStampMclk, sum);
                    }
                }
                if (complete < 100) throw new InvalidOperationException("only " + complete + " complete intervals in 240 frames");
                host.LoadSavestate(state);
                Expect(() => ledger.ReadFrame());                                          // span crossed a loadstate
                ledger.Disable();
            }
        }

        private static void BindingAbsentThrows()
        {
            using (GpgxHost host = OpenS3k())
            {
                try { GpgxM68kStall2Native.Bind(host); }
                catch (Exception) { return; }
                throw new InvalidOperationException("A core without 0005 bound stall ABI v2.");
            }
        }

        private static void Expect(Action action)
        {
            try { action(); }
            catch (Exception) { return; }
            throw new InvalidOperationException("Expected an exception.");
        }
    }
}
