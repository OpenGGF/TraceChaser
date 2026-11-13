using System;
using System.Globalization;
using System.Text;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>
    /// Sonic CD stage physics row: the ordinary-level 42-column v5 shape with
    /// the S1 recorder's header and formatting. Only the global sources
    /// differ from S1: rings <c>rings</c>, gameplay_frame_counter
    /// <c>levelFrames</c>, vblank_counter the low word of
    /// <c>levelVIntCounter</c>. lag_counter is the S1 placeholder 0000
    /// (the CD stage program keeps no lag counter; per-frame lag lives in
    /// the lag_state aux event). There is no sidekick.
    /// </summary>
    public static class ScdTraceCsvWriter
    {
        public const string Header = S1TraceCsvWriter.Header;

        public static string FormatRow(int traceFrame, int inputMask, IGpgxHost host)
        {
            if (host == null) throw new ArgumentNullException("host");
            int p = ScdRam.PlayerBase;
            byte angle = S1Ram.U8(host, p + S1Ram.OffAngle);
            byte status = S1Ram.U8(host, p + ScdRam.OffFlags);
            bool air = (status & S1Ram.StatusInAir) != 0;
            bool rolling = (status & S1Ram.StatusRolling) != 0;

            var row = new StringBuilder(192);
            row.Append(Hex4(traceFrame));
            row.Append(',').Append(Hex4(inputMask));
            row.Append(',').Append(Hex4(S1Ram.U16(host, ScdRam.CameraX)));
            row.Append(',').Append(Hex4(S1Ram.U16(host, ScdRam.CameraY)));
            row.Append(',').Append(Hex4(S1Ram.U16(host, ScdRam.Rings)));
            row.Append(',').Append(Hex4(S1Ram.U16(host, ScdRam.LevelFrames)));
            row.Append(',').Append(Hex4(S1Ram.U16(host, ScdRam.LevelVIntCounterLow)));
            row.Append(',').Append(Hex4(0));
            row.Append(',').Append('1');
            row.Append(',').Append(Hex4(S1Ram.U16(host, p + S1Ram.OffXPos)));
            row.Append(',').Append(Hex4(S1Ram.U16(host, p + S1Ram.OffYPos)));
            row.Append(',').Append(Hex4(S1Ram.U16(host, p + S1Ram.OffXVel)));
            row.Append(',').Append(Hex4(S1Ram.U16(host, p + S1Ram.OffYVel)));
            row.Append(',').Append(Hex4(S1Ram.U16(host, p + S1Ram.OffInertia)));
            row.Append(',').Append(Hex2(angle));
            row.Append(',').Append(air ? '1' : '0');
            row.Append(',').Append(rolling ? '1' : '0');
            row.Append(',').Append(S1TraceCsvWriter.DeriveGroundMode(air, angle)
                .ToString(CultureInfo.InvariantCulture));
            row.Append(',').Append(Hex4(S1Ram.U16(host, p + S1Ram.OffXSub)));
            row.Append(',').Append(Hex4(S1Ram.U16(host, p + S1Ram.OffYSub)));
            row.Append(',').Append(Hex2(S1Ram.U8(host, p + S1Ram.OffRoutine)));
            row.Append(',').Append(Hex2(status));
            row.Append(',').Append(Hex2(S1Ram.U8(host, p + ScdRam.OffPlayerStandObj)));
            row.Append(',').Append(Hex2(S1Ram.U8(host, p + S1Ram.OffAnimId)));
            row.Append(',').Append(Hex2(S1Ram.U8(host, p + S1Ram.OffMappingFrame)));
            row.Append(',').Append(S1TraceCsvWriter.SidekickBlock);
            return row.ToString();
        }

        private static string Hex4(int value)
        {
            return value.ToString("X4", CultureInfo.InvariantCulture);
        }

        private static string Hex2(int value)
        {
            return value.ToString("X2", CultureInfo.InvariantCulture);
        }
    }
}
