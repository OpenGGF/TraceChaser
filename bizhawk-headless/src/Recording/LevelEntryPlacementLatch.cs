using System;

namespace OpenGGF.BizHawk.Headless
{
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
    /// <summary>One PreLevel-load observation: the placement branch and the raw byte.</summary>
    public sealed class LevelEntryObservation
    {
        public LevelEntryObservation(string placementSource, int lastStarPostHit)
        {
            if (placementSource == null)
            {
                throw new ArgumentNullException("placementSource");
            }
            PlacementSource = placementSource;
            LastStarPostHit = lastStarPostHit & 0xFF;
        }

        public string PlacementSource { get; private set; }

        /// <summary>
        /// Raw S3K Last_star_post_hit ($FE2A) / S2 Last_star_pole_hit ($FE30)
        /// as the load reads it, including any bit 7 set by the special-stage
        /// results (sonic3k.asm:12154-12157, 12709-12712).
        /// </summary>
        public int LastStarPostHit { get; private set; }
    }

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
        }
    }
}
