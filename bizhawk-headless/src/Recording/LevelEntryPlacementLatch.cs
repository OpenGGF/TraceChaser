using System;
using System.Collections.Generic;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>One PreLevel-load observation: the placement branch and the raw byte.</summary>
    public sealed class LevelEntryObservation
    {
        public LevelEntryObservation(string placementSource, int lastStarPostHit)
            : this(placementSource, lastStarPostHit, null)
        {
        }

        public LevelEntryObservation(
            string placementSource,
            int lastStarPostHit,
            IList<KeyValuePair<string, long>> ram)
        {
            if (placementSource == null)
            {
                throw new ArgumentNullException("placementSource");
            }
            PlacementSource = placementSource;
            LastStarPostHit = lastStarPostHit & 0xFF;
            Ram = ram;
        }

        /// <summary>
        /// The restore blocks and carried counters the load reads, in the
        /// game table's order; null when not captured.
        /// </summary>
        public IList<KeyValuePair<string, long>> Ram { get; private set; }

        public string PlacementSource { get; private set; }

        /// <summary>
        /// Raw S3K Last_star_post_hit ($FE2A) / S2 Last_star_pole_hit ($FE30)
        /// as the load reads it, including any bit 7 set by the special-stage
        /// results (sonic3k.asm:12154-12157, 12709-12712).
        /// </summary>
        public int LastStarPostHit { get; private set; }
    }

    /// <summary>
    /// The per-load observation behind a level segment's
    /// <c>level_entry_placement_source</c>. Every S2/S3K <c>Level:</c> entry
    /// sets Game_mode bit 7 before its first Wait_VSync (s2.asm:4757-4765,
    /// sonic3k.asm:7536-7561), so the first end-of-frame sample reading the
    /// PreLevel mode ($8C) still holds the placement inputs exactly as the
    /// load will read them; later frames of the same load may already have
    /// consumed or masked them. The latch records the zone the load was
    /// entered for, and the next level arm consumes it only when it arms
    /// for that same zone. Otherwise — no observed load since the previous
    /// arm, or a load of a different zone — no field is published.
    ///
    /// Observation only: it reads RAM the caller names and decides nothing
    /// about segmentation.
    /// </summary>
    public sealed class LevelEntryPlacementLatch
    {
        public const int PreLevelGameMode = 0x8C;

        private int previousGameMode = -1;
        private int? pendingZone;
        private LevelEntryObservation pendingObservation;

        /// <summary>
        /// Call once per evaluated frame with that frame's raw Game_mode
        /// and Current_zone. <paramref name="readSource"/> runs only on the
        /// first PreLevel frame of a load.
        /// </summary>
        public void Observe(
            int gameMode, int zone, Func<LevelEntryObservation> readSource)
        {
            if (readSource == null)
            {
                throw new ArgumentNullException("readSource");
            }
            if (gameMode == PreLevelGameMode
                && previousGameMode != PreLevelGameMode)
            {
                pendingZone = zone;
                pendingObservation = readSource();
            }
            previousGameMode = gameMode;
        }

        /// <summary>
        /// The latched observation for an arm of <paramref name="zone"/>, or
        /// null. Always clears the latch.
        /// </summary>
        public LevelEntryObservation Consume(int zone)
        {
            LevelEntryObservation observation =
                pendingZone.HasValue && pendingZone.Value == zone
                    ? pendingObservation
                    : null;
            pendingZone = null;
            pendingObservation = null;
            return observation;
        }

        /// <summary>
        /// S2/KiS2: LevelSizeLoad's start-location branch
        /// (s2.asm:14774-14779) tests Last_star_pole_hit; non-zero calls
        /// Obj79_LoadData (s2.asm:44776-44812), which restores the star-pole
        /// block. A special-stage return re-enters through the same star
        /// pole save, so S2 has no third source.
        /// </summary>
        public static string MapS2(int lastStarPoleHit)
        {
            return (lastStarPoleHit & 0xFF) == 0
                ? RunManifestSegment.PlacementStartTable
                : RunManifestSegment.PlacementStarpostRestore;
        }

        public static LevelEntryObservation ObserveS2(int lastStarPoleHit)
        {
            return new LevelEntryObservation(
                MapS2(lastStarPoleHit), lastStarPoleHit);
        }

        public static LevelEntryObservation ObserveS2(IGpgxHost host)
        {
            int lastStarPoleHit = S1Ram.U8(host, 0xFE30);
            return new LevelEntryObservation(
                MapS2(lastStarPoleHit), lastStarPoleHit,
                ReadFields(host, S2Fields));
        }

        /// <summary>
        /// The two additive metadata lines, emitted immediately after start_y,
        /// or nothing when the observation is absent.
        /// </summary>
        public static void AppendMetadata(
            System.Text.StringBuilder json, LevelEntryObservation observation)
        {
            if (observation == null)
            {
                return;
            }
            json.Append("  \"level_entry_placement_source\": \"")
                .Append(observation.PlacementSource).Append("\",\n");
            json.Append("  \"last_star_post_hit\": ")
                .Append(observation.LastStarPostHit.ToString(
                    System.Globalization.CultureInfo.InvariantCulture))
                .Append(",\n");
            if (observation.Ram == null)
            {
                return;
            }
            json.Append("  \"level_entry_ram\": {");
            for (int i = 0; i < observation.Ram.Count; i++)
            {
                if (i > 0)
                {
                    json.Append(", ");
                }
                json.Append('"').Append(observation.Ram[i].Key).Append("\": ")
                    .Append(observation.Ram[i].Value.ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
            }
            json.Append("},\n");
        }

        /// <summary>One named RAM read: unsigned big-endian of 1, 2 or 4 bytes.</summary>
        public sealed class RamField
        {
            public RamField(string name, int address, int size)
            {
                Name = name;
                Address = address;
                Size = size;
            }

            public string Name { get; private set; }
            public int Address { get; private set; }
            public int Size { get; private set; }
        }

        public static IList<KeyValuePair<string, long>> ReadFields(
            IGpgxHost host, RamField[] fields)
        {
            var values = new List<KeyValuePair<string, long>>(fields.Length);
            foreach (RamField field in fields)
            {
                long value = field.Size == 1 ? S1Ram.U8(host, field.Address)
                    : field.Size == 2 ? (long)S1Ram.U16(host, field.Address)
                    : (long)S1Ram.U32(host, field.Address);
                values.Add(new KeyValuePair<string, long>(field.Name, value));
            }
            return values;
        }

        /// <summary>
        /// S3K: the Saved_* block written by sub_2D164/Save_Level_Data and read
        /// by Load_Starpost_Settings (sonic3k.asm:61743-61769, 61800-61834),
        /// the Saved2_* block written by Save_Level_Data2 and read by loc_2D2C2
        /// (61775-61794, 61835-61857), and the carried counters the load keeps
        /// or clears (loc_63B4, 7826-7852). Addresses checked against the
        /// encoded Save_Level_Data ($2D176) and Save_Level_Data2 ($2D1CC)
        /// instructions.
        /// </summary>
        public static readonly RamField[] S3kFields =
        {
            new RamField("saved_last_star_post_hit", 0xFE2B, 1),
            new RamField("saved_zone_and_act", 0xFE2C, 2),
            new RamField("saved_apparent_zone_and_act", 0xFF9A, 2),
            new RamField("saved_x_pos", 0xFE2E, 2),
            new RamField("saved_y_pos", 0xFE30, 2),
            new RamField("saved_ring_count", 0xFE32, 2),
            new RamField("saved_timer", 0xFE34, 4),
            new RamField("saved_art_tile", 0xFE38, 2),
            new RamField("saved_solid_bits", 0xFE3A, 2),
            new RamField("saved_camera_x_pos", 0xFE3C, 2),
            new RamField("saved_camera_y_pos", 0xFE3E, 2),
            new RamField("saved_mean_water_level", 0xFE40, 2),
            new RamField("saved_water_full_screen_flag", 0xFE42, 1),
            new RamField("saved_extra_life_flags", 0xFE43, 1),
            new RamField("saved_camera_max_y_pos", 0xFE44, 2),
            new RamField("saved_dynamic_resize_routine", 0xFE46, 1),
            new RamField("saved_status_secondary", 0xFE47, 1),
            new RamField("special_bonus_entry_flag", 0xFE48, 1),
            new RamField("saved2_last_star_post_hit", 0xFE49, 1),
            new RamField("saved2_zone_and_act", 0xFE4A, 2),
            new RamField("saved2_apparent_zone_and_act", 0xFF9C, 2),
            new RamField("saved2_x_pos", 0xFE4C, 2),
            new RamField("saved2_y_pos", 0xFE4E, 2),
            new RamField("saved2_ring_count", 0xFE50, 2),
            new RamField("saved2_timer", 0xFE52, 4),
            new RamField("saved2_art_tile", 0xFE56, 2),
            new RamField("saved2_solid_bits", 0xFE58, 2),
            new RamField("saved2_camera_x_pos", 0xFE5A, 2),
            new RamField("saved2_camera_y_pos", 0xFE5C, 2),
            new RamField("saved2_mean_water_level", 0xFE5E, 2),
            new RamField("saved2_water_full_screen_flag", 0xFE60, 1),
            new RamField("saved2_extra_life_flags", 0xFE61, 1),
            new RamField("saved2_camera_max_y_pos", 0xFE62, 2),
            new RamField("saved2_dynamic_resize_routine", 0xFE64, 1),
            new RamField("saved2_status_secondary", 0xFF96, 1),
            // Native continuation bank, independent of Saved/Saved2 and live counters.
            // loc_63C14 / loc_7F310 store byte/word/long; loc_62FE clears the
            // flag and loc_59B1C / loc_5A49A consume the bank after this latch.
            new RamField("act3_flag", 0xEE5E, 1),
            new RamField("act3_ring_count", 0xEF5E, 2),
            new RamField("act3_timer", 0xEF60, 4),
            new RamField("life_count", 0xFE12, 1),
            new RamField("current_special_stage", 0xFE16, 1),
            new RamField("extra_life_flags", 0xFE1B, 1),
            new RamField("ring_count", 0xFE20, 2),
            new RamField("timer", 0xFE22, 4),
            new RamField("collected_special_ring_array", 0xFF92, 4),
            new RamField("respawn_table_keep", 0xFF97, 1),
            new RamField("chaos_emerald_count", 0xFFB0, 1),
            new RamField("super_emerald_count", 0xFFB1, 1),
            new RamField("collected_emerald_0", 0xFFB2, 1),
            new RamField("collected_emerald_1", 0xFFB3, 1),
            new RamField("collected_emerald_2", 0xFFB4, 1),
            new RamField("collected_emerald_3", 0xFFB5, 1),
            new RamField("collected_emerald_4", 0xFFB6, 1),
            new RamField("collected_emerald_5", 0xFFB7, 1),
            new RamField("collected_emerald_6", 0xFFB8, 1),
            new RamField("sk_special_stage_flag", 0xFFBB, 1),
            // Full 16.16 camera longs: the low word is the sub-pixel fraction,
            // which survives level loads (only Title_Screen clears Camera_RAM,
            // sonic3k.asm:5448) and feeds autoscrollers such as DDZ's. The
            // addresses are the loc_1BF8C stores (ROM $1BF8C).
            new RamField("camera_x_pos", 0xEE78, 4),
            new RamField("camera_y_pos", 0xEE7C, 4),
            new RamField("camera_x_pos_p2", 0xEE60, 4),
            new RamField("camera_y_pos_p2", 0xEE64, 4)
        };

        /// <summary>
        /// S2/KiS2: the star-pole block written by Obj79_SaveData and read by
        /// Obj79_LoadData (s2.asm:44733-44812; checked against the encoded
        /// routine at $1F35E), plus the carried counters.
        /// </summary>
        public static readonly RamField[] S2Fields =
        {
            new RamField("saved_last_star_pole_hit", 0xFE31, 1),
            new RamField("saved_x_pos", 0xFE32, 2),
            new RamField("saved_y_pos", 0xFE34, 2),
            new RamField("saved_ring_count", 0xFE36, 2),
            new RamField("saved_timer", 0xFE38, 4),
            new RamField("saved_art_tile", 0xFE3C, 2),
            new RamField("saved_solid_bits", 0xFE3E, 2),
            new RamField("saved_camera_x_pos", 0xFE40, 2),
            new RamField("saved_camera_y_pos", 0xFE42, 2),
            new RamField("saved_camera_bg_x_pos", 0xFE44, 2),
            new RamField("saved_camera_bg_y_pos", 0xFE46, 2),
            new RamField("saved_camera_bg2_x_pos", 0xFE48, 2),
            new RamField("saved_camera_bg2_y_pos", 0xFE4A, 2),
            new RamField("saved_camera_bg3_x_pos", 0xFE4C, 2),
            new RamField("saved_camera_bg3_y_pos", 0xFE4E, 2),
            new RamField("saved_water_level", 0xFE50, 2),
            new RamField("saved_water_routine", 0xFE52, 1),
            new RamField("saved_water_move", 0xFE53, 1),
            new RamField("saved_extra_life_flags", 0xFE54, 1),
            new RamField("saved_camera_max_y_pos", 0xFE56, 2),
            new RamField("saved_dynamic_resize_routine", 0xFE58, 1),
            new RamField("life_count", 0xFE12, 1),
            new RamField("extra_life_flags", 0xFE1B, 1),
            new RamField("ring_count", 0xFE20, 2),
            new RamField("timer", 0xFE22, 4),
            // Full 16.16 camera longs (s2.constants.asm:1216-1217; P2 pair from
            // the encoded LevelSizeLoad stores).
            new RamField("camera_x_pos", 0xEE00, 4),
            new RamField("camera_y_pos", 0xEE04, 4),
            new RamField("camera_x_pos_p2", 0xEE20, 4),
            new RamField("camera_y_pos_p2", 0xEE24, 4)
        };
    }
}
