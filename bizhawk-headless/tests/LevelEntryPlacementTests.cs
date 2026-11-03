using System;
using System.Collections.Generic;

namespace OpenGGF.BizHawk.Headless.Tests
{
    /// <summary>
    /// level_entry_placement_source: the ROM branch that placed the player
    /// for a level/bonus segment's load. Expectations come from the
    /// disassembly (S3K loc_1BE46 / Load_Starpost_Settings / loc_2D2C2;
    /// S2 LevelSizeLoad / Obj79_LoadData), never from a capture.
    ///
    /// The S3K frame streams model the two ROM writes that make a late
    /// sample wrong: loc_2D2C2 clears Special_bonus_entry_flag during the
    /// load, and the andi.b #$7F before LevelLoop erases a results-screen
    /// $80 on a star-post-less special-stage return. A recorder sampling at
    /// the arm would publish start_table for that return.
    /// </summary>
    internal static class LevelEntryPlacementTests
    {
        internal static void Register(List<TestMain.TestCase> tests)
        {
            tests.Add(new TestMain.TestCase(
                "LevelEntryPlacement S3K source follows loc_1BE46 and Load_Starpost_Settings",
                S3kMappingFollowsRomBranches));
            tests.Add(new TestMain.TestCase(
                "LevelEntryPlacement S2 source follows LevelSizeLoad",
                S2MappingFollowsRomBranch));
            tests.Add(new TestMain.TestCase(
                "LevelEntryPlacement latch reads only the first PreLevel frame",
                LatchReadsOnlyFirstPreLevelFrame));
            tests.Add(new TestMain.TestCase(
                "LevelEntryPlacement latch requires a load of the arming zone",
                LatchRequiresArmingZone));
            tests.Add(new TestMain.TestCase(
                "LevelEntryPlacement S3K segmenter publishes each load's source at arm",
                S3kSegmenterPublishesLoadSourceAtArm));
            tests.Add(new TestMain.TestCase(
                "LevelEntryPlacement metadata writers emit the key only when observed",
                MetadataWritersEmitKeyOnlyWhenObserved));
        }

        private static void S3kMappingFollowsRomBranches()
        {
            // tst.b Last_star_post_hit; beq -> start table, whatever the flag.
            AssertEx.Equal("start_table",
                S3KCompleteRunSegmenter.MapLevelEntryPlacementSource(0, 0));
            // Bonus-stage entry: loc_2D47E writes flag 2 and clears the hit.
            AssertEx.Equal("start_table",
                S3KCompleteRunSegmenter.MapLevelEntryPlacementSource(0, 2));
            AssertEx.Equal("starpost_restore",
                S3KCompleteRunSegmenter.MapLevelEntryPlacementSource(3, 0));
            // Special-stage results: ori.b #$80 with the flag still set.
            AssertEx.Equal("special_stage_restore",
                S3KCompleteRunSegmenter.MapLevelEntryPlacementSource(0x80, 1));
            AssertEx.Equal("special_stage_restore",
                S3KCompleteRunSegmenter.MapLevelEntryPlacementSource(0x83, 1));
        }

        private static void S2MappingFollowsRomBranch()
        {
            AssertEx.Equal("start_table", LevelEntryPlacementLatch.MapS2(0));
            AssertEx.Equal("starpost_restore", LevelEntryPlacementLatch.MapS2(1));
            AssertEx.Equal("starpost_restore", LevelEntryPlacementLatch.MapS2(0x82));
        }

        private static void LatchReadsOnlyFirstPreLevelFrame()
        {
            var latch = new LevelEntryPlacementLatch();
            int reads = 0;
            string value = "start_table";
            Func<string> read = () => { reads++; return value; };
            latch.Observe(0x04, 0, read);
            latch.Observe(0x8C, 1, read);
            value = "starpost_restore";   // a later write inside the load
            latch.Observe(0x8C, 1, read);
            latch.Observe(0x8C, 1, read);
            latch.Observe(0x0C, 1, read);
            AssertEx.Equal(1, reads);
            AssertEx.Equal("start_table", latch.Consume(1));
            // Consumed: a second arm without a new load publishes nothing.
            AssertEx.Equal<string>(null, latch.Consume(1));
            // $4C (SaveScreen) and a still-$0C restart are not PreLevel.
            latch.Observe(0x4C, 1, read);
            latch.Observe(0x0C, 1, read);
            AssertEx.Equal(1, reads);
            // A new load latches again.
            latch.Observe(0x8C, 1, read);
            AssertEx.Equal(2, reads);
            AssertEx.Equal("starpost_restore", latch.Consume(1));
        }

        private static void LatchRequiresArmingZone()
        {
            var latch = new LevelEntryPlacementLatch();
            latch.Observe(0x8C, 3, () => "starpost_restore");
            latch.Observe(0x0C, 3, () => "unused");
            AssertEx.Equal<string>(null, latch.Consume(4));
            AssertEx.Equal<string>(null, latch.Consume(3));
        }

        private sealed class Frame
        {
            internal int Mode;
            internal int Zone;
            internal int LastStarPostHit;
            internal int SpecialBonusEntryFlag;
        }

        private static void S3kSegmenterPublishesLoadSourceAtArm()
        {
            var plan = new List<Frame>();
            Action<int, int, int, int, int> span = (count, mode, zone, hit, flag) =>
            {
                for (int i = 0; i < count; i++)
                {
                    plan.Add(new Frame
                    {
                        Mode = mode,
                        Zone = zone,
                        LastStarPostHit = hit,
                        SpecialBonusEntryFlag = flag
                    });
                }
            };
            span(5, 0x04, 0, 0, 0);          // title
            span(5, 0x8C, 1, 0, 0);          // fresh HCZ load
            span(20, 0x0C, 1, 0, 0);         // arm 1: hcz
            span(5, 0x34, 0, 0, 1);          // giant-ring special stage
            span(3, 0x48, 1, 0x80, 1);       // results: ori.b #$80, zone=Saved2
            span(1, 0x8C, 1, 0x80, 1);       // first PreLevel frame
            span(4, 0x8C, 1, 0x80, 0);       // loc_2D2C2 cleared the flag
            span(20, 0x0C, 1, 0x00, 0);      // andi.b #$7F; arm 2: hcz_2
            span(5, 0x8C, 0x13, 0, 2);       // starpost bonus (gumball) load
            span(20, 0x0C, 0x13, 0, 2);      // arm 3: gumball
            span(5, 0x8C, 1, 2, 0);          // bonus exit restores Saved_*
            span(20, 0x0C, 1, 2, 0);         // arm 4: hcz_3
            span(5, 0x8C, 2, 0, 0);          // fresh MGZ load ...
            span(20, 0x0C, 2, 0, 0);         // arm 5: mgz

            Action<FakeS1Host, int> apply = (host, frame) =>
            {
                Frame f = frame < plan.Count ? plan[frame] : plan[plan.Count - 1];
                host.Ram[S3KRam.GameMode] = (byte)f.Mode;
                host.Ram[S3KRam.Zone] = (byte)f.Zone;
                host.Ram[S3KRam.LastStarPostHit] = (byte)f.LastStarPostHit;
                host.Ram[S3KRam.SpecialBonusEntryFlag] =
                    (byte)f.SpecialBonusEntryFlag;
            };
            var arms = new List<S3KSegmentArm>();
            var segmenter = new S3KCompleteRunSegmenter(plan.Count);
            segmenter.SegmentOpened = arms.Add;
            var fake = new FakeS1Host(apply);
            apply(fake, 0);
            while (!segmenter.Finished)
            {
                segmenter.Step(fake);
                if (segmenter.Finished)
                {
                    break;
                }
                fake.Advance();
            }
            segmenter.FinalizeRunEnd();

            var levelArms = new List<S3KSegmentArm>();
            foreach (S3KSegmentArm arm in arms)
            {
                if (arm.Kind != RunManifestSegment.SpecialStageKind)
                {
                    levelArms.Add(arm);
                }
                else
                {
                    AssertEx.Equal<string>(null, arm.LevelEntryPlacementSource);
                }
            }
            AssertEx.Equal(5, levelArms.Count);
            AssertEx.Equal("start_table", levelArms[0].LevelEntryPlacementSource);
            AssertEx.Equal("special_stage_restore", levelArms[1].LevelEntryPlacementSource);
            AssertEx.Equal("start_table", levelArms[2].LevelEntryPlacementSource);
            AssertEx.Equal("starpost_restore", levelArms[3].LevelEntryPlacementSource);
            AssertEx.Equal("start_table", levelArms[4].LevelEntryPlacementSource);
        }

        private static void MetadataWritersEmitKeyOnlyWhenObserved()
        {
            var arm = new S3KSegmentArm("hcz", RunManifestSegment.LevelKind,
                S3KCompleteRunSegmenter.LevelTraceProfile, 100, 0);
            arm.ZoneId = 1;
            arm.ZoneToken = "hcz";
            arm.StartX = 0x35B0;
            arm.StartY = 0x03EC;
            string absent = S3KCompleteRunMetadataWriter.Format(
                arm, 10, "movie.bk2", "2026-11-03", "run", 2);
            AssertEx.Equal(false, absent.Contains("level_entry_placement_source"));
            arm.LevelEntryPlacementSource = "starpost_restore";
            string present = S3KCompleteRunMetadataWriter.Format(
                arm, 10, "movie.bk2", "2026-11-03", "run", 2);
            AssertEx.Equal(true, present.Contains(
                "  \"start_y\": \"0x03EC\",\n"
                + "  \"level_entry_placement_source\": \"starpost_restore\",\n"));
            AssertEx.Equal(absent, present.Replace(
                "  \"level_entry_placement_source\": \"starpost_restore\",\n", ""));

            string s2Absent = S2TraceMetadataWriter.Format(
                0, 0, 0, 714, 10, 0x60, 0x290, false, 0, "gameplay_unlock",
                "kis2-ehz1.bk2", "2026-11-03", null, 0, false, true,
                S2TraceIdentity.StockSonic2);
            AssertEx.Equal(false, s2Absent.Contains("level_entry_placement_source"));
            string s2Present = S2TraceMetadataWriter.Format(
                0, 0, 0, 714, 10, 0x60, 0x290, false, 0, "gameplay_unlock",
                "kis2-ehz1.bk2", "2026-11-03", null, 0, false, true,
                S2TraceIdentity.StockSonic2, "start_table");
            AssertEx.Equal(true, s2Present.Contains(
                "  \"start_y\": \"0x0290\",\n"
                + "  \"level_entry_placement_source\": \"start_table\",\n"));
            AssertEx.Equal(s2Absent, s2Present.Replace(
                "  \"level_entry_placement_source\": \"start_table\",\n", ""));
        }
    }
}
