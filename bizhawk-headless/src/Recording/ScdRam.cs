namespace OpenGGF.BizHawk.Headless
{
    /// <summary>
    /// Sonic CD (USA) main-CPU work-RAM map for the stage recorder. Offsets
    /// are 68K RAM domain offsets ($FF0000 stripped). Each label is cited
    /// from the scddisasm pin 1ecc5f3af7ccfbe26c6c58c96e6e5bcfdd15d6ff:
    /// D = src/Level/_Definitions.inc, V = src/_Include/Main CPU
    /// Variables.inc, M = src/Level/Main.asm. Addresses were computed from
    /// the rs allocations and agree with the source-derived layout used by
    /// the independent player-reference probe (2026-10-24). The stage
    /// engine keeps Sonic 1's object layout, so per-object offsets are
    /// shared with <see cref="S1Ram"/> where the field is the same.
    /// Reads go through <see cref="S1Ram"/>'s typed big-endian helpers.
    /// </summary>
    public static class ScdRam
    {
        // Engine globals (D).
        public const int GameMode = 0xF600;          // u8 gameMode (D:178); bit 7 = level initializing (M:56, M:328)
        public const int VIntRoutine = 0xF62A;       // u8 vintRoutine (D:206); 0 at V-INT = lag (M:685)
        public const int RngSeed = 0xF636;           // u32 rngSeed (D:210)
        public const int Paused = 0xF63A;            // u16 paused (D:211)
        public const int CameraX = 0xF700;           // pixel word of u32 cameraX (D:237)
        public const int CameraY = 0xF704;           // pixel word of u32 cameraY (D:238)
        public const int BottomBound = 0xF72E;       // u16 bottomBound (D:252)
        public const int ObjPrevChunk = 0xF76E;      // u16 objPrevChunk (D:294), S1 v_opl_screen
        public const int ObjChunkRight = 0xF770;     // u32 objChunkRight (D:295), S1 v_opl_data
        public const int ObjChunkLeft = 0xF774;      // u32 objChunkLeft (D:296), S1 v_opl_data+4
        public const int CtrlLocked = 0xF7CC;        // u8 ctrlLocked (D:327); title card lock

        // Main CPU global variables (V).
        public const int SavedObjFlags = 0x1200;     // savedObjFlags (V:48); bytes 0/1 = spawn cursors
        public const int LevelFrames = 0x1504;       // u16 levelFrames (V:52); +1 per unpaused main loop (M:400)
        public const int Zone = 0x1506;              // u8 zone (V:54)
        public const int Act = 0x1507;               // u8 act (V:55)
        public const int Rings = 0x1512;             // u16 rings (V:65)
        public const int TimeWarp = 0x1521;          // u8 timeWarp (V:76)
        public const int TimeZone = 0x152E;          // u8 timeZone (V:83); 0 past, 1 present, 2 future; bit 7 = warp
        public const int GoodFuture = 0x156A;        // u8 goodFuture (V:119)
        public const int DemoMode = 0x1580;          // u16 demoMode (V:134)
        public const int DebugMode = 0x1906;         // u16 debugMode (V:153)
        public const int LevelVIntCounter = 0x190A;  // u32 levelVIntCounter (V:155); +1 every V-INT incl. lag (M:712)
        public const int LevelVIntCounterLow = 0x190C;
        public const int LevelStarted = 0x1957;      // u8 levelStarted (V:184); 0 at LevelStart (M:29), 1 before Level_MainLoop (M:329)

        // Object pool (D:129-169): 32 reserved + $60 dynamic slots of oSize $40.
        public const int PlayerBase = 0xD000;        // objPlayerSlot (D:131)
        public const int FirstDynamicSlot = 32;      // dynObjects (D:165) = $FFD800
        public const int TotalObjectSlots = 128;     // OBJCOUNT (D:169)
        public const int ObjectSlotSize = 0x40;      // oSize (D:24)
        public const byte PlayerObjectId = 1;        // ObjectIndex entry 1 = ObjSonic (player-reference probe)

        // Player-object fields (D:33-93) that differ in name from S1.
        public const int OffFlags = 0x22;            // oFlags (D:57) = S1 obStatus
        public const int OffPlayerStandObj = 0x3D;   // oPlayerStandObj (D:93)
        public const int OffPlayerMoveLock = 0x3E;   // oPlayerMoveLock (D:77) = S1 obCtrlLock

        // 2M Word RAM: the stage program's MMD header lands at $200000.
        public const int MmdHeaderBytes = 0x100;     // MMDHEADSZ (src/_Include/MMD.inc)

        public static int SlotAddress(int slot)
        {
            return PlayerBase + (slot * ObjectSlotSize);
        }
    }
}
