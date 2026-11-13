using System;
using System.Globalization;
using System.IO;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>
    /// Observes actual V-INT entry for the Sonic CD (USA) stage recorder and
    /// writes the per-trace <c>vint_service_timing.jsonl</c> companion (lane
    /// VINT, 2026-11-13; owner approval of the lost-V-INT timing extension the
    /// same day). One event per recorded row on which the frame's VBlank
    /// produced no interrupt entry:
    /// <c>{"event":"vint_not_serviced","raw_frame":N}</c>, N the trace row.
    ///
    /// <para>Observation is one fixed, opcode-checked execute hook at the first
    /// instruction of <c>VInterrupt</c> (src/Level/Main.asm:681-682,
    /// <c>bset #0,GAIRQ2</c>), resolved from the loaded stage program itself:
    /// the MMD header's <c>mmdVInt</c> (src/_Include/MMD.inc, header offset
    /// $10) names <c>JmpTo_VInt</c> (src/Level/Initialization.asm:11-13, 40-41,
    /// <c>jmp VInterrupt</c>), and the IPX loader installs that address as the
    /// main CPU's V-INT vector <c>_LEVEL6+2</c> (src/CD Initial
    /// Program/IP.asm:84-87; <c>_LEVEL6</c> = $FFFD06, src/_Include/Main
    /// CPU.inc:146). The vector is checked at attach and on every row, so an
    /// entry that bypasses this handler cannot be miscounted.</para>
    ///
    /// <para>The event is never derived from <c>levelVIntCounter</c>,
    /// <c>levelFrames</c>, <c>vintRoutine</c> or the emulator lag flag: it is
    /// the count of observed entries during the row's one hardware frame.
    /// One entry is the ordinary row; zero is the event; two or more (a
    /// pending interrupt delivered in the next frame beside that frame's own)
    /// is outside the admitted outcome and fails the capture. The stream
    /// carries no counter, handler, decoder or gameplay value.</para>
    /// </summary>
    public sealed class ScdVIntServiceObserver : IDisposable
    {
        public const string FileName = "vint_service_timing.jsonl";
        public const string EventName = "vint_not_serviced";

        /// <summary>Main RAM offset of <c>_LEVEL6+2</c>, the V-INT jump target long.</summary>
        public const int Level6TargetRam = 0xFD08;
        /// <summary><c>mmdVInt</c> in the MMD header (mmdFlags.b, pad.b, mmdOrigin.l, mmdSize.w, mmdEntry.l, mmdHInt.l).</summary>
        public const int MmdVIntOffset = 0x10;
        public const uint WordRam2MBase = 0x200000;
        public const int WordRam2MSize = 0x40000;
        /// <summary><c>jmp (xxx).l</c>.</summary>
        public const int JmpAbsLongOpcode = 0x4EF9;
        /// <summary><c>bset #0,GAIRQ2</c> = <c>bset #0,($A12000).l</c>.</summary>
        public static readonly byte[] VInterruptPrologue = { 0x08, 0xF9, 0x00, 0x00, 0x00, 0xA1, 0x20, 0x00 };

        private readonly IGpgxHost host;
        private readonly uint jumpTo;
        private readonly IDisposable hook;
        private int entries;
        private bool disposed;

        private ScdVIntServiceObserver(IGpgxHost host, uint jumpTo, uint vInterrupt)
        {
            this.host = host;
            this.jumpTo = jumpTo;
            VInterruptAddress = vInterrupt;
            hook = host.RegisterExecuteCallback(vInterrupt, () => { entries++; });
        }

        /// <summary>The resolved <c>VInterrupt</c> address the hook observes.</summary>
        public uint VInterruptAddress { get; private set; }

        /// <summary>
        /// Resolves <c>VInterrupt</c> from the loaded stage program, verifies the
        /// installed vector and both instructions, and registers the hook. Entries
        /// are counted from the next frame on.
        /// </summary>
        public static ScdVIntServiceObserver Attach(IGpgxHost host, IScdWordRamReader wordRam)
        {
            if (host == null) throw new ArgumentNullException("host");
            if (wordRam == null) throw new ArgumentNullException("wordRam");
            uint jumpTo = WordRamLong(wordRam, WordRam2MBase + MmdVIntOffset);
            uint vector = S1Ram.U32(host, Level6TargetRam);
            if (vector != jumpTo)
                throw new InvalidDataException("The V-INT vector _LEVEL6+2 is 0x" + vector.ToString("X6")
                    + ", not the stage program's mmdVInt 0x" + jumpTo.ToString("X6") + ".");
            if (WordRamWord(wordRam, jumpTo) != JmpAbsLongOpcode)
                throw new InvalidDataException("mmdVInt 0x" + jumpTo.ToString("X6") + " is not jmp (xxx).l.");
            uint vInterrupt = WordRamLong(wordRam, jumpTo + 2);
            for (int i = 0; i < VInterruptPrologue.Length; i++)
            {
                if (WordRamByte(wordRam, vInterrupt + (uint)i) != VInterruptPrologue[i])
                    throw new InvalidDataException("0x" + vInterrupt.ToString("X6")
                        + " does not begin with VInterrupt's bset #0,GAIRQ2.");
            }
            return new ScdVIntServiceObserver(host, jumpTo, vInterrupt);
        }

        /// <summary>
        /// Closes the row whose frame just completed: returns its event line when the
        /// frame's VBlank produced no entry, null for one entry, and throws for more.
        /// </summary>
        public string CloseRow(int row)
        {
            if (disposed) throw new ObjectDisposedException("ScdVIntServiceObserver");
            uint vector = S1Ram.U32(host, Level6TargetRam);
            if (vector != jumpTo)
                throw new InvalidDataException("The V-INT vector changed to 0x" + vector.ToString("X6")
                    + " during trace row " + row + ".");
            int observed = entries;
            entries = 0;
            if (observed == 1) return null;
            if (observed == 0) return FormatEvent(row);
            throw new InvalidDataException("Trace row " + row + " observed " + observed
                + " V-INT entries in one frame; only one or none is an admitted outcome.");
        }

        /// <summary>Discards entries of a frame that produced no row (the stop frame).</summary>
        public void DiscardFrame()
        {
            entries = 0;
        }

        public static string FormatEvent(int row)
        {
            if (row < 0) throw new ArgumentOutOfRangeException("row");
            return "{\"event\":\"" + EventName + "\",\"raw_frame\":"
                + row.ToString(CultureInfo.InvariantCulture) + "}";
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            hook.Dispose();
        }

        private static int WordRamByte(IScdWordRamReader wordRam, uint address)
        {
            if (address < WordRam2MBase || address >= WordRam2MBase + WordRam2MSize)
                throw new InvalidDataException("0x" + address.ToString("X6") + " is outside 2M Word RAM.");
            return wordRam.ReadWordRam2MByte((int)(address - WordRam2MBase));
        }

        private static int WordRamWord(IScdWordRamReader wordRam, uint address)
        {
            return (WordRamByte(wordRam, address) << 8) | WordRamByte(wordRam, address + 1);
        }

        private static uint WordRamLong(IScdWordRamReader wordRam, uint address)
        {
            return ((uint)WordRamWord(wordRam, address) << 16) | (uint)WordRamWord(wordRam, address + 2);
        }
    }
}
