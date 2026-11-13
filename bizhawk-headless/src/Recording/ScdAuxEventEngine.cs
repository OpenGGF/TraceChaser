using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>
    /// Sonic CD stage aux_state.jsonl events. The event vocabulary and field
    /// shapes are the S1 standard recorder's (<see cref="S1AuxEventEngine"/>):
    /// mode_change, routine_change, state_snapshot (every 60 rows and on the
    /// same triggers), object_appeared / object_removed / object_near over
    /// slots 1..127, slot_dump of the dynamic slots, and cursor_state from
    /// the object spawner. The CD stage spawner is S1's ObjPosLoad
    /// (src/Level/Object Spawner.asm), so cursor_state maps objPrevChunk,
    /// objChunkRight/Left and savedObjFlags[0..1]. S1's s1_obj64_state is
    /// omitted (object $64 is a different CD object), and the complete-run
    /// lag_state event is emitted every row so replay has the emulator's lag
    /// flag. "vfc" is <c>levelFrames</c>. No new event type is introduced.
    /// </summary>
    public sealed class ScdAuxEventEngine
    {
        private const int ObjectProximity = 160;
        private const int SnapshotInterval = 60;

        private int prevStatus;
        private int prevRoutine;
        private int prevMoveLock;
        private int prevChunk = -1;
        private readonly byte[] knownObjects = new byte[ScdRam.TotalObjectSlots];

        public IList<string> ProcessFrame(int traceFrame, IGpgxHost host, bool lagged, int lagCount)
        {
            if (host == null) throw new ArgumentNullException("host");
            var lines = new List<string>();
            int p = ScdRam.PlayerBase;
            int vfc = S1Ram.U16(host, ScdRam.LevelFrames);
            byte status = S1Ram.U8(host, p + ScdRam.OffFlags);
            byte routine = S1Ram.U8(host, p + S1Ram.OffRoutine);

            ModeChange(lines, traceFrame, vfc, "air", prevStatus, status, S1Ram.StatusInAir, host, true);
            ModeChange(lines, traceFrame, vfc, "rolling", prevStatus, status, S1Ram.StatusRolling, host, false);
            ModeChange(lines, traceFrame, vfc, "on_object", prevStatus, status, S1Ram.StatusOnObject, host, false);
            prevStatus = status;
            int moveLock = S1Ram.U16(host, p + ScdRam.OffPlayerMoveLock);
            if ((prevMoveLock > 0) != (moveLock > 0))
                lines.Add(FormatModeChange(traceFrame, vfc, "control_locked", prevMoveLock > 0, moveLock > 0));
            prevMoveLock = moveLock;
            if (routine != prevRoutine)
            {
                lines.Add(FormatRoutineChange(traceFrame, vfc, prevRoutine, routine, status, host));
                if (routine == 0x04 || routine == 0x06)
                    lines.Add(FormatStateSnapshot(traceFrame, vfc, host));
            }
            prevRoutine = routine;

            if (traceFrame % SnapshotInterval == 0)
                lines.Add(FormatStateSnapshot(traceFrame, vfc, host));

            ScanObjects(lines, traceFrame, vfc, host);
            lines.Add("{\"frame\":" + Dec(traceFrame) + ",\"event\":\"lag_state\",\"lagged\":"
                + (lagged ? "true" : "false") + ",\"lagcount\":" + Dec(lagCount) + "}");
            CursorState(lines, traceFrame, vfc, host);
            return lines;
        }

        private void ModeChange(List<string> lines, int traceFrame, int vfc, string field,
            int before, int after, int bit, IGpgxHost host, bool snapshot)
        {
            bool was = (before & bit) != 0;
            bool now = (after & bit) != 0;
            if (was == now) return;
            lines.Add(FormatModeChange(traceFrame, vfc, field, was, now));
            if (snapshot) lines.Add(FormatStateSnapshot(traceFrame, vfc, host));
        }

        private static string FormatModeChange(int traceFrame, int vfc, string field, bool from, bool to)
        {
            return "{\"frame\":" + Dec(traceFrame) + ",\"vfc\":" + Dec(vfc)
                + ",\"event\":\"mode_change\",\"field\":\"" + field
                + "\",\"from\":" + (from ? "1" : "0") + ",\"to\":" + (to ? "1" : "0") + "}";
        }

        private static string FormatRoutineChange(int traceFrame, int vfc, int from, int to,
            byte status, IGpgxHost host)
        {
            int p = ScdRam.PlayerBase;
            byte stand = S1Ram.U8(host, p + ScdRam.OffPlayerStandObj);
            string context = "";
            if (stand > 0 && stand < ScdRam.TotalObjectSlots)
            {
                int obj = ScdRam.SlotAddress(stand);
                context = ",\"stand_obj_slot\":" + Dec(stand)
                    + ",\"stand_obj_type\":\"0x" + Hex2(S1Ram.U8(host, obj)) + "\""
                    + ",\"stand_obj_x\":\"0x" + Hex4(S1Ram.U16(host, obj + S1Ram.OffXPos)) + "\""
                    + ",\"stand_obj_y\":\"0x" + Hex4(S1Ram.U16(host, obj + S1Ram.OffYPos)) + "\""
                    + ",\"stand_obj_routine\":\"0x" + Hex2(S1Ram.U8(host, obj + S1Ram.OffRoutine)) + "\"";
            }
            return "{\"frame\":" + Dec(traceFrame) + ",\"vfc\":" + Dec(vfc)
                + ",\"event\":\"routine_change\",\"from\":\"0x" + Hex2(from)
                + "\",\"to\":\"0x" + Hex2(to)
                + "\",\"sonic_x\":\"0x" + Hex4(S1Ram.U16(host, p + S1Ram.OffXPos))
                + "\",\"sonic_y\":\"0x" + Hex4(S1Ram.U16(host, p + S1Ram.OffYPos))
                + "\",\"x_vel\":" + Dec(S1Ram.S16(host, p + S1Ram.OffXVel))
                + ",\"y_vel\":" + Dec(S1Ram.S16(host, p + S1Ram.OffYVel))
                + ",\"inertia\":" + Dec(S1Ram.S16(host, p + S1Ram.OffInertia))
                + ",\"status\":\"0x" + Hex2(status)
                + "\",\"stand_on_obj\":" + Dec(stand) + context + "}";
        }

        private static string FormatStateSnapshot(int traceFrame, int vfc, IGpgxHost host)
        {
            int p = ScdRam.PlayerBase;
            byte status = S1Ram.U8(host, p + ScdRam.OffFlags);
            return "{\"frame\":" + Dec(traceFrame) + ",\"vfc\":" + Dec(vfc)
                + ",\"event\":\"state_snapshot\",\"control_locked\":"
                + Bool(S1Ram.U16(host, p + ScdRam.OffPlayerMoveLock) > 0)
                + ",\"anim_id\":" + Dec(S1Ram.U8(host, p + S1Ram.OffAnimId))
                + ",\"status_byte\":\"0x" + Hex2(status)
                + "\",\"routine\":\"0x" + Hex2(S1Ram.U8(host, p + S1Ram.OffRoutine))
                + "\",\"y_radius\":" + Dec(S1Ram.S8(host, p + S1Ram.OffRadiusY))
                + ",\"x_radius\":" + Dec(S1Ram.S8(host, p + S1Ram.OffRadiusX))
                + ",\"on_object\":" + Bool((status & S1Ram.StatusOnObject) != 0)
                + ",\"pushing\":" + Bool((status & S1Ram.StatusPushing) != 0)
                + ",\"underwater\":" + Bool((status & S1Ram.StatusUnderwater) != 0)
                + ",\"roll_jumping\":" + Bool((status & S1Ram.StatusRollJump) != 0)
                + "}";
        }

        private void ScanObjects(List<string> lines, int traceFrame, int vfc, IGpgxHost host)
        {
            int playerX = S1Ram.U16(host, ScdRam.PlayerBase + S1Ram.OffXPos);
            int playerY = S1Ram.U16(host, ScdRam.PlayerBase + S1Ram.OffYPos);
            bool anyAppeared = false;
            for (int slot = 1; slot < ScdRam.TotalObjectSlots; slot++)
            {
                int addr = ScdRam.SlotAddress(slot);
                byte id = S1Ram.U8(host, addr);
                byte prev = knownObjects[slot];
                if (id != 0 && id != prev)
                {
                    lines.Add("{\"frame\":" + Dec(traceFrame) + ",\"vfc\":" + Dec(vfc)
                        + ",\"event\":\"object_appeared\",\"slot\":" + Dec(slot)
                        + ",\"object_type\":\"0x" + Hex2(id)
                        + "\",\"x\":\"0x" + Hex4(S1Ram.U16(host, addr + S1Ram.OffXPos))
                        + "\",\"y\":\"0x" + Hex4(S1Ram.U16(host, addr + S1Ram.OffYPos)) + "\"}");
                    anyAppeared = true;
                }
                if (id == 0 && prev != 0)
                {
                    lines.Add("{\"frame\":" + Dec(traceFrame) + ",\"vfc\":" + Dec(vfc)
                        + ",\"event\":\"object_removed\",\"slot\":" + Dec(slot)
                        + ",\"object_type\":\"0x" + Hex2(prev) + "\"}");
                }
                if (id != 0)
                {
                    int x = S1Ram.U16(host, addr + S1Ram.OffXPos);
                    int y = S1Ram.U16(host, addr + S1Ram.OffYPos);
                    if (Math.Abs(x - playerX) <= ObjectProximity && Math.Abs(y - playerY) <= ObjectProximity)
                    {
                        lines.Add("{\"frame\":" + Dec(traceFrame) + ",\"vfc\":" + Dec(vfc)
                            + ",\"event\":\"object_near\",\"slot\":" + Dec(slot)
                            + ",\"type\":\"0x" + Hex2(id)
                            + "\",\"x\":\"0x" + Hex4(x) + "\",\"y\":\"0x" + Hex4(y)
                            + "\",\"routine\":\"0x" + Hex2(S1Ram.U8(host, addr + S1Ram.OffRoutine))
                            + "\",\"status\":\"0x" + Hex2(S1Ram.U8(host, addr + ScdRam.OffFlags)) + "\"}");
                    }
                }
                knownObjects[slot] = id;
            }
            if (!anyAppeared) return;
            var dump = new StringBuilder("[");
            bool first = true;
            for (int slot = ScdRam.FirstDynamicSlot; slot < ScdRam.TotalObjectSlots; slot++)
            {
                byte id = S1Ram.U8(host, ScdRam.SlotAddress(slot));
                if (id == 0) continue;
                if (!first) dump.Append(',');
                first = false;
                dump.Append('[').Append(Dec(slot)).Append(",\"0x").Append(Hex2(id)).Append("\"]");
            }
            lines.Add("{\"frame\":" + Dec(traceFrame) + ",\"vfc\":" + Dec(vfc)
                + ",\"event\":\"slot_dump\",\"slots\":" + dump.Append(']') + "}");
        }

        private void CursorState(List<string> lines, int traceFrame, int vfc, IGpgxHost host)
        {
            int chunk = S1Ram.U16(host, ScdRam.ObjPrevChunk);
            if (chunk == prevChunk) return;
            string dir = (prevChunk >= 0 && chunk < prevChunk) ? "L" : "R";
            lines.Add("{\"frame\":" + Dec(traceFrame) + ",\"vfc\":" + Dec(vfc)
                + ",\"event\":\"cursor_state\",\"opl_screen\":\"0x" + Hex4(chunk)
                + "\",\"fwd_ptr\":\"0x" + Hex8(S1Ram.U32(host, ScdRam.ObjChunkRight))
                + "\",\"bwd_ptr\":\"0x" + Hex8(S1Ram.U32(host, ScdRam.ObjChunkLeft))
                + "\",\"fwd_ctr\":" + Dec(S1Ram.U8(host, ScdRam.SavedObjFlags))
                + ",\"bwd_ctr\":" + Dec(S1Ram.U8(host, ScdRam.SavedObjFlags + 1))
                + ",\"dir\":\"" + dir + "\"}");
            prevChunk = chunk;
        }

        private static string Bool(bool value) { return value ? "true" : "false"; }
        private static string Dec(long value) { return value.ToString(CultureInfo.InvariantCulture); }
        private static string Hex2(int value) { return value.ToString("X2", CultureInfo.InvariantCulture); }
        private static string Hex4(int value) { return value.ToString("X4", CultureInfo.InvariantCulture); }
        private static string Hex8(uint value) { return value.ToString("X8", CultureInfo.InvariantCulture); }
    }
}
