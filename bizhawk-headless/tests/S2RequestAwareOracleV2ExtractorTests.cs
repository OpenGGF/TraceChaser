using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpenGGF.BizHawk.Headless.Tests
{
    internal static class S2RequestAwareOracleV2ExtractorTests
    {
        internal static void Register(ICollection<TestMain.TestCase> tests)
        {
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests define the closed raw-v2 contract",
                DefinesClosedContract));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests project the exact 750-row window deterministically",
                ProjectsExactWindowDeterministically));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests reject malformed candidate authority before output",
                RejectMalformedCandidateAuthority));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests reject unbound production publication",
                RejectsUnboundProductionPublication));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests reject unknown raw fields and marker disagreement",
                RejectUnknownRawFieldsAndMarkerDisagreement));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests preserve existing output and clean failed staging",
                PreservesExistingOutputAndCleansFailedStaging));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests reject request order owner terminal and trailing violations",
                RejectRequestOrderOwnerTerminalAndTrailingViolations));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests reject raw attestation disagreement",
                RejectsRawAttestationDisagreement));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests keep the raw path line-streamed",
                KeepsRawPathLineStreamed));
        }

        private static void DefinesClosedContract()
        {
            AssertEx.Equal("openggf.s2-oracle-audio-raw.v2",
                S2RequestAwareOracleV2Extractor.OracleSchema);
        }

        private static void ProjectsExactWindowDeterministically()
        {
            WithSyntheticInputs((root, input) =>
            {
                string first = Path.Combine(root, "first.jsonl");
                string second = Path.Combine(root, "second.jsonl");
                input.Extractor.ExtractForTesting(input.Raw, input.Capability,
                    input.Attestation, first);
                input.Extractor.ExtractForTesting(input.Raw, input.Capability,
                    input.Attestation, second);
                byte[] firstBytes = File.ReadAllBytes(first);
                AssertEx.Equal(Hex(firstBytes), Hex(File.ReadAllBytes(second)));
                string[] lines = File.ReadAllText(first).Split(new[] { '\n' },
                    StringSplitOptions.RemoveEmptyEntries);
                AssertEx.Equal(753, lines.Length);
                JObject metadata = JObject.Parse(lines[0]);
                AssertEx.Equal("openggf.s2-oracle-audio-raw.v2",
                    (string)metadata["schema"]);
                AssertEx.Equal(false, (bool)metadata["production_bound"]);
                JObject baseline = JObject.Parse(lines[1]);
                AssertEx.Equal(10150, (int)baseline["row"]);
                AssertEx.Equal(10149, (int)baseline["source_preceding_row"]);
                AssertEx.Equal(0, (int)baseline["ym_port0_latch"]);
                AssertEx.Equal(0, (int)baseline["ym_port1_latch"]);
                JObject firstFrame = JObject.Parse(lines[2]);
                JArray transfers = (JArray)firstFrame["request_transfers"];
                AssertEx.Equal(1, transfers.Count);
                AssertEx.Equal(3, (int)transfers[0]["slot"]);
                AssertEx.Equal(0xB5, (int)transfers[0]["request"]);
                JObject secondFrame = JObject.Parse(lines[3]);
                AssertEx.Equal(2, ((JArray)secondFrame["request_transfers"]).Count);
                JObject cutoff = JObject.Parse(lines[752]);
                AssertEx.Equal(10900, (int)cutoff["exclusive_end"]);
            });
        }

        private static void RejectMalformedCandidateAuthority()
        {
            WithSyntheticInputs((root, input) =>
            {
                JObject capability = JObject.Parse(File.ReadAllText(input.Capability));
                capability["request_count"] = 99;
                File.WriteAllText(input.Capability, capability.ToString(Formatting.None));
                AssertEx.Throws<InvalidDataException>(() => input.Extractor
                    .ExtractForTesting(input.Raw, input.Capability,
                        input.Attestation, Path.Combine(root, "out.jsonl")),
                    "attestation");
                AssertEx.Equal(false, File.Exists(Path.Combine(root, "out.jsonl")));
            });
        }

        private static void RejectsUnboundProductionPublication()
        {
            WithSyntheticInputs((root, input) => AssertEx.Throws<InvalidDataException>(
                () => input.Extractor.ExtractProduction(input.Raw, input.Capability,
                    input.Attestation, Path.Combine(root, "out.jsonl")), "unbound"));
        }

        private static void RejectUnknownRawFieldsAndMarkerDisagreement()
        {
            WithSyntheticInputs((root, input) =>
            {
                string[] lines = File.ReadAllText(input.Raw).Split(new[] { '\n' },
                    StringSplitOptions.RemoveEmptyEntries);
                JObject firstFrame = JObject.Parse(lines[2]);
                firstFrame["unexpected"] = true;
                lines[2] = firstFrame.ToString(Formatting.None);
                RefreshAuthority(input, string.Join("\n", lines) + "\n");
                AssertEx.Throws<InvalidDataException>(() => input.Extractor
                    .ExtractForTesting(input.Raw, input.Capability, input.Attestation,
                        Path.Combine(root, "unknown.jsonl")), "unknown");

                RefreshAuthority(input, Encoding.UTF8.GetString(SyntheticRaw()));
                lines = File.ReadAllText(input.Raw).Split(new[] { '\n' },
                    StringSplitOptions.RemoveEmptyEntries);
                JObject transferFrame = JObject.Parse(lines[4]);
                ((JArray)transferFrame["request_transfers"])[0]["a7"] = "999";
                lines[4] = transferFrame.ToString(Formatting.None);
                RefreshAuthority(input, string.Join("\n", lines) + "\n");
                AssertEx.Throws<InvalidDataException>(() => input.Extractor
                    .ExtractForTesting(input.Raw, input.Capability, input.Attestation,
                        Path.Combine(root, "marker.jsonl")), "marker");

                RefreshAuthority(input, Encoding.UTF8.GetString(SyntheticRaw()));
                lines = File.ReadAllText(input.Raw).Split(new[] { '\n' },
                    StringSplitOptions.RemoveEmptyEntries);
                JObject markerFrame = JObject.Parse(lines[4]);
                ((JArray)markerFrame["events"])[2]["source_cpu"] = 1;
                lines[4] = markerFrame.ToString(Formatting.None);
                RefreshAuthority(input, string.Join("\n", lines) + "\n");
                AssertEx.Throws<InvalidDataException>(() => input.Extractor
                    .ExtractForTesting(input.Raw, input.Capability, input.Attestation,
                        Path.Combine(root, "near-marker.jsonl")), "candidate");
            });
        }

        private static void PreservesExistingOutputAndCleansFailedStaging()
        {
            WithSyntheticInputs((root, input) =>
            {
                string output = Path.Combine(root, "exists.jsonl");
                File.WriteAllText(output, "existing\n");
                AssertEx.Throws<IOException>(() => input.Extractor.ExtractForTesting(
                    input.Raw, input.Capability, input.Attestation, output), "exists");
                AssertEx.Equal("existing\n", File.ReadAllText(output));

                JObject capability = JObject.Parse(File.ReadAllText(input.Capability));
                capability["all_event_count"] = 0;
                WriteAuthority(input, capability, File.ReadAllBytes(input.Raw));
                string rejected = Path.Combine(root, "rejected.jsonl");
                AssertEx.Throws<InvalidDataException>(() => input.Extractor
                    .ExtractForTesting(input.Raw, input.Capability, input.Attestation,
                        rejected), "inventory count");
                AssertEx.Equal(false, File.Exists(rejected));
            });
        }

        private static void RejectRequestOrderOwnerTerminalAndTrailingViolations()
        {
            WithSyntheticInputs((root, input) =>
            {
                AssertRawRejection(input, root, lines =>
                {
                    JObject frame = JObject.Parse(lines[5]);
                    ((JArray)frame["request_transfers"])[1]["order"] = 0;
                    lines[5] = Json(frame);
                }, "order");
                AssertRawRejection(input, root, lines =>
                {
                    JObject frame = JObject.Parse(lines[4]);
                    ((JArray)frame["request_transfers"])[0]["source_cpu"] = 1;
                    lines[4] = Json(frame);
                }, "identity");
                AssertRawRejection(input, root, lines =>
                {
                    JObject cutoff = JObject.Parse(lines[lines.Length - 1]);
                    cutoff["exclusive_end"] = 10899;
                    lines[lines.Length - 1] = Json(cutoff);
                }, "cutoff");
                AssertRawRejection(input, root, lines =>
                { lines[lines.Length - 1] = lines[lines.Length - 1] + "\n{}"; },
                    "follow cutoff");
            });
        }

        private static void RejectsRawAttestationDisagreement()
        {
            WithSyntheticInputs((root, input) =>
            {
                File.AppendAllText(input.Raw, " ");
                AssertEx.Throws<InvalidDataException>(() => input.Extractor
                    .ExtractForTesting(input.Raw, input.Capability, input.Attestation,
                        Path.Combine(root, "attestation.jsonl")), "attestation");
            });
        }

        private static void KeepsRawPathLineStreamed()
        {
            string source = File.ReadAllText(Path.Combine(EndToEndTests.ToolDirectory,
                "src", "Recording", "S2RequestAwareOracleV2Extractor.cs"));
            AssertEx.Equal(false, source.Contains("File.ReadAllBytes(rawPath)"));
            AssertEx.Equal(false, source.Contains("ReadToEnd()"));
            AssertEx.Equal(true, source.Contains("new StreamReader(File.OpenRead(rawPath)"));
            AssertEx.Equal(true, source.Contains(".s2-request-window-"));
        }

        private static void AssertRawRejection(Inputs input, string root,
            Action<string[]> mutate, string message)
        {
            string[] lines = Encoding.UTF8.GetString(SyntheticRaw()).Split(
                new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            mutate(lines);
            RefreshAuthority(input, string.Join("\n", lines) + "\n");
            AssertEx.Throws<InvalidDataException>(() => input.Extractor
                .ExtractForTesting(input.Raw, input.Capability, input.Attestation,
                    Path.Combine(root, Guid.NewGuid().ToString("N") + ".jsonl")),
                message);
        }

        private sealed class Inputs
        {
            internal S2RequestAwareOracleV2Extractor Extractor;
            internal string Raw, Capability, Attestation;
        }

        private static void WithSyntheticInputs(Action<string, Inputs> body)
        {
            string root = TestScratch.CreateRootPath("s2-request-aware-extractor");
            try
            {
                Directory.CreateDirectory(root);
                var inputs = new Inputs { Extractor = S2RequestAwareOracleV2Extractor
                    .ForTesting(10148, 10900, 10150, 10900) };
                inputs.Raw = Path.Combine(root, "input.raw.jsonl");
                inputs.Capability = Path.Combine(root, "input.capability.json");
                inputs.Attestation = Path.Combine(root, "input.attestation.json");
                byte[] raw = SyntheticRaw(); File.WriteAllBytes(inputs.Raw, raw);
                JObject capability = Capability(raw);
                WriteAuthority(inputs, capability, raw);
                body(root, inputs);
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        private static void RefreshAuthority(Inputs input, string rawText)
        {
            byte[] raw = Encoding.UTF8.GetBytes(rawText);
            File.WriteAllBytes(input.Raw, raw);
            WriteAuthority(input, Capability(raw), raw);
        }

        private static void WriteAuthority(Inputs input, JObject capability,
            byte[] raw)
        {
            File.WriteAllText(input.Capability, capability.ToString(Formatting.None));
            var attestation = new JObject {
                ["schema"]="openggf.s2-request-aware-raw-v3-attestation.v1",
                ["raw_sha256"]=Digest(raw), ["raw_byte_count"]=raw.Length,
                ["status_count"]=1, ["fault_count"]=0, ["overflow_count"]=0,
                ["authority_id"]="s2-request-candidate-unbound",
                ["capability_sha256"]=Digest(Canonical(capability)) };
            File.WriteAllText(input.Attestation, attestation.ToString(Formatting.None));
        }

        private static byte[] SyntheticRaw()
        {
            string state = new string('0', 0x4000);
            var lines = new List<string>();
            lines.Add(Json(new JObject { ["type"]="metadata",
                ["schema"]="openggf.s2-complete-run-audio-raw.v3",
                ["rom_sha1"]=new string('a',40), ["bk2_sha256"]=new string('b',64),
                ["service_manifest_sha256"]=new string('c',64), ["first_row"]=10148,
                ["exclusive_end"]=10900, ["state_start"]=0,
                ["state_exclusive_end"]=0x2000, ["production_bound"]=false,
                ["request_manifest_schema"]="openggf.s2-preconsumption-request-manifest.v1" }));
            lines.Add(Json(new JObject { ["type"]="baseline", ["state_hex"]=state,
                ["ym_port0_latch"]=1, ["ym_port1_latch"]=2,
                ["native_arm_epoch"]=1, ["native_armed"]=true,
                ["active_services"]=new JArray(), ["pending_descendants"]=new JArray(),
                ["row"]=10148 }));
            int global = 0;
            for (int row = 10148; row < 10900; row++)
            {
                var events = new JArray();
                if (row == 10148) events.Add(Event(1, 3, 0, 0x22));
                else if (row == 10149)
                { events.Add(Event(2, 3, 2, 0x33)); events.Add(Event(3, 8, 0, 0)); }
                else events.Add(Event((uint)(100 + row), 3, 1, 0x44));
                var transfers = new JArray();
                if (row == 10150)
                {
                    events.Add(Event(699, 3, 0, 0x99));
                    events.Add(Event(700, 10, 24, 3, "16715808"));
                    transfers.Add(Transfer(row, 0, global++, 0xB5, 3, 700,
                        "16715808"));
                }
                if (row == 10151)
                {
                    events.Add(Event(701, 10, 24, 3, "20"));
                    events.Add(Event(702, 10, 24, 3, "21"));
                    transfers.Add(Transfer(row, 0, global++, 1, 0, 701, "20"));
                    transfers.Add(Transfer(row, 1, global++, 2, 1, 702, "21"));
                }
                lines.Add(Json(new JObject { ["type"]="frame", ["row"]=row,
                    ["lag"]=false, ["state_hex"]=state, ["events"]=events,
                    ["override_resume"]=JValue.CreateNull(), ["pcm"]=JValue.CreateNull(),
                    ["request_transfers"]=transfers }));
            }
            lines.Add(Json(new JObject { ["type"]="cutoff", ["state_hex"]=state,
                ["ym_port0_latch"]=0x22, ["ym_port1_latch"]=0x33,
                ["native_arm_epoch"]=1, ["native_armed"]=true,
                ["active_services"]=new JArray(), ["pending_descendants"]=new JArray(),
                ["exclusive_end"]=10900 }));
            return Encoding.UTF8.GetBytes(string.Join("\n", lines) + "\n");
        }

        private static JObject Capability(byte[] raw)
        {
            string[] lines = Encoding.UTF8.GetString(raw).Split(new[] { '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            long baseCount=0, allCount=0, markerCount=0, requestCount=0;
            var baseBytes=new List<byte>();
            var allBytes=new List<byte>();
            var markerBytes=new List<byte>();
            var requestBytes=new List<byte>();
            JObject cutoff=null;
            foreach(string line in lines)
            {
                JObject record=JObject.Parse(line);
                if((string)record["type"]=="frame")
                {
                    foreach(JToken token in (JArray)record["events"])
                    {
                        JObject evt=(JObject)token; byte[] bytes=Canonical(evt);
                        allBytes.AddRange(bytes);allCount++;
                        bool marker=(int)evt["kind"]==10&&(int)evt["value"]==3
                            &&(int)evt["pc"]==0x10D6&&(int)evt["subject"]==24;
                        if(marker){markerBytes.AddRange(bytes);markerCount++;}
                        else {baseBytes.AddRange(bytes);baseCount++;}
                    }
                    foreach(JToken transfer in (JArray)record["request_transfers"])
                    { requestBytes.AddRange(Canonical(transfer));requestCount++; }
                }
                else if((string)record["type"]=="cutoff")cutoff=record;
            }
            return new JObject { ["schema"]="openggf.s2-request-aware-raw-v3-capability.v1",
                ["production_bound"]=false, ["producer"]="s2-complete-audio-request-candidate",
                ["rom_sha1"]=new string('a',40), ["bk2_sha256"]=new string('b',64),
                ["service_manifest_sha256"]=new string('c',64),
                ["candidate_manifest_sha256"]=new string('d',64),
                ["harness_executable_sha256"]=new string('e',64),
                ["first_row"]=10148, ["exclusive_end"]=10900,
                ["window_first_row"]=10150, ["window_exclusive_end"]=10900,
                ["base_event_count"]=baseCount, ["all_event_count"]=allCount,
                ["marker_event_count"]=markerCount, ["request_count"]=requestCount,
                ["base_event_sha256"]=Digest(baseBytes.ToArray()),
                ["all_event_sha256"]=Digest(allBytes.ToArray()),
                ["marker_event_sha256"]=Digest(markerBytes.ToArray()),
                ["request_sha256"]=Digest(requestBytes.ToArray()),
                ["max_request_occupancy"]=2,
                ["cutoff_frontier_sha256"]=Digest(Canonical(cutoff)),
                ["terminal_state_sha256"]=Digest(Encoding.UTF8.GetBytes(new string('0',0x4000))) };
        }

        private static JObject Event(uint ordinal, int kind, int subject, int value,
            string payload = "0")
        { return new JObject { ["ordinal"]=ordinal, ["service_token"]=0,
            ["parent_token"]=0, ["pc"]=kind==10?0x10D6:0x100,
            ["subject"]=subject, ["offset"]=0, ["kind"]=kind,
            ["service_kind"]=0, ["depth"]=0, ["source_cpu"]=kind==10?2:1,
            ["payload_length"]=kind==10?4:0, ["value"]=value,
            ["flags"]=0, ["reserved"]=0, ["payload"]=payload }; }
        private static JObject Transfer(int row,int order,int global,int request,int slot,
            int nativeOrdinal,string a7)
        { return new JObject { ["row"]=row, ["order"]=order,
            ["global_transfer_ordinal"]=global, ["request"]=request,["slot"]=slot,
            ["pc"]=0x10D6,["a7"]=a7,["native_ordinal"]=nativeOrdinal,
            ["source_cpu"]=2,["service_token"]=0,["service_kind"]=0,["depth"]=0,
            ["active_service_owner"]=new JObject { ["token"]=0,["kind"]=0,["depth"]=0 } }; }
        private static string Json(JObject value) { return value.ToString(Formatting.None); }
        private static byte[] Canonical(JToken value)
        { return Encoding.UTF8.GetBytes(value.ToString(Formatting.None)+"\n"); }
        private static string Digest(byte[] bytes)
        { using(SHA256 hash=SHA256.Create())return Hex(hash.ComputeHash(bytes)); }
        private static string Hex(byte[] bytes)
        { var value=new StringBuilder(bytes.Length*2);foreach(byte b in bytes)value.Append(b.ToString("x2"));return value.ToString(); }
    }
}
