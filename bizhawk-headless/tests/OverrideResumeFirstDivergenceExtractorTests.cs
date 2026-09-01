using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace OpenGGF.BizHawk.Headless.Tests
{
    internal static class OverrideResumeFirstDivergenceExtractorTests
    {
        internal static void Register(ICollection<TestMain.TestCase> tests)
        {
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergenceExtractorTests normalize duplicate authenticated raws deterministically",
                NormalizesDuplicateAuthenticatedRawsDeterministically));
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergenceExtractorTests reject duplicate raw mismatch",
                RejectsDuplicateRawMismatch));
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergenceExtractorTests reject ambiguous boundary and missing PCM",
                RejectsAmbiguousBoundaryAndMissingPcm));
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergenceExtractorTests attest exact streamed UTF8 raw bytes",
                AttestsExactStreamedUtf8RawBytes));
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergenceExtractorTests reject truncated canonical rows",
                RejectsTruncatedCanonicalRows));
        }

        private static void NormalizesDuplicateAuthenticatedRawsDeterministically()
        {
            WithInputs((root, inputs) =>
            {
                OverrideResumeFirstDivergenceExtractor.Output first =
                    OverrideResumeFirstDivergenceExtractor.ForTesting().Extract(inputs);
                OverrideResumeFirstDivergenceExtractor.Output second =
                    OverrideResumeFirstDivergenceExtractor.ForTesting().Extract(inputs);
                AssertEx.Equal(Hex(first.S1.ReferenceGzip),
                    Hex(second.S1.ReferenceGzip));
                AssertEx.Equal(Hex(first.S2.ReferenceGzip),
                    Hex(second.S2.ReferenceGzip));
                AssertEx.Equal(0, first.S1.ReferenceGzip[4]);
                AssertEx.Equal(0, first.S1.ReferenceGzip[5]);
                AssertEx.Equal(0, first.S1.ReferenceGzip[6]);
                AssertEx.Equal(0, first.S1.ReferenceGzip[7]);
                AssertEx.Equal(255, first.S1.ReferenceGzip[9]);
                JObject s1 = JObject.Parse(Encoding.UTF8.GetString(
                    first.S1.MetadataUtf8));
                AssertEx.Equal(
                    "openggf.override-resume-first-divergence-metadata.v1",
                    (string)s1["schema"]);
                AssertEx.Equal("s1", (string)s1["game"]);
                AssertEx.Equal(2, ((JArray)s1["raw_sha256"]).Count);
                string reference = Gunzip(first.S1.ReferenceGzip);
                AssertEx.Equal(true, reference.EndsWith("\n",
                    StringComparison.Ordinal));
                AssertEx.Equal(true, reference.Contains(
                    "openggf.override-resume-first-divergence-reference.v1"));
                AssertEx.Equal(true, reference.Contains("\"pcm_hex\":\"0100ffff\""));
            });
        }

        private static void RejectsDuplicateRawMismatch()
        {
            WithInputs((root, inputs) =>
            {
                File.AppendAllText(inputs.S1Raw2, "{}\n", new UTF8Encoding(false));
                AssertEx.Throws<InvalidDataException>(() =>
                    OverrideResumeFirstDivergenceExtractor.ForTesting().Extract(inputs),
                    "duplicate S1 raw bytes");
            });
        }

        private static void RejectsAmbiguousBoundaryAndMissingPcm()
        {
            WithInputs((root, inputs) =>
            {
                string raw = File.ReadAllText(inputs.S2Raw1);
                JObject row = JObject.Parse(raw.Split('\n')[2]);
                JObject duplicate = (JObject)row.DeepClone();
                duplicate["row"] = (int)row["row"] + 1;
                raw = raw.Replace(raw.Split('\n')[3],
                    duplicate.ToString(Newtonsoft.Json.Formatting.None)
                    + "\n" + raw.Split('\n')[3]);
                Write(inputs.S2Raw1, raw);
                Write(inputs.S2Raw2, raw);
                WriteAttestation(inputs.S2Attestation1, "s2", inputs.S2Raw1,
                    "2026-09-01T00:00:00Z");
                WriteAttestation(inputs.S2Attestation2, "s2", inputs.S2Raw2,
                    "2026-09-01T00:00:01Z");
                AssertEx.Throws<InvalidDataException>(() =>
                    OverrideResumeFirstDivergenceExtractor.ForTesting().Extract(inputs),
                    "ambiguous");
            });
        }

        private static void AttestsExactStreamedUtf8RawBytes()
        {
            var output=new StringWriter();
            var hashing=new OverrideResumeRawDigestTextWriter(output);
            hashing.Write("{\"type\":\"metadata\"}\n");
            hashing.Write("{\"type\":\"terminal\"}\n");
            OverrideResumeRawDigestTextWriter.Evidence evidence=hashing.Finish();
            byte[] expected=Encoding.UTF8.GetBytes(output.ToString());
            AssertEx.Equal(expected.Length,evidence.ByteCount);
            AssertEx.Equal(Digest(expected),evidence.Sha256);
            JObject attestation=OverrideResumeFirstDivergenceAttestation.Create(
                "s1",evidence,"unit-authority",
                new DateTime(2026,9,1,0,0,0,DateTimeKind.Utc));
            AssertEx.Equal("2026-09-01T00:00:00Z",
                (string)attestation["capture_timestamp_utc"]);
            AssertEx.Equal(evidence.Sha256,(string)attestation["raw_sha256"]);
        }

        private static void RejectsTruncatedCanonicalRows()
        {
            WithInputs((root,inputs)=>AssertEx.Throws<InvalidDataException>(
                ()=>new OverrideResumeFirstDivergenceExtractor().Extract(inputs),
                "not contiguous"));
        }

        internal static void WithInputs(Action<string,
            OverrideResumeFirstDivergenceExtractor.Inputs> action)
        {
            string root = TestScratch.CreateRootPath("override-resume-extractor");
            Directory.CreateDirectory(root);
            try
            {
                string s1 = S1Raw();
                string s2 = S2Raw();
                string s1r1 = Path.Combine(root, "s1-1.jsonl");
                string s1r2 = Path.Combine(root, "s1-2.jsonl");
                string s2r1 = Path.Combine(root, "s2-1.jsonl");
                string s2r2 = Path.Combine(root, "s2-2.jsonl");
                Write(s1r1, s1); Write(s1r2, s1);
                Write(s2r1, s2); Write(s2r2, s2);
                string s1a1 = Path.Combine(root, "s1-1.attestation.json");
                string s1a2 = Path.Combine(root, "s1-2.attestation.json");
                string s2a1 = Path.Combine(root, "s2-1.attestation.json");
                string s2a2 = Path.Combine(root, "s2-2.attestation.json");
                WriteAttestation(s1a1, "s1", s1r1,
                    "2026-09-01T00:00:00Z");
                WriteAttestation(s1a2, "s1", s1r2,
                    "2026-09-01T00:00:01Z");
                WriteAttestation(s2a1, "s2", s2r1,
                    "2026-09-01T00:00:00Z");
                WriteAttestation(s2a2, "s2", s2r2,
                    "2026-09-01T00:00:01Z");
                action(root, new OverrideResumeFirstDivergenceExtractor.Inputs(
                    s1r1, s1a1, s1r2, s1a2, s2r1, s2a1, s2r2, s2a2));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static string S1Raw()
        {
            return Line(new JObject
            {
                ["type"]="metadata",
                ["schema"]="openggf.s1-complete-run-audio-raw.v1",
                ["rom_sha1"]="69e102855d4389c3fd1a8f3dc7d193f8eee5fe5b",
                ["bk2_sha256"]=S1CompleteRunAudioReferenceCapture.MovieSha256,
                ["first_row"]=860,["exclusive_end"]=225101,
                ["native_abi"]=4,["native_event_size"]=32,["native_capacity"]=65536
            }) + Line(new JObject { ["type"]="frame_begin",["row"]=3910 })
                + Line(new JObject
                {
                    ["type"]="override_resume",["request"]="cfFadeInToPrevious",
                    ["admission"]="native_restore_entry",["request_frame"]=3698,
                    ["admission_frame"]=3699,["frame"]=3910,["pc"]=0x72B14,
                    ["service_token"]=9,["native_ordinal"]=30,["fix_bugs"]=0,
                    ["writes_dac_disable_zero"]=false,
                    ["writes"]=new JArray(new JObject
                    {
                        ["native_ordinal"]=31,["event_kind"]=3,["subject"]=1,
                        ["value"]=0x7f,["pc"]=0x72B3E,["source_cpu"]=2,
                        ["data"]=true,["port"]=0,["register"]=0x28
                    })
                }) + Line(new JObject
                {
                    ["type"]="native_pcm_packet",["selection"]="service_frame",
                    ["row"]=3910,["offset"]=0,["sample_rate"]=44100,["channels"]=2,
                    ["format"]="s16le-interleaved-stereo",["stereo_frames"]=1,
                    ["byte_count"]=4,["pcm_hex"]="0100ffff",
                    ["sha256"]="16b8cb1fe734fbc60c6763c94c9e4cc55840ae966e7e508ba82f539d82702511"
                }) + Line(new JObject { ["type"]="frame_end",["row"]=3910 })
                + Line(new JObject
                {
                    ["type"]="terminal",["exclusive_end"]=225101,["rows"]=224241,
                    ["orphan_closes"]=0,["opcode_mismatches"]=0,["overflows"]=0
                });
        }

        private static string S2Raw()
        {
            return Line(new JObject
            {
                ["type"]="metadata",["schema"]="openggf.s2-complete-run-audio-raw.v2",
                ["rom_sha1"]="8bca5dcef1af3e00098666fd892dc1c2a76333f9",
                ["bk2_sha256"]="e850798f882b8c580aad148bc97cb50f260cae1d336dd649fe2f4dfae6796aa5",
                ["first_row"]=769,["exclusive_end"]=259590
            }) + Line(new JObject { ["type"]="baseline" }) + Line(new JObject
            {
                ["type"]="frame",["row"]=4000,["lag"]=false,["state_hex"]="00",
                ["events"]=new JArray(),
                ["override_resume"]=new JObject
                {
                    ["request"]="cfFadeInToPrevious",
                    ["admission"]="native_service_completion",["request_pc"]=0x0D35,
                    ["pc"]=0x0DB4,["service_token"]=7,["service_begin_ordinal"]=10,
                    ["native_ordinal"]=42,["frame"]=4000,["fix_driver_bugs"]=0,
                    ["restores_saved_priority"]=true,["restores_psg_noise"]=false,
                    ["writes"]=new JArray(new JObject
                    {
                        ["native_ordinal"]=41,["event_kind"]=3,["subject"]=1,
                        ["value"]=0x40,["pc"]=0x0D70,["source_cpu"]=1,
                        ["data"]=true,["port"]=0,["register"]=0x28
                    })
                },
                ["pcm"]=new JObject
                {
                    ["selection"]="service_frame",["row"]=4000,["offset"]=0,
                    ["sample_rate"]=44100,["channels"]=2,
                    ["format"]="s16le-interleaved-stereo",["stereo_frames"]=1,
                    ["byte_count"]=4,["pcm_hex"]="0100ffff",
                    ["sha256"]="16b8cb1fe734fbc60c6763c94c9e4cc55840ae966e7e508ba82f539d82702511"
                }
            }) + Line(new JObject
            {
                ["type"]="terminal",["exclusive_end"]=259590,
                ["faulted"]=false,["overflows"]=0
            });
        }

        private static void WriteAttestation(string path, string game,
            string rawPath, string timestamp)
        {
            byte[] raw = File.ReadAllBytes(rawPath);
            JObject value = new JObject
            {
                ["schema"]="openggf.override-resume-first-divergence-attestation.v1",
                ["capture_timestamp_utc"]=timestamp,["game"]=game,
                ["raw_sha256"]=Digest(raw),["raw_byte_count"]=raw.Length,
                ["status"]="ok",["fault_count"]=0,["overflow_count"]=0,
                ["authority_id"]="synthetic-unit-authority"
            };
            Write(path, value.ToString(Newtonsoft.Json.Formatting.None) + "\n");
        }

        private static string Line(JObject value)
        { return value.ToString(Newtonsoft.Json.Formatting.None) + "\n"; }
        private static void Write(string path, string value)
        { File.WriteAllText(path, value, new UTF8Encoding(false)); }
        private static string Digest(byte[] value)
        { using (SHA256 sha = SHA256.Create()) return Hex(sha.ComputeHash(value)); }
        private static string Hex(byte[] value)
        {
            var result = new StringBuilder(value.Length * 2);
            foreach (byte item in value) result.Append(item.ToString("x2"));
            return result.ToString();
        }
        private static string Gunzip(byte[] value)
        {
            using (var input = new MemoryStream(value))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip, new UTF8Encoding(false, true)))
                return reader.ReadToEnd();
        }
    }
}
