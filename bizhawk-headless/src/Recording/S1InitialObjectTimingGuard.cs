using System;
using System.Collections.Generic;

namespace OpenGGF.BizHawk.Headless
{
    // Qualified native initial callback states, including same-pass children.
    // VBlank_Lag in pre-level mode88 changes no gameplay RNG/level counter
    // (sonic.asm:681-754). The reached callbacks below have no VBlank read;
    // the Basaran exclusion is separately checked rather than assumed.
    internal static class S1InitialObjectTimingGuard
    {
        private static readonly HashSet<int> Qualified = new HashSet<int> {
            0x0100, 0x0A00, 0x0C00, 0x1200, 0x1600, 0x1800, 0x1B00, 0x2200,
            0x2500, 0x2502, 0x2600, 0x2C00, 0x2D00, 0x2F00, 0x3000, 0x3008,
            0x3200, 0x3300, 0x3600, 0x4000, 0x4100, 0x4200, 0x4600, 0x4700,
            0x4C00, 0x5000, 0x5400, 0x5500, 0x5700, 0x5704, 0x5A00, 0x5D00,
            0x5F00, 0x6000, 0x6006, 0x6100, 0x6400, 0x6600, 0x6604, 0x6700,
            0x6800, 0x6A00, 0x6B00, 0x6C00, 0x6D00, 0x6E00, 0x7100, 0x7200,
            0x7800, 0x7900,
        };

        public static void Validate(int id, int routine, int secondary, int playerX, int objectX)
        {
            if (!Qualified.Contains((id << 8) | routine))
                throw new InvalidOperationException("unproven S1 initial callback state");
            // Bas_Main -> Bas_Action_ChkSonic -> Bas_CheckDistanceAndFaceSonic
            // (55 Badnik - Basaran.asm:18-70,185-202). The range exit precedes
            // its clock read. Burrobot2D/0 initializes secondary6, which never
            // takes Move_NextAction's clock read; ring25/0,2 is not lost-ring37.
            if (id == 0x55) {
                int distance = (short)(playerX - objectX);
                if (distance < 0) distance = (-distance) & 0xFFFF;
                if (secondary != 0 || distance < 0x80)
                    throw new InvalidOperationException("unproven Basaran initial VBlank read");
            }
        }
    }
}
