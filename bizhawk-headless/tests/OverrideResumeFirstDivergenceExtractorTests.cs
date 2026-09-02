using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergenceExtractorTests reject open raw metadata contracts",
                RejectsOpenRawMetadataContracts));
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergenceExtractorTests reject open selected boundary contracts",
                RejectsOpenSelectedBoundaryContracts));
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergenceExtractorTests reject open selected PCM contracts",
                RejectsOpenSelectedPcmContracts));
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergenceExtractorTests validate every selected write contract",
                ValidatesEverySelectedWriteContract));
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergenceExtractorTests accept timestamp-value-only attestation differences",
                AcceptsTimestampValueOnlyAttestationDifferences));
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergenceExtractorTests reject attestation whitespace differences",
                RejectsAttestationWhitespaceDifferences));
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergenceExtractorTests reject attestation member-order differences",
                RejectsAttestationMemberOrderDifferences));
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergenceExtractorTests reject attestation terminal-newline differences",
                RejectsAttestationTerminalNewlineDifferences));
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergenceExtractorTests reject other attestation byte differences",
                RejectsOtherAttestationByteDifferences));
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergenceExtractorTests reject malformed and multi-record attestations",
                RejectsMalformedAndMultiRecordAttestations));
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
                AssertEx.Equal(
                    "src/test/resources/audio/parity/"
                    + "override-resume-first-divergence-v1",
                    (string)s1["bundle_relative_root"]);
                AssertEx.Equal(
                    "linux-atomic-bundle-rename-noreplace-v1",
                    (string)s1["publication_protocol"]);
                AssertEx.Equal(4,
                    ((JArray)s1["bundle_member_inventory"]).Count);
                AssertEx.Equal(true,
                    ((string)s1["namespace_lock_precondition"])
                        .Contains("namespace-stable"));
                string reference = Gunzip(first.S1.ReferenceGzip);
                AssertEx.Equal(true, reference.EndsWith("\n",
                    StringComparison.Ordinal));
                AssertEx.Equal(true, reference.Contains(
                    "openggf.override-resume-first-divergence-reference.v1"));
                AssertEx.Equal(true, reference.Contains("\"pcm_hex\":\"0100ffff\""));

                JObject s1Reference = JObject.Parse(reference);
                AssertExactProperties(s1Reference,
                    "boundary", "game", "pcm", "schema");
                AssertExactProperties((JObject)s1Reference["boundary"],
                    "admission", "admission_frame", "fix_bugs", "frame",
                    "native_ordinal", "pc", "request", "request_frame",
                    "service_token", "type", "writes",
                    "writes_dac_disable_zero");
                AssertExactProperties((JObject)s1Reference["pcm"],
                    "byte_count", "channels", "format", "offset", "pcm_hex",
                    "row", "sample_rate", "selection", "sha256",
                    "stereo_frames", "type");
                AssertExactProperties((JObject)((JArray)
                    s1Reference["boundary"]["writes"])[0],
                    "data", "event_kind", "native_ordinal", "pc", "port",
                    "register", "source_cpu", "subject", "value");
                AssertExactProperties(s1,
                    "attestation_sha256", "game", "logical_byte_count",
                    "logical_sha256", "raw_byte_count", "raw_sha256",
                    "record_count", "schema", "stored_byte_count",
                    "stored_sha256", "bundle_relative_root",
                    "bundle_member_inventory", "publication_protocol",
                    "namespace_lock_precondition");

                JObject s2Reference = JObject.Parse(
                    Gunzip(first.S2.ReferenceGzip));
                AssertExactProperties((JObject)s2Reference["boundary"],
                    "admission", "fix_driver_bugs", "frame", "native_ordinal",
                    "pc", "request", "request_pc", "restores_psg_noise",
                    "restores_saved_priority", "service_begin_ordinal",
                    "service_token", "writes");
                AssertExactProperties((JObject)s2Reference["pcm"],
                    "byte_count", "channels", "format", "offset", "pcm_hex",
                    "row", "sample_rate", "selection", "sha256",
                    "stereo_frames");
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

        private static void RejectsOpenRawMetadataContracts()
        {
            AssertRejectsRawMutation("s1", rows =>
                rows[0]["unexpected"] = 1, "unknown property");
            AssertRejectsRawMutation("s1", rows =>
                rows[0].Remove("native_capacity"), "missing property");
            AssertRejectsRawMutation("s2", rows =>
                rows[0]["unexpected"] = 1, "unknown property");
            AssertRejectsRawMutation("s2", rows =>
                rows[0].Remove("service_manifest_sha256"), "missing property");
        }

        private static void RejectsOpenSelectedBoundaryContracts()
        {
            AssertRejectsRawMutation("s1", rows =>
                FindBoundary(rows, "s1")["unexpected"] = true,
                "unknown property");
            AssertRejectsRawMutation("s1", rows =>
                FindBoundary(rows, "s1").Remove("admission_frame"),
                "missing property");
            AssertRejectsRawMutation("s2", rows =>
                FindBoundary(rows, "s2")["unexpected"] = true,
                "unknown property");
            AssertRejectsRawMutation("s2", rows =>
                FindBoundary(rows, "s2").Remove("request_pc"),
                "missing property");
        }

        private static void RejectsOpenSelectedPcmContracts()
        {
            AssertRejectsRawMutation("s1", rows =>
                FindPcm(rows, "s1")["unexpected"] = true,
                "unknown property");
            AssertRejectsRawMutation("s1", rows =>
                FindPcm(rows, "s1").Remove("format"), "missing property");
            AssertRejectsRawMutation("s2", rows =>
                FindPcm(rows, "s2")["unexpected"] = true,
                "unknown property");
            AssertRejectsRawMutation("s2", rows =>
                FindPcm(rows, "s2").Remove("sha256"), "missing property");
        }

        private static void ValidatesEverySelectedWriteContract()
        {
            AssertRejectsRawMutation("s1", rows =>
            {
                JArray writes = (JArray)FindBoundary(rows, "s1")["writes"];
                JObject second = (JObject)writes[0].DeepClone();
                second["native_ordinal"] = 32;
                second["unexpected"] = true;
                writes.Add(second);
            }, "unknown property");
            AssertRejectsRawMutation("s2", rows =>
                ((JObject)((JArray)FindBoundary(rows, "s2")["writes"])[0])
                    .Remove("register"), "missing property");
        }

        private static void AcceptsTimestampValueOnlyAttestationDifferences()
        {
            WithInputs((root, inputs) =>
            {
                OverrideResumeFirstDivergenceExtractor.ForTesting()
                    .Extract(inputs);
            });
        }

        private static void RejectsAttestationWhitespaceDifferences()
        {
            AssertRejectsAttestationMutation(text => " " + text,
                "not canonical");
        }

        private static void RejectsAttestationMemberOrderDifferences()
        {
            AssertRejectsAttestationMutation(text =>
            {
                JObject parsed = JObject.Parse(text);
                var reordered = new JObject();
                foreach (JProperty property in parsed.Properties().Reverse())
                    reordered.Add(property.Name, property.Value.DeepClone());
                return reordered.ToString(Newtonsoft.Json.Formatting.None) + "\n";
            }, "not canonical");
        }

        private static void RejectsAttestationTerminalNewlineDifferences()
        {
            AssertRejectsAttestationMutation(text => text + "\n",
                "not canonical");
        }

        private static void RejectsOtherAttestationByteDifferences()
        {
            AssertRejectsAttestationMutation(text =>
            {
                JObject parsed = JObject.Parse(text);
                parsed["authority_id"] = "different-authority";
                return parsed.ToString(Newtonsoft.Json.Formatting.None) + "\n";
            }, "timestamp normalization");
        }

        private static void RejectsMalformedAndMultiRecordAttestations()
        {
            AssertRejectsAttestationMutation(text => "{\n", "attestation");
            AssertRejectsAttestationMutation(text => text + "{}\n",
                "attestation");
        }

        private static void AssertRejectsAttestationMutation(
            Func<string, string> mutation, string message)
        {
            WithInputs((root, inputs) =>
            {
                Write(inputs.S1Attestation2, mutation(
                    File.ReadAllText(inputs.S1Attestation2)));
                AssertEx.Throws<InvalidDataException>(() =>
                    OverrideResumeFirstDivergenceExtractor.ForTesting()
                        .Extract(inputs), message);
            });
        }

        private static void AssertRejectsRawMutation(string game,
            Action<IList<JObject>> mutation, string message)
        {
            WithInputs((root, inputs) =>
            {
                string first = game == "s1" ? inputs.S1Raw1 : inputs.S2Raw1;
                string second = game == "s1" ? inputs.S1Raw2 : inputs.S2Raw2;
                IList<JObject> rows = File.ReadAllText(first)
                    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(JObject.Parse).ToList();
                mutation(rows);
                string value = string.Concat(rows.Select(Line));
                Write(first, value);
                Write(second, value);
                if (game == "s1")
                {
                    WriteAttestation(inputs.S1Attestation1, game, first,
                        "2026-09-01T00:00:00Z");
                    WriteAttestation(inputs.S1Attestation2, game, second,
                        "2026-09-01T00:00:01Z");
                }
                else
                {
                    WriteAttestation(inputs.S2Attestation1, game, first,
                        "2026-09-01T00:00:00Z");
                    WriteAttestation(inputs.S2Attestation2, game, second,
                        "2026-09-01T00:00:01Z");
                }
                AssertEx.Throws<InvalidDataException>(() =>
                    OverrideResumeFirstDivergenceExtractor.ForTesting()
                        .Extract(inputs), message);
            });
        }

        private static JObject FindBoundary(IList<JObject> rows, string game)
        {
            if (game == "s1")
                return rows.Single(row => (string)row["type"] ==
                    "override_resume");
            return (JObject)rows.Single(row => (string)row["type"] == "frame")
                ["override_resume"];
        }

        private static JObject FindPcm(IList<JObject> rows, string game)
        {
            if (game == "s1")
                return rows.Single(row => (string)row["type"] ==
                    "native_pcm_packet");
            return (JObject)rows.Single(row => (string)row["type"] == "frame")
                ["pcm"];
        }

        private static void AssertExactProperties(JObject value,
            params string[] expected)
        {
            string actual = string.Join(",", value.Properties()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
            AssertEx.Equal(string.Join(",", expected.OrderBy(
                name => name, StringComparer.Ordinal)), actual);
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
                ["service_manifest_sha256"]=S2AudioObserverProfile.ServiceManifestSha256,
                ["first_row"]=769,["exclusive_end"]=259590,
                ["state_start"]=S2AudioObserverProfile.DriverStateStart,
                ["state_exclusive_end"]=S2AudioObserverProfile.DriverStateExclusiveEnd
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
