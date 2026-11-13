using System;
using System.Globalization;
using System.Text;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>Start-of-recording identity captured on the detection frame.</summary>
    public sealed class ScdTraceStart
    {
        public int ZoneId;
        public int ActRaw;
        public int TimeZoneRaw;
        public int GoodFuture;
        public int StartX;
        public int StartY;
        public uint RngSeed;
        public string StageProgramHeaderSha256;
    }

    /// <summary>Recorder inputs whose identity the metadata records.</summary>
    public sealed class ScdTraceInputs
    {
        public string CueSha256;
        public string DataTrackSha256;
        public string BiosSha1;
        public string MovieSha256;
    }

    /// <summary>
    /// metadata.json for a Sonic CD stage trace: the S1 level layout and key
    /// order with the native v5 envelope, plus Sonic CD identity keys
    /// (time zone, stage file, stage-program fingerprint, disc/BIOS
    /// identity) that consumers ignore unless they read them. The game id
    /// "scd" is outside the current v5 validator's {s1,s2,s3k} set; that is
    /// a recorded contract gap, not something this writer works around.
    /// </summary>
    public static class ScdTraceMetadataWriter
    {
        public const string Game = "scd";
        public const string Profile = "scd_stage";

        /// <summary>Zone ids follow the stage numbering (zone 2 is Tidal Tempest, src/Level/Main.asm:741).</summary>
        public static string ZoneName(int zoneId)
        {
            switch (zoneId)
            {
                case 0: return "ppz";
                case 1: return "ccz";
                case 2: return "ttz";
                case 3: return "qqz";
                case 4: return "wwz";
                case 5: return "ssz";
                case 6: return "mmz";
                default: return "unknown_" + zoneId.ToString("x2", CultureInfo.InvariantCulture);
            }
        }

        public static string TimeZoneName(int timeZoneRaw)
        {
            switch (timeZoneRaw & 0x7F)
            {
                case 0: return "past";
                case 1: return "present";
                case 2: return "future";
                default: return "unknown";
            }
        }

        /// <summary>
        /// Disc stage file stem (FID_Rxxx, src/_Include/System.inc): round
        /// R1 for zone 0 and R(zone+2) after it, act digit, then A present,
        /// B past, C good future, D bad future.
        /// </summary>
        public static string StageFile(int zoneId, int actRaw, int timeZoneRaw, int goodFuture)
        {
            if (zoneId < 0 || zoneId > 6 || actRaw < 0 || actRaw > 2) return null;
            int round = zoneId == 0 ? 1 : zoneId + 2;
            char period;
            switch (timeZoneRaw & 0x7F)
            {
                case 0: period = 'B'; break;
                case 1: period = 'A'; break;
                case 2: period = goodFuture != 0 ? 'C' : 'D'; break;
                default: return null;
            }
            return "R" + round.ToString(CultureInfo.InvariantCulture)
                + (actRaw + 1).ToString(CultureInfo.InvariantCulture) + period;
        }

        public static string Format(ScdTraceStart start, ScdTraceInputs inputs,
            int bk2FrameOffset, int traceFrameCount, string recordingDate)
        {
            if (start == null) throw new ArgumentNullException("start");
            if (inputs == null) throw new ArgumentNullException("inputs");
            if (recordingDate == null) throw new ArgumentNullException("recordingDate");
            string stage = StageFile(start.ZoneId, start.ActRaw, start.TimeZoneRaw, start.GoodFuture);
            var json = new StringBuilder(1024);
            json.Append("{\n");
            json.Append("  \"game\": \"").Append(Game).Append("\",\n");
            json.Append("  \"trace_profile\": \"").Append(Profile).Append("\",\n");
            json.Append("  \"zone\": \"").Append(ZoneName(start.ZoneId)).Append("\",\n");
            json.Append("  \"zone_id\": ").Append(Dec(start.ZoneId)).Append(",\n");
            json.Append("  \"act\": ").Append(Dec(start.ActRaw + 1)).Append(",\n");
            json.Append("  \"time_zone\": \"").Append(TimeZoneName(start.TimeZoneRaw)).Append("\",\n");
            json.Append("  \"time_zone_id\": ").Append(Dec(start.TimeZoneRaw)).Append(",\n");
            json.Append("  \"good_future\": ").Append(Dec(start.GoodFuture)).Append(",\n");
            json.Append("  \"stage_file\": ").Append(stage == null ? "null" : "\"" + stage + "\"").Append(",\n");
            json.Append("  \"stage_program_header_sha256\": \"").Append(start.StageProgramHeaderSha256).Append("\",\n");
            json.Append("  \"bk2_frame_offset\": ").Append(Dec(bk2FrameOffset)).Append(",\n");
            json.Append("  \"trace_frame_count\": ").Append(Dec(traceFrameCount)).Append(",\n");
            json.Append("  \"start_x\": \"0x").Append(Hex4(start.StartX)).Append("\",\n");
            json.Append("  \"start_y\": \"0x").Append(Hex4(start.StartY)).Append("\",\n");
            json.Append("  \"characters\": [\"sonic\"],\n");
            json.Append("  \"main_character\": \"sonic\",\n");
            json.Append("  \"sidekicks\": [],\n");
            json.Append("  \"rng_seed\": \"0x").Append(start.RngSeed.ToString("X8", CultureInfo.InvariantCulture)).Append("\",\n");
            json.Append("  \"recording_date\": \"").Append(recordingDate).Append("\",\n");
            TraceContract.AppendNativeEnvelope(json);
            json.Append("  \"aux_schema_extras\": [],\n");
            json.Append("  \"disc_cue_sha256\": \"").Append(inputs.CueSha256).Append("\",\n");
            json.Append("  \"disc_data_track_sha256\": \"").Append(inputs.DataTrackSha256).Append("\",\n");
            json.Append("  \"bios_sha1\": \"").Append(inputs.BiosSha1).Append("\",\n");
            json.Append("  \"source_bk2_sha256\": \"").Append(inputs.MovieSha256).Append("\",\n");
            json.Append("  \"rom_checksum\": \"\",\n");
            json.Append("  \"notes\": \"Row N is sampled after BK2 row bk2_frame_offset+N is applied and one hardware frame completes; lag_counter is a placeholder, per-row lag is the lag_state aux event.\"\n");
            json.Append("}\n");
            return json.ToString();
        }

        private static string Dec(int value) { return value.ToString(CultureInfo.InvariantCulture); }
        private static string Hex4(int value) { return value.ToString("X4", CultureInfo.InvariantCulture); }
    }
}
