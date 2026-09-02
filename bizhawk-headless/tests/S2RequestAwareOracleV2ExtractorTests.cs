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
                "S2RequestAwareOracleV2ExtractorTests accept the committed unbound candidate template",
                AcceptCommittedUnboundCandidateTemplate));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests project the exact 750-row window deterministically",
                ProjectsExactWindowDeterministically));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests publish a self-verifying bounded-v2 closure",
                PublishesSelfVerifyingBoundedClosure));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests reject malformed candidate authority before output",
                RejectMalformedCandidateAuthority));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests expose no production or CLI entry",
                ExposesNoProductionOrCliEntry));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests reject unknown raw fields and marker disagreement",
                RejectUnknownRawFieldsAndMarkerDisagreement));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests reject non-contiguous native ordinals",
                RejectNonContiguousNativeOrdinals));
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
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests reject a second JSON value on one raw line",
                RejectsSecondJsonValueOnRawLine));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests accept bytes from the closed raw-v3 producer",
                AcceptsBytesFromClosedRawV3Producer));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests accept the reviewed kind-3 request topology",
                AcceptsReviewedKind3RequestTopology));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests accept the reviewed nested kind-3 request topology",
                AcceptsReviewedNestedKind3RequestTopology));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests reject every other request topology",
                RejectsEveryOtherRequestTopology));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests accept producer following-row PCM",
                AcceptsProducerFollowingRowPcm));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests reject native ABI and lifecycle mutations",
                RejectsNativeAbiAndLifecycleMutations));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests reject Z80 evidence while native proofs are disarmed",
                RejectsZ80EvidenceWhileNativeProofsAreDisarmed));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests require the exact native upload re-arm proof",
                RequiresExactNativeUploadRearmProof));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests preserve armed unarmed and repeated-cycle cutoff state",
                PreservesArmStateAcrossCutoffsAndRepeatedCycles));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests reject native continuation overflow across rows",
                RejectsNativeContinuationOverflowAcrossRows));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests reject exact ABI-4 kind and field mutations",
                RejectsExactAbi4KindAndFieldMutations));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests reject override and PCM mutations",
                RejectsOverrideAndPcmMutations));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests reject every static identity mutation",
                RejectsEveryStaticIdentityMutation));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests reject noncanonical JSONL byte forms",
                RejectsNoncanonicalJsonlByteForms));
            tests.Add(new TestMain.TestCase(
                "S2RequestAwareOracleV2ExtractorTests reject nonstandard authority JSON",
                RejectsNonstandardAuthorityJson));
        }

        private static void DefinesClosedContract()
        {
            AssertEx.Equal("openggf.s2-oracle-audio-raw.v2",
                S2RequestAwareOracleV2Extractor.OracleSchema);
        }

        private static void AcceptCommittedUnboundCandidateTemplate()
        {
            string path=Path.GetFullPath(Path.Combine(EndToEndTests.ToolDirectory,
                "fixtures", "gpgx-audio-capability-s2-request-v3.template.json"));
            JObject template=JObject.Parse(File.ReadAllText(path));
            AssertEx.Equal(Digest(File.ReadAllBytes(Fixture(
                "gpgx-audio-service-manifest-s2-request-v3.json"))),
                (string)template["candidate_manifest_sha256"]);
            AssertEx.Equal(Digest(File.ReadAllBytes(Path.Combine(
                EndToEndTests.ToolDirectory,"native",
                "gpgx-audio-observer-candidates",
                "0001-s2-request-successor-ordinal.patch"))),
                (string)template["candidate_patch_sha256"]);
            AssertEx.Equal(Digest(File.ReadAllBytes(Path.Combine(
                EndToEndTests.ToolDirectory,"native",
                "gpgx-audio-observer-candidates",
                "s2-request-selftest-recipe.json"))),
                (string)template["candidate_recipe_sha256"]);
            AssertEx.Equal(Digest(File.ReadAllBytes(Path.Combine(
                EndToEndTests.ToolDirectory,"src","Recording",
                "S2PreconsumptionRequestProfile.cs"))),
                (string)template["candidate_profile_sha256"]);
            S2RequestAwareOracleV2Extractor.ValidateCandidateTemplateForTesting(path);
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

        private static void PublishesSelfVerifyingBoundedClosure()
        {
            string root = TestScratch.CreateRootPath("s2-request-aware-bounded-closure");
            try
            {
                Directory.CreateDirectory(root);
                string[] rawLines = Encoding.UTF8.GetString(ProducerRaw()).Split(
                    new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                JObject boundedFrame = JObject.Parse(rawLines[3]);
                boundedFrame["state_hex"] = "01" + new string('0', 0x3FFE);
                rawLines[3] = Json(boundedFrame);
                byte[] raw = Encoding.UTF8.GetBytes(string.Join("\n", rawLines) + "\n");
                var input = new Inputs
                {
                    Extractor = S2RequestAwareOracleV2Extractor.ForTesting(
                        1, 4, 2, 3,
                        Fixture("gpgx-audio-service-manifests-v1.json")),
                    Raw = Path.Combine(root, "producer.raw.jsonl"),
                    Capability = Path.Combine(root, "producer.capability.json"),
                    Attestation = Path.Combine(root, "producer.attestation.json")
                };
                File.WriteAllBytes(input.Raw, raw);
                WriteAuthority(input, Capability(raw, 2, 3), raw);
                string output = Path.Combine(root, "window.jsonl");

                input.Extractor.ExtractForTesting(input.Raw, input.Capability,
                    input.Attestation, output);

                string[] lines = File.ReadAllText(output).Split(new[] { '\n' },
                    StringSplitOptions.RemoveEmptyEntries);
                AssertEx.Equal(4, lines.Length);
                JObject metadata = JObject.Parse(lines[0]);
                AssertEx.Equal(false, metadata.ContainsKey("source_raw_sha256"));
                AssertEx.Equal(false, metadata.ContainsKey("source_raw_byte_count"));
                AssertEx.Equal(false, metadata.ContainsKey("source_capability_sha256"));
                JObject domains = (JObject)metadata["digest_domains"];
                AssertEx.Equal("compact-json-lf-v1", (string)domains["inventories"]);
                AssertEx.Equal("bounded-jsonl-body-bytes-v1", (string)domains["body"]);
                AssertEx.Equal("decoded-z80-state-bytes-v1",
                    (string)domains["terminal_state"]);
                AssertEx.Equal("bounded-jsonl-before-cutoff-bytes-v1",
                    (string)domains["payload_before_cutoff"]);

                JObject baseline = JObject.Parse(lines[1]);
                JObject frame = JObject.Parse(lines[2]);
                JObject cutoff = JObject.Parse(lines[3]);
                AssertEx.Equal(false, cutoff.ContainsKey("source_cutoff_frontier_sha256"));
                AssertEx.Equal(false, cutoff.ContainsKey("terminal_state_sha256_source"));
                AssertEx.Equal(false, cutoff.ContainsKey("source_terminal_state_sha256"));

                var baseEvents = new List<byte>();
                var allEvents = new List<byte>();
                var markerEvents = new List<byte>();
                long baseCount = 0, markerCount = 0;
                foreach (JToken token in (JArray)frame["events"])
                {
                    JObject value = (JObject)token;
                    byte[] canonical = Canonical(value);
                    allEvents.AddRange(canonical);
                    bool marker = (int)value["kind"] == 10
                        && (int)value["value"] == 3
                        && (int)value["pc"] == 0x10D6
                        && (int)value["subject"] == 24;
                    if (marker)
                    {
                        AssertEx.Equal(4, (int)value["payload_length"]);
                        AssertEx.Equal(0, (int)value["offset"]);
                        AssertEx.Equal(0, (int)value["flags"]);
                        AssertEx.Equal(0, (int)value["reserved"]);
                        markerEvents.AddRange(canonical);
                        markerCount++;
                    }
                    else { baseEvents.AddRange(canonical); baseCount++; }
                }
                var transfers = new List<byte>();
                foreach (JToken token in (JArray)frame["request_transfers"])
                    transfers.AddRange(Canonical(token));
                byte[] body = Concat(Canonical(baseline), Canonical(frame));
                AssertEx.Equal(1L, (long)cutoff["frame_count"]);
                AssertEx.Equal(baseCount, (long)cutoff["base_event_count"]);
                AssertEx.Equal((long)((JArray)frame["events"]).Count,
                    (long)cutoff["all_event_count"]);
                AssertEx.Equal(markerCount, (long)cutoff["marker_event_count"]);
                AssertEx.Equal((long)((JArray)frame["request_transfers"]).Count,
                    (long)cutoff["request_transfer_count"]);
                AssertEx.Equal(1L, (long)cutoff["override_resume_count"]);
                AssertEx.Equal(1L, (long)cutoff["pcm_count"]);
                AssertEx.Equal(1, (int)cutoff["max_request_occupancy"]);
                AssertEx.Equal(Digest(baseEvents.ToArray()),
                    (string)cutoff["base_event_sha256"]);
                AssertEx.Equal(Digest(allEvents.ToArray()),
                    (string)cutoff["all_event_sha256"]);
                AssertEx.Equal(Digest(markerEvents.ToArray()),
                    (string)cutoff["marker_event_sha256"]);
                AssertEx.Equal(Digest(transfers.ToArray()),
                    (string)cutoff["request_transfer_sha256"]);
                AssertEx.Equal(Digest(Canonical(frame["override_resume"])),
                    (string)cutoff["override_resume_sha256"]);
                AssertEx.Equal(Digest(Canonical(frame["pcm"])),
                    (string)cutoff["pcm_sha256"]);
                AssertEx.Equal((long)body.Length, (long)cutoff["body_byte_count"]);
                AssertEx.Equal(Digest(body), (string)cutoff["body_sha256"]);
                AssertEx.Equal(Digest(StateBytes((string)frame["state_hex"])),
                    (string)cutoff["terminal_state_sha256"]);
                AssertEx.Equal(Digest(Concat(Canonical(metadata), body)),
                    (string)cutoff["payload_before_cutoff_sha256"]);
            }
            finally { try { Directory.Delete(root, true); } catch { } }
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
                    "inventory");
                AssertEx.Equal(false, File.Exists(Path.Combine(root, "out.jsonl")));
                byte[] raw=SyntheticRaw();
                capability=Capability(raw);
                capability["override_resume_sha256"]=new string('0',64);
                WriteAuthority(input,capability,raw);
                AssertEx.Throws<InvalidDataException>(()=>input.Extractor
                    .ExtractForTesting(input.Raw,input.Capability,
                        input.Attestation,Path.Combine(root,"resume.jsonl")),
                    "override/PCM inventory");
                capability=Capability(raw);
                capability["pcm_sha256"]=new string('0',64);
                WriteAuthority(input,capability,raw);
                AssertEx.Throws<InvalidDataException>(()=>input.Extractor
                    .ExtractForTesting(input.Raw,input.Capability,
                        input.Attestation,Path.Combine(root,"pcm.jsonl")),
                    "override/PCM inventory");
            });
        }

        private static void ExposesNoProductionOrCliEntry()
        {
            string source = File.ReadAllText(Path.Combine(EndToEndTests.ToolDirectory,
                "src", "Recording", "S2RequestAwareOracleV2Extractor.cs"));
            AssertEx.Equal(false, source.Contains("ExtractProduction"));
            AssertEx.Equal(false, source.Contains("ExtractProduction("));
            string program = File.ReadAllText(Path.Combine(EndToEndTests.ToolDirectory,
                "src", "Program.cs"));
            AssertEx.Equal(false, program.Contains("S2RequestAwareOracleV2Extractor"));
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
                ((JArray)markerFrame["events"])[0]["source_cpu"] = 1;
                lines[4] = markerFrame.ToString(Formatting.None);
                RefreshAuthority(input, string.Join("\n", lines) + "\n");
                AssertEx.Throws<InvalidDataException>(() => input.Extractor
                    .ExtractForTesting(input.Raw, input.Capability, input.Attestation,
                        Path.Combine(root, "near-marker.jsonl")), "candidate");
            });
        }

        private static void RejectNonContiguousNativeOrdinals()
        {
            WithSyntheticInputs((root,input) => AssertRawRejection(input,root,lines =>
            {
                JObject frame=JObject.Parse(lines[5]);
                ((JArray)frame["events"])[1]["ordinal"]=8;
                lines[5]=Json(frame);
            },"native ordinal"));
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
                        Path.Combine(root, "attestation.jsonl")), "follow cutoff");
            });
        }

        private static void KeepsRawPathLineStreamed()
        {
            string source = File.ReadAllText(Path.Combine(EndToEndTests.ToolDirectory,
                "src", "Recording", "S2RequestAwareOracleV2Extractor.cs"));
            AssertEx.Equal(false, source.Contains("File.ReadAllBytes(rawPath)"));
            AssertEx.Equal(false, source.Contains("ReadToEnd()"));
            AssertEx.Equal(true, source.Contains("new StreamReader(rawInput"));
            AssertEx.Equal(true, source.Contains(".s2-request-window-"));
            AssertEx.Equal(true, source.Contains(
                "byte[] serviceManifestBytes=VerifiedBytes(serviceManifestPath"));
            AssertEx.Equal(true, source.Contains(
                "LoadS2RequestCandidate(\n                    serviceManifestBytes"));
            AssertEx.Equal(false, source.Contains(
                "LoadS2RequestCandidate(\n                    serviceManifestPath"));
        }

        private static void RejectsSecondJsonValueOnRawLine()
        {
            WithSyntheticInputs((root, input) =>
            {
                string[] lines = Encoding.UTF8.GetString(SyntheticRaw()).Split(
                    new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                lines[2] += " {}";
                byte[] malformed = Encoding.UTF8.GetBytes(
                    string.Join("\n", lines) + "\n");
                File.WriteAllBytes(input.Raw, malformed);
                WriteAuthority(input, Capability(SyntheticRaw()), malformed);
                AssertEx.Throws<InvalidDataException>(() => input.Extractor
                    .ExtractForTesting(input.Raw, input.Capability,
                        input.Attestation, Path.Combine(root, "second.jsonl")),
                    "strict JSON");
            });
        }

        private static void AcceptsBytesFromClosedRawV3Producer()
        {
            string root = TestScratch.CreateRootPath(
                "s2-request-aware-producer-extractor");
            try
            {
                Directory.CreateDirectory(root);
                byte[] raw = ProducerRaw();
                var input = new Inputs
                {
                    Extractor = S2RequestAwareOracleV2Extractor.ForTesting(
                        1, 4, 2, 3,
                        Fixture("gpgx-audio-service-manifests-v1.json")),
                    Raw = Path.Combine(root, "producer.raw.jsonl"),
                    Capability = Path.Combine(root, "producer.capability.json"),
                    Attestation = Path.Combine(root, "producer.attestation.json")
                };
                File.WriteAllBytes(input.Raw, raw);
                WriteAuthority(input, Capability(raw, 2, 3), raw);
                string output = Path.Combine(root, "window.jsonl");

                input.Extractor.ExtractForTesting(input.Raw, input.Capability,
                    input.Attestation, output);

                AssertEx.Equal(true, File.Exists(output));
                string[] records = File.ReadAllText(input.Raw).Split(
                    new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                AssertEx.Equal(1,((JArray)JObject.Parse(records[1])
                    ["active_services"]).Count);
                AssertEx.Equal(2,((JArray)JObject.Parse(records[1])
                    ["pending_descendants"]).Count);
                JObject frontierWrite=(JObject)((JArray)((JArray)
                    JObject.Parse(records[1])["active_services"])[0]
                    ["chips"])[0];
                AssertEx.Equal(true,frontierWrite.ContainsKey("coordinate"));
                JObject resume = (JObject)JObject.Parse(records[3])
                    ["override_resume"];
                JObject write = (JObject)((JArray)resume["writes"])[0];
                AssertEx.Equal(false, write.ContainsKey("coordinate"));
                AssertEx.Equal(1, ((JArray)JObject.Parse(records[5])
                    ["active_services"]).Count);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        private static void AcceptsReviewedKind3RequestTopology()
        {
            byte[] raw=ProducerRaw(false,true,true,false,true);
            string root=TestScratch.CreateRootPath(
                "s2-request-aware-kind3-extractor");
            try
            {
                Directory.CreateDirectory(root);
                var input=new Inputs {
                    Extractor=S2RequestAwareOracleV2Extractor.ForTesting(
                        1,4,2,3,
                        Fixture("gpgx-audio-service-manifests-v1.json")),
                    Raw=Path.Combine(root,"kind3.raw.jsonl"),
                    Capability=Path.Combine(root,"kind3.capability.json"),
                    Attestation=Path.Combine(root,"kind3.attestation.json") };
                File.WriteAllBytes(input.Raw,raw);
                WriteAuthority(input,Capability(raw,2,3),raw);
                string output=Path.Combine(root,"window.jsonl");
                input.Extractor.ExtractForTesting(input.Raw,input.Capability,
                    input.Attestation,output);
                AssertEx.Equal(true,File.Exists(output));
            }
            finally { try { Directory.Delete(root,true); } catch { } }
        }

        private static void AcceptsReviewedNestedKind3RequestTopology()
        {
            byte[] raw=ProducerRaw(false,true,true,false,false,true);
            string root=TestScratch.CreateRootPath(
                "s2-request-aware-nested-kind3-extractor");
            try
            {
                Directory.CreateDirectory(root);
                var input=new Inputs {
                    Extractor=S2RequestAwareOracleV2Extractor.ForTesting(
                        1,4,2,3,
                        Fixture("gpgx-audio-service-manifests-v1.json")),
                    Raw=Path.Combine(root,"nested-kind3.raw.jsonl"),
                    Capability=Path.Combine(root,"nested-kind3.capability.json"),
                    Attestation=Path.Combine(root,"nested-kind3.attestation.json") };
                File.WriteAllBytes(input.Raw,raw);
                WriteAuthority(input,Capability(raw,2,3),raw);
                string output=Path.Combine(root,"window.jsonl");
                input.Extractor.ExtractForTesting(input.Raw,input.Capability,
                    input.Attestation,output);
                AssertEx.Equal(true,File.Exists(output));
            }
            finally { try { Directory.Delete(root,true); } catch { } }
        }

        private static void RejectsEveryOtherRequestTopology()
        {
            byte[] kind3=ProducerRaw(false,true,true,false,true);
            AssertProducerRawRejection(kind3,lines=>MutateRequest(lines,
                (marker,transfer)=>marker["subject"]=
                    S2PreconsumptionRequestObserver.MarkerToken),"topology");
            AssertProducerRawRejection(kind3,lines=>MutateRequest(lines,
                (marker,transfer)=>marker["parent_token"]=1),"topology");
            AssertProducerRawRejection(kind3,lines=>MutateRequest(lines,
                (marker,transfer)=>SetOwner(marker,transfer,4,2,0)),"topology");
            AssertProducerRawRejection(kind3,lines=>MutateRequest(lines,
                (marker,transfer)=>SetOwner(marker,transfer,4,3,1)),"topology");
            AssertProducerRawRejection(kind3,lines=>MutateRequest(lines,
                (marker,transfer)=>SetOwner(marker,transfer,0,0,0)),"topology");
            AssertProducerRawRejection(kind3,lines=>MutateRequest(lines,
                (marker,transfer)=>transfer["service_token"]=5),"owner");
            AssertProducerRawRejection(kind3,lines=>MutateRequest(lines,
                (marker,transfer)=>marker["subject"]=26),"candidate");

            byte[] root=ProducerRaw();
            AssertProducerRawRejection(root,lines=>MutateRequest(lines,
                (marker,transfer)=>marker["subject"]=
                    S2PreconsumptionRequestObserver.Kind3MarkerToken),
                "S2 request-aware extractor");
            AssertProducerRawRejection(root,lines=>MutateRequest(lines,
                (marker,transfer)=>SetOwner(marker,transfer,4,3,0)),
                "S2 request-aware extractor");

            byte[] nested=ProducerRaw(false,true,true,false,false,true);
            AssertProducerRawRejection(nested,lines=>MutateRequest(lines,
                (marker,transfer)=>marker["subject"]=
                    S2PreconsumptionRequestObserver.MarkerToken),
                "S2 request-aware extractor");
            AssertProducerRawRejection(nested,lines=>MutateRequest(lines,
                (marker,transfer)=>marker["parent_token"]=0),
                "S2 request-aware extractor");
            AssertProducerRawRejection(nested,lines=>MutateRequest(lines,
                (marker,transfer)=>marker["parent_token"]=5),
                "S2 request-aware extractor");
            AssertProducerRawRejection(nested,lines=>MutateRequest(lines,
                (marker,transfer)=>marker["parent_token"]=6),
                "S2 request-aware extractor");
            AssertProducerRawRejection(nested,lines=>MutateRequest(lines,
                (marker,transfer)=>SetOwner(marker,transfer,0,3,1)),
                "S2 request-aware extractor");
            AssertProducerRawRejection(nested,lines=>MutateRequest(lines,
                (marker,transfer)=>SetOwner(marker,transfer,4,3,1)),
                "S2 request-aware extractor");
            AssertProducerRawRejection(nested,lines=>MutateRequest(lines,
                (marker,transfer)=>SetOwner(marker,transfer,5,4,1)),
                "S2 request-aware extractor");
            AssertProducerRawRejection(nested,lines=>MutateRequest(lines,
                (marker,transfer)=>SetOwner(marker,transfer,5,3,0)),
                "S2 request-aware extractor");
            AssertProducerRawRejection(nested,lines=>MutateRequest(lines,
                (marker,transfer)=>SetOwner(marker,transfer,5,3,2)),
                "S2 request-aware extractor");
            AssertProducerRawRejection(nested,MutateNestedRootKind,
                "S2 request-aware extractor");
        }

        private static void MutateNestedRootKind(string[] lines)
        {
            for(int lineIndex=0;lineIndex<lines.Length;lineIndex++)
            {
                JObject frame=JObject.Parse(lines[lineIndex]);
                if((string)frame["type"]!="frame")continue;
                JArray events=(JArray)frame["events"];
                bool containsMarker=false;
                foreach(JToken token in events)
                {
                    JObject value=(JObject)token;
                    if((int)value["kind"]==10
                        &&(int)value["pc"]==0x10D6)
                        containsMarker=true;
                }
                if(!containsMarker)continue;
                foreach(JToken token in events)
                {
                    JObject value=(JObject)token;
                    if((int)value["kind"]!=1||(int)value["subject"]!=5)
                        continue;
                    value["service_kind"]=3;
                    lines[lineIndex]=Json(frame);
                    return;
                }
            }
            throw new InvalidOperationException("missing nested root begin");
        }

        private static void MutateRequest(string[] lines,
            Action<JObject,JObject> mutate)
        {
            for(int lineIndex=0;lineIndex<lines.Length;lineIndex++)
            {
                JObject frame=JObject.Parse(lines[lineIndex]);
                if((string)frame["type"]!="frame")continue;
                JArray events=(JArray)frame["events"];
                foreach(JToken token in events)
                {
                    JObject marker=(JObject)token;
                    if((int)marker["kind"]!=10
                        ||(int)marker["pc"]!=0x10D6)continue;
                    JObject transfer=(JObject)((JArray)
                        frame["request_transfers"])[0];
                    mutate(marker,transfer);
                    lines[lineIndex]=Json(frame);
                    return;
                }
            }
            throw new InvalidOperationException("missing request marker");
        }

        private static void SetOwner(JObject marker,JObject transfer,
            int token,int kind,int depth)
        {
            marker["service_token"]=token;
            marker["service_kind"]=kind;
            marker["depth"]=depth;
            transfer["service_token"]=token;
            transfer["service_kind"]=kind;
            transfer["depth"]=depth;
            JObject owner=(JObject)transfer["active_service_owner"];
            owner["token"]=token;owner["kind"]=kind;owner["depth"]=depth;
        }

        private static byte[] ProducerRaw()
        { return ProducerRaw(false, true); }

        private static byte[] ProducerRaw(bool followingRowPcm)
        { return ProducerRaw(followingRowPcm, true); }

        private static byte[] ProducerRaw(bool followingRowPcm,
            bool includePostResetArmProof)
        { return ProducerRaw(followingRowPcm,includePostResetArmProof,
            true,false); }

        private static byte[] ProducerRaw(bool followingRowPcm,
            bool includePostResetArmProof,bool includePostResetZ80,
            bool repeatResetAndArm,bool kind3Request=false,
            bool nestedKind3Request=false)
        {
            var api = new ProducerTraceApi();
            var host = new ProducerHost(api);
            var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            using (S2CompleteAudioCaptureRunner.RequestAwareRawV3Candidate candidate =
                S2CompleteAudioCaptureRunner
                    .OpenRequestAwareRawV3CandidateForTesting(
                        Fixture("gpgx-audio-service-manifest-s2-request-v3.json"),
                        Fixture("gpgx-audio-service-manifests-v1.json"),
                        host, output, 1, 4))
            {
                api.Events = UploadEvents();
                host.AudioAfterAdvance = new short[0];
                candidate.AdvanceRow(0, new Bk2Frame());

                api.Events = ResumeEvents();
                host.AudioAfterAdvance = new short[0];
                candidate.AdvanceRow(1, new Bk2Frame());

                const uint a7 = 0x00FF1020;
                api.Events = nestedKind3Request
                    ?RequestInsideNestedKind3Events(a7)
                    :kind3Request
                        ?RequestInsideRootKind3Events(a7)
                        :RequestAndOpenServiceEvents(a7,
                            includePostResetArmProof,includePostResetZ80);
                api.SuccessorOrdinal = kind3Request||nestedKind3Request
                    ?FindMarkerOrdinal(api.Events):19;
                host.Set("M68K D0", 0xB5);
                host.Set("M68K D1", 3);
                host.Set("M68K A7", a7);
                host.ExecuteCallbackOnAdvance = true;
                host.AudioAfterAdvance = followingRowPcm
                    ?new short[0]:new short[] { 1, -2 };
                candidate.AdvanceRow(2, new Bk2Frame());

                api.Events = repeatResetAndArm
                    ?ResetAndRearmEvents()
                    :new GpgxAudioTraceEvent[0];
                host.AudioAfterAdvance = followingRowPcm
                    ?new short[] { 1, -2 }:new short[0];
                candidate.AdvanceRow(3,new Bk2Frame());
                candidate.Complete();
            }
            return Encoding.UTF8.GetBytes(output.ToString());
        }

        private static void AcceptsProducerFollowingRowPcm()
        {
            string root=TestScratch.CreateRootPath(
                "s2-request-aware-following-pcm");
            try
            {
                Directory.CreateDirectory(root);
                byte[] raw=ProducerRaw(true);
                var input=new Inputs {
                    Extractor=S2RequestAwareOracleV2Extractor.ForTesting(
                        1,4,2,3,
                        Fixture("gpgx-audio-service-manifests-v1.json")),
                    Raw=Path.Combine(root,"following.raw.jsonl"),
                    Capability=Path.Combine(root,"capability.json"),
                    Attestation=Path.Combine(root,"attestation.json") };
                File.WriteAllBytes(input.Raw,raw);
                WriteAuthority(input,Capability(raw,2,3),raw);
                input.Extractor.ExtractForTesting(input.Raw,input.Capability,
                    input.Attestation,Path.Combine(root,"out.jsonl"));
                string[] lines=Encoding.UTF8.GetString(raw).Split(
                    new[]{'\n'},StringSplitOptions.RemoveEmptyEntries);
                AssertEx.Equal(JTokenType.Null,
                    JObject.Parse(lines[3])["pcm"].Type);
                JObject pcm=(JObject)JObject.Parse(lines[4])["pcm"];
                AssertEx.Equal("following_row",(string)pcm["selection"]);
                AssertEx.Equal(1,(int)pcm["offset"]);
            }
            finally { try { Directory.Delete(root,true); } catch { } }
        }

        private static void RejectsNativeAbiAndLifecycleMutations()
        {
            AssertProducerRawRejection(lines =>
            {
                JObject frame = JObject.Parse(lines[3]);
                ((JArray)frame["events"])[0]["subject"] = 11;
                lines[3] = Json(frame);
            }, "native ABI");
            AssertProducerRawRejection(lines =>
            {
                JObject cutoff = JObject.Parse(lines[5]);
                ((JArray)cutoff["active_services"])[0]["parent_token"] = 44;
                lines[5] = Json(cutoff);
            }, "frontier");
        }

        private static void RejectsZ80EvidenceWhileNativeProofsAreDisarmed()
        {
            AssertProducerRawRejection(lines=>
            {
                JObject frame=JObject.Parse(lines[3]);
                JArray events=(JArray)frame["events"];
                int completion=ArmCompletionIndex(events);
                JToken z80=events[completion+1];
                events.RemoveAt(completion+1);
                events.Insert(20,z80);
                Renumber(events);
                lines[3]=Json(frame);
            },"native");
        }

        private static void RequiresExactNativeUploadRearmProof()
        {
            AssertProducerRawRejection(lines=>
            {
                JObject frame=JObject.Parse(lines[3]);
                JArray events=(JArray)frame["events"];
                ((JObject)events[ArmCompletionIndex(events)])["subject"]=9;
                lines[3]=Json(frame);
            },"native");
            AssertProducerRawRejection(lines=>
            {
                JObject frame=JObject.Parse(lines[3]);
                JArray events=(JArray)frame["events"];
                ((JObject)events[ArmCompletionIndex(events)])["pc"]=0x0EC034;
                lines[3]=Json(frame);
            },"native");
            AssertProducerRawRejection(lines=>
            {
                JObject frame=JObject.Parse(lines[3]);
                JArray events=(JArray)frame["events"];
                ((JObject)events[ArmCompletionIndex(events)])
                    ["service_kind"]=3;
                lines[3]=Json(frame);
            },"native");
            AssertProducerRawRejection(lines=>
            {
                JObject frame=JObject.Parse(lines[3]);
                JArray events=(JArray)frame["events"];
                ((JObject)events[ArmCompletionIndex(events)])
                    ["source_cpu"]=1;
                lines[3]=Json(frame);
            },"native");
            AssertProducerRawRejection(lines=>
            {
                JObject frame=JObject.Parse(lines[3]);
                JArray events=(JArray)frame["events"];
                int completion=ArmCompletionIndex(events);
                events.RemoveAt(completion-1);
                Renumber(events);
                lines[3]=Json(frame);
            },"native");
            AssertProducerRawRejection(lines=>
            {
                JObject frame=JObject.Parse(lines[3]);
                JArray events=(JArray)frame["events"];
                int completion=ArmCompletionIndex(events);
                JObject end=(JObject)events[completion];
                events.Insert(completion,new JObject {
                    ["ordinal"]=0,
                    ["service_token"]=(int)end["service_token"],
                    ["parent_token"]=0,["pc"]=0x0EC036,
                    ["subject"]=0,["offset"]=0,["kind"]=3,
                    ["service_kind"]=2,["depth"]=0,["source_cpu"]=2,
                    ["payload_length"]=0,["value"]=0x22,["flags"]=0,
                    ["reserved"]=0,["payload"]="0" });
                Renumber(events);
                lines[3]=Json(frame);
            },"native");
        }

        private static void PreservesArmStateAcrossCutoffsAndRepeatedCycles()
        {
            AssertProducerArmState(ProducerRaw(),3,true);
            AssertProducerArmState(ProducerRaw(false,false,false,false),
                2,false);
            AssertProducerArmState(ProducerRaw(false,true,true,true),5,true);
        }

        private static void RejectsNativeContinuationOverflowAcrossRows()
        {
            string root=TestScratch.CreateRootPath(
                "s2-request-aware-continuation-overflow");
            try
            {
                Directory.CreateDirectory(root);
                string[] produced=Encoding.UTF8.GetString(ProducerRaw()).Split(
                    new[]{'\n'},StringSplitOptions.RemoveEmptyEntries);
                JObject metadata=JObject.Parse(produced[0]);
                metadata["exclusive_end"]=8;
                var lines=new List<string>{Json(metadata),produced[1],
                    produced[2],produced[3],produced[4]};
                JObject empty=JObject.Parse(produced[4]);
                for(int row=4;row<8;row++)
                {
                    empty["row"]=row;
                    lines.Add(Json(empty));
                }
                JObject cutoff=JObject.Parse(produced[5]);
                cutoff["exclusive_end"]=8;
                lines.Add(Json(cutoff));
                byte[] raw=Encoding.UTF8.GetBytes(
                    string.Join("\n",lines)+"\n");
                var input=new Inputs {
                    Extractor=S2RequestAwareOracleV2Extractor.ForTesting(
                        1,8,2,3,
                        Fixture("gpgx-audio-service-manifests-v1.json")),
                    Raw=Path.Combine(root,"overflow.raw.jsonl"),
                    Capability=Path.Combine(root,"capability.json"),
                    Attestation=Path.Combine(root,"attestation.json") };
                File.WriteAllBytes(input.Raw,raw);
                WriteAuthority(input,Capability(raw,2,3),raw);
                AssertEx.Throws<InvalidDataException>(()=>input.Extractor
                    .ExtractForTesting(input.Raw,input.Capability,
                        input.Attestation,Path.Combine(root,"out.jsonl")),
                    "native ABI");
            }
            finally { try { Directory.Delete(root,true); } catch { } }
        }

        private static void RejectsExactAbi4KindAndFieldMutations()
        {
            foreach(string name in new[]{"service_token","parent_token",
                "subject","offset"})
                AssertProducerRawRejection(lines=>
                {
                    JObject frame=JObject.Parse(lines[3]);
                    ((JArray)frame["events"])[19][name]=65536;
                    lines[3]=Json(frame);
                },"extractor");
            foreach(string name in new[]{"value","flags","reserved"})
                AssertProducerRawRejection(lines=>
                {
                    JObject frame=JObject.Parse(lines[3]);
                    ((JArray)frame["events"])[19][name]=256;
                    lines[3]=Json(frame);
                },"extractor");
            foreach(string name in new[]{"offset","flags","reserved"})
                AssertProducerRawRejection(lines=>
                {
                    JObject frame=JObject.Parse(lines[3]);
                    ((JArray)frame["events"])[19][name]=1;
                    lines[3]=Json(frame);
                },"extractor");
            AssertProducerRawRejection(lines=>
            {
                JObject frame=JObject.Parse(lines[3]);
                ((JArray)frame["events"])[14]["subject"]=1;
                lines[3]=Json(frame);
            },"native ABI");
            AssertProducerRawRejection(lines=>
            {
                JObject frame=JObject.Parse(lines[3]);
                ((JArray)frame["events"])[18]["flags"]=1;
                lines[3]=Json(frame);
            },"native ABI");
            AssertProducerRawRejection(lines=>
            {
                JObject frame=JObject.Parse(lines[3]);
                ((JArray)frame["events"])[20]["kind"]=11;
                lines[3]=Json(frame);
            },"native ABI");
            AssertProducerRawRejection(lines=>
            {
                JObject frame=JObject.Parse(lines[3]);
                ((JArray)frame["events"])[4]["kind"]=6;
                lines[3]=Json(frame);
            },"native ABI");
            AssertProducerRawRejection(lines=>
            {
                JObject frame=JObject.Parse(lines[2]);
                ((JArray)frame["events"])[1]["parent_token"]=0;
                lines[2]=Json(frame);
            },"native ABI");
            AssertProducerRawRejection(lines=>
            {
                JObject frame=JObject.Parse(lines[2]);
                ((JArray)frame["events"])[0]["pc"]=57;
                lines[2]=Json(frame);
            },"native ABI");
            AssertProducerRawRejection(lines=>
            {
                JObject baseline=JObject.Parse(lines[1]);
                ((JArray)baseline["active_services"])[0]
                    ["current_parent_token"]=99;
                lines[1]=Json(baseline);
            },"baseline");
            AssertProducerRawRejection(lines=>
            {
                JObject baseline=JObject.Parse(lines[1]);
                ((JArray)baseline["active_services"])[0]["begin_row"]=1;
                lines[1]=Json(baseline);
            },"baseline");
            AssertProducerRawRejection(lines=>
            {
                JObject baseline=JObject.Parse(lines[1]);
                JObject pending=(JObject)((JArray)
                    baseline["pending_descendants"])[0];
                pending["end_coordinate"]=pending["begin_coordinate"];
                lines[1]=Json(baseline);
            },"baseline");
            AssertProducerRawRejection(lines=>
            {
                JObject baseline=JObject.Parse(lines[1]);
                JObject pending=(JObject)((JArray)
                    baseline["pending_descendants"])[0];
                pending["depth"]=2;pending["current_depth"]=2;
                lines[1]=Json(baseline);
            },"baseline");
            AssertProducerRawRejection(lines=>
            {
                JObject baseline=JObject.Parse(lines[1]);
                JArray chips=(JArray)((JArray)
                    baseline["active_services"])[0]["chips"];
                ((JObject)chips[1])["coordinate"]=chips[0]["coordinate"];
                lines[1]=Json(baseline);
            },"baseline");
            AssertProducerRawRejection(lines=>
            {
                JObject baseline=JObject.Parse(lines[1]);
                JArray pending=(JArray)baseline["pending_descendants"];
                JToken first=pending[0];pending[0]=pending[1];pending[1]=first;
                lines[1]=Json(baseline);
            },"pending");
            AssertProducerRawRejection(lines=>
            {
                JObject baseline=JObject.Parse(lines[1]);
                baseline["native_armed"]=false;
                lines[1]=Json(baseline);
            },"baseline");
            AssertProducerRawRejection(lines=>
            {
                JObject baseline=JObject.Parse(lines[1]);
                baseline["native_arm_epoch"]=0;
                lines[1]=Json(baseline);
                JObject cutoff=JObject.Parse(lines[5]);
                cutoff["native_arm_epoch"]=0;
                lines[5]=Json(cutoff);
            },"arm epoch");
            AssertProducerRawRejection(lines=>
            {
                JObject baseline=JObject.Parse(lines[1]);
                ((JArray)baseline["active_services"])[0]
                    ["begin_native_ordinal"]=65536;
                lines[1]=Json(baseline);
            },"baseline");
            AssertProducerRawRejection(lines=>
            {
                JObject baseline=JObject.Parse(lines[1]);
                ((JArray)((JArray)baseline["active_services"])[0]
                    ["chips"])[0]["native_ordinal"]=65536;
                lines[1]=Json(baseline);
            },"baseline");
            AssertProducerRawRejection(lines=>
            {
                JObject baseline=JObject.Parse(lines[1]);
                JObject chip=(JObject)((JArray)((JArray)
                    baseline["active_services"])[0]["chips"])[0];
                chip["native_ordinal"]=(long)chip["coordinate"]+1;
                lines[1]=Json(baseline);
            },"baseline");
            AssertProducerRawRejection(lines=>
            {
                JObject baseline=JObject.Parse(lines[1]);
                JArray chips=(JArray)((JArray)
                    baseline["active_services"])[0]["chips"];
                ((JObject)chips[0])["native_ordinal"]=
                    (long)((JObject)chips[0])["native_ordinal"]-1;
                lines[1]=Json(baseline);
            },"baseline");
            AssertProducerRawRejection(lines=>
            {
                JObject baseline=JObject.Parse(lines[1]);
                JArray chips=(JArray)((JArray)
                    baseline["active_services"])[0]["chips"];
                ((JObject)chips[1])["native_ordinal"]=
                    (long)((JObject)chips[1])["coordinate"]-1;
                lines[1]=Json(baseline);
            },"baseline");
            AssertProducerRawRejection(lines=>
            {
                JObject baseline=JObject.Parse(lines[1]);
                JObject active=(JObject)((JArray)
                    baseline["active_services"])[0];
                active["begin_native_ordinal"]=
                    (long)active["begin_coordinate"]+1;
                lines[1]=Json(baseline);
            },"baseline");
            AssertProducerRawRejection(lines=>
            {
                JObject baseline=JObject.Parse(lines[1]);
                JObject pending=(JObject)((JArray)
                    baseline["pending_descendants"])[0];
                pending["begin_native_ordinal"]=
                    (long)pending["begin_native_ordinal"]-1;
                lines[1]=Json(baseline);
            },"baseline");
            AssertProducerRawRejection(lines=>
            {
                JObject frame=JObject.Parse(lines[2]);
                string state=(string)frame["state_hex"];
                frame["state_hex"]="A"+state.Substring(1);
                lines[2]=Json(frame);
            },"state snapshot");
            AssertProducerRawRejection(lines=>
            {
                JObject baseline=JObject.Parse(lines[1]);
                JArray active=(JArray)baseline["active_services"];
                JObject child=(JObject)active[0];
                JObject parent=(JObject)child.DeepClone();
                parent["token"]=1;parent["parent_token"]=0;
                parent["kind"]=6;parent["depth"]=0;
                parent["current_parent_token"]=0;
                parent["current_depth"]=0;
                parent["begin_coordinate"]=(long)child["begin_coordinate"]-1;
                parent["begin_native_ordinal"]=
                    (long)child["begin_native_ordinal"]-1;
                parent["begin_pc"]=0;parent["begin_hook_token"]=11;
                parent["chips"]=new JArray();
                parent["snapshots"]=new JArray();
                parent["ancestry_transitions"]=new JArray();
                child["parent_token"]=1;child["depth"]=1;
                child["current_parent_token"]=1;
                child["current_depth"]=1;
                child["begin_hook_token"]=13;child["begin_pc"]=378;
                active.Insert(0,parent);lines[1]=Json(baseline);
            },"baseline");
            AssertProducerRawRejection(lines=>
            {
                JObject frame=JObject.Parse(lines[3]);
                ((JArray)frame["events"])[20]["service_token"]=2;
                lines[3]=Json(frame);
                JObject cutoff=JObject.Parse(lines[5]);
                ((JArray)cutoff["active_services"])[0]["token"]=2;
                lines[5]=Json(cutoff);
            },"native ABI");
        }

        private static void RejectsOverrideAndPcmMutations()
        {
            AssertProducerRawRejection(lines =>
            {
                JObject frame = JObject.Parse(lines[3]);
                ((JObject)frame["override_resume"])
                    ["restores_saved_priority"] = false;
                lines[3] = Json(frame);
            }, "override");
            AssertProducerRawRejection(lines =>
            {
                JObject frame = JObject.Parse(lines[3]);
                ((JObject)frame["pcm"])["sha256"] = new string('0', 64);
                lines[3] = Json(frame);
            }, "PCM");
            foreach(string name in new[]{"request","admission","request_pc",
                "pc","service_token","service_begin_ordinal","native_ordinal",
                "frame","fix_driver_bugs","restores_psg_noise"})
                AssertProducerRawRejection(lines=>
                {
                    JObject frame=JObject.Parse(lines[3]);
                    JObject resume=(JObject)frame["override_resume"];
                    if(resume[name].Type==JTokenType.String)resume[name]="wrong";
                    else if(resume[name].Type==JTokenType.Boolean)
                        resume[name]=!(bool)resume[name];
                    else resume[name]=(long)resume[name]+1;
                    lines[3]=Json(frame);
                },"extractor");
            AssertProducerRawRejection(lines=>
            {
                JObject frame=JObject.Parse(lines[3]);
                JArray writes=(JArray)frame["override_resume"]["writes"];
                JToken first=writes[0];writes[0]=writes[1];writes[1]=first;
                lines[3]=Json(frame);
            },"override");
            AssertProducerRawRejection(lines=>
            {
                JObject baseline=JObject.Parse(lines[1]);
                JObject write=(JObject)((JArray)((JArray)
                    baseline["active_services"])[0]["chips"])[0];
                write.Remove("coordinate");
                lines[1]=Json(baseline);
            },"chip");
            AssertProducerRawRejection(lines=>
            {
                JObject frame=JObject.Parse(lines[3]);
                ((JObject)((JArray)frame["override_resume"]["writes"])[0])
                    ["coordinate"]=0;
                lines[3]=Json(frame);
            },"override write");
            foreach(string name in new[]{"selection","row","offset",
                "sample_rate","channels","format","stereo_frames",
                "byte_count","pcm_hex"})
                AssertProducerRawRejection(lines=>
                {
                    JObject frame=JObject.Parse(lines[3]);
                    JObject pcm=(JObject)frame["pcm"];
                    if(name=="selection"||name=="format")pcm[name]="wrong";
                    else if(name=="pcm_hex")pcm[name]="00";
                    else pcm[name]=(long)pcm[name]+1;
                    lines[3]=Json(frame);
                },"PCM");
            AssertProducerRawRejection(ProducerRaw(true),lines=>
            {
                JObject frame=JObject.Parse(lines[4]);
                ((JObject)frame["pcm"])["selection"]="service_frame";
                lines[4]=Json(frame);
            },"PCM selection");
            AssertProducerRawRejection(ProducerRaw(true),lines=>
            {
                JObject frame=JObject.Parse(lines[4]);
                frame["pcm"]=JValue.CreateNull();
                lines[4]=Json(frame);
            },"extractor");
        }

        private static void RejectsEveryStaticIdentityMutation()
        {
            WithSyntheticInputs((root,input) =>
            {
                byte[] raw=SyntheticRaw();
                foreach(string name in new[]{"rom_sha1","bk2_sha256",
                    "service_manifest_sha256","candidate_manifest_sha256",
                    "candidate_patch_sha256","candidate_recipe_sha256",
                    "candidate_profile_sha256","harness_executable_sha256"})
                {
                    JObject capability=Capability(raw);
                    string value=(string)capability[name];
                    capability[name]=(value[0]=='0'?'1':'0')+value.Substring(1);
                    WriteAuthority(input,capability,raw);
                    AssertEx.Throws<InvalidDataException>(()=>input.Extractor
                        .ExtractForTesting(input.Raw,input.Capability,
                            input.Attestation,Path.Combine(root,name+".jsonl")),
                        "identity");
                }
            });
        }

        private static void RejectsNoncanonicalJsonlByteForms()
        {
            byte[] canonical=SyntheticRaw();
            string text=Encoding.UTF8.GetString(canonical);
            foreach(string suffix in new[]{" {}"," []"," 0"," //hidden"})
            {
                string[] lines=text.Split(new[]{'\n'},
                    StringSplitOptions.RemoveEmptyEntries);
                lines[2]+=suffix;
                AssertMalformedRaw(Encoding.UTF8.GetBytes(
                    string.Join("\n",lines)+"\n"),"extractor");
            }
            AssertMalformedRaw(Encoding.UTF8.GetBytes(text.Replace(
                "\"type\":\"frame\",\"row\":10148",
                "\"type\":\"frame\",/*hidden*/\"row\":10148")),
                "extractor");
            string[] trailingComma=text.Split(new[]{'\n'},
                StringSplitOptions.RemoveEmptyEntries);
            trailingComma[2]=trailingComma[2].Substring(0,
                trailingComma[2].Length-1)+",}";
            AssertMalformedRaw(Encoding.UTF8.GetBytes(
                string.Join("\n",trailingComma)+"\n"),"extractor");
            foreach(string noncanonical in new[]{
                " "+trailingComma[2].Substring(0,
                    trailingComma[2].Length-2)+"}",
                trailingComma[2].Substring(0,
                    trailingComma[2].Length-2).Replace("\"type\"","'type'")+"}",
                trailingComma[2].Substring(0,
                    trailingComma[2].Length-2).Replace(
                        "\"row\":10148","\"row\":0x27a4")+"}"})
            {
                string[] lines=text.Split(new[]{'\n'},
                    StringSplitOptions.RemoveEmptyEntries);
                lines[2]=noncanonical;
                AssertMalformedRaw(Encoding.UTF8.GetBytes(
                    string.Join("\n",lines)+"\n"),"extractor");
            }
            AssertMalformedRaw(Encoding.UTF8.GetBytes(text.Replace(
                "\"type\":\"metadata\"",
                "\"type\":\"metadata\",\"type\":\"metadata\"")),
                "extractor");
            var bom=new byte[canonical.Length+3];
            bom[0]=0xEF;bom[1]=0xBB;bom[2]=0xBF;
            Buffer.BlockCopy(canonical,0,bom,3,canonical.Length);
            AssertMalformedRaw(bom,"extractor");
            AssertMalformedRaw(Encoding.UTF8.GetBytes(text.Replace(
                "\n","\r\n")),"extractor");
            var missingLf=new byte[canonical.Length-1];
            Buffer.BlockCopy(canonical,0,missingLf,0,missingLf.Length);
            AssertMalformedRaw(missingLf,"extractor");
        }

        private static void AssertMalformedRaw(byte[] malformed,string message)
        {
            string root=TestScratch.CreateRootPath(
                "s2-request-aware-byte-form");
            try
            {
                Directory.CreateDirectory(root);
                var input=new Inputs {
                    Extractor=S2RequestAwareOracleV2Extractor.ForTesting(
                        10148,10900,10150,10900,
                        Fixture("gpgx-audio-service-manifests-v1.json")),
                    Raw=Path.Combine(root,"malformed.raw.jsonl"),
                    Capability=Path.Combine(root,"capability.json"),
                    Attestation=Path.Combine(root,"attestation.json") };
                File.WriteAllBytes(input.Raw,malformed);
                WriteAuthority(input,Capability(SyntheticRaw()),malformed);
                string output=Path.Combine(root,"out.jsonl");
                AssertEx.Throws<InvalidDataException>(()=>input.Extractor
                    .ExtractForTesting(input.Raw,input.Capability,
                        input.Attestation,output),message);
                AssertEx.Equal(false,File.Exists(output));
            }
            finally { try { Directory.Delete(root,true); } catch { } }
        }

        private static void RejectsNonstandardAuthorityJson()
        {
            WithSyntheticInputs((root,input)=>
            {
                string capability=File.ReadAllText(input.Capability);
                string attestation=File.ReadAllText(input.Attestation);
                foreach(Func<string,string> mutate in new Func<string,string>[] {
                    InsertJsonComment,
                    value=>value.Substring(0,value.Length-1)+",}",
                    value=>value.Replace("\"schema\"","'schema'") })
                {
                    File.WriteAllText(input.Capability,mutate(capability));
                    AssertEx.Throws<InvalidDataException>(()=>input.Extractor
                        .ExtractForTesting(input.Raw,input.Capability,
                            input.Attestation,Path.Combine(root,
                                Guid.NewGuid().ToString("N")+".jsonl")),
                        "capability");
                    File.WriteAllText(input.Capability,capability);

                    File.WriteAllText(input.Attestation,mutate(attestation));
                    AssertEx.Throws<InvalidDataException>(()=>input.Extractor
                        .ExtractForTesting(input.Raw,input.Capability,
                            input.Attestation,Path.Combine(root,
                                Guid.NewGuid().ToString("N")+".jsonl")),
                        "attestation");
                    File.WriteAllText(input.Attestation,attestation);
                }
            });
        }

        private static string InsertJsonComment(string value)
        {
            int comma=value.IndexOf(",\"",StringComparison.Ordinal);
            if(comma<0)throw new InvalidOperationException(
                "authority fixture has no second property");
            return value.Substring(0,comma+1)+"/*hidden*/"
                +value.Substring(comma+1);
        }

        private static void AssertProducerRawRejection(
            Action<string[]> mutate, string message)
        { AssertProducerRawRejection(ProducerRaw(),mutate,message); }

        private static int ArmCompletionIndex(JArray events)
        {
            int found=-1;
            for(int index=0;index<events.Count;index++)
            {
                JObject value=(JObject)events[index];
                if((int)value["kind"]!=2||(int)value["subject"]!=10
                    ||(int)value["pc"]!=0x0EC036
                    ||(int)value["service_kind"]!=2)continue;
                if(found>=0)throw new InvalidOperationException(
                    "duplicate test upload completion");
                found=index;
            }
            if(found<0)throw new InvalidOperationException(
                "missing test upload completion");
            return found;
        }

        private static void Renumber(JArray events)
        {
            for(int index=0;index<events.Count;index++)
                ((JObject)events[index])["ordinal"]=index;
        }

        private static void AssertProducerArmState(byte[] raw,long epoch,
            bool armed)
        {
            string root=TestScratch.CreateRootPath(
                "s2-request-aware-arm-state");
            try
            {
                Directory.CreateDirectory(root);
                string[] lines=Encoding.UTF8.GetString(raw).Split(
                    new[]{'\n'},StringSplitOptions.RemoveEmptyEntries);
                JObject baseline=JObject.Parse(lines[1]);
                JObject cutoff=JObject.Parse(lines[lines.Length-1]);
                AssertEx.Equal(1L,(long)baseline["native_arm_epoch"]);
                AssertEx.Equal(true,(bool)baseline["native_armed"]);
                AssertEx.Equal(epoch,(long)cutoff["native_arm_epoch"]);
                AssertEx.Equal(armed,(bool)cutoff["native_armed"]);
                var input=new Inputs {
                    Extractor=S2RequestAwareOracleV2Extractor.ForTesting(
                        1,4,2,3,
                        Fixture("gpgx-audio-service-manifests-v1.json")),
                    Raw=Path.Combine(root,"arm.raw.jsonl"),
                    Capability=Path.Combine(root,"arm.capability.json"),
                    Attestation=Path.Combine(root,"arm.attestation.json") };
                File.WriteAllBytes(input.Raw,raw);
                WriteAuthority(input,Capability(raw,2,3),raw);
                string output=Path.Combine(root,"window.jsonl");
                input.Extractor.ExtractForTesting(input.Raw,input.Capability,
                    input.Attestation,output);
                AssertEx.Equal(true,File.Exists(output));
            }
            finally { try { Directory.Delete(root,true); } catch { } }
        }

        private static void AssertProducerRawRejection(byte[] original,
            Action<string[]> mutate, string message)
        {
            string root = TestScratch.CreateRootPath(
                "s2-request-aware-producer-mutation");
            try
            {
                Directory.CreateDirectory(root);
                string[] lines = Encoding.UTF8.GetString(original).Split(
                    new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                mutate(lines);
                byte[] raw = Encoding.UTF8.GetBytes(
                    string.Join("\n", lines) + "\n");
                var input = new Inputs
                {
                    Extractor = S2RequestAwareOracleV2Extractor.ForTesting(
                        1, 4, 2, 3,
                        Fixture("gpgx-audio-service-manifests-v1.json")),
                    Raw = Path.Combine(root, "mutated.raw.jsonl"),
                    Capability = Path.Combine(root, "mutated.capability.json"),
                    Attestation = Path.Combine(root, "mutated.attestation.json")
                };
                File.WriteAllBytes(input.Raw, raw);
                WriteAuthority(input, Capability(raw, 2, 3), raw);
                AssertEx.Throws<InvalidDataException>(() => input.Extractor
                    .ExtractForTesting(input.Raw, input.Capability,
                        input.Attestation, Path.Combine(root, "out.jsonl")),
                    message);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        private static GpgxAudioTraceEvent[] UploadEvents()
        {
            var values = new List<GpgxAudioTraceEvent>();
            AddUploadProofEvents(values, 1);
            values.Add(Native(values.Count,1,2,0,378,5,0,4,0,1));
            values.Add(Native(values.Count,1,3,2,56,2,0,3,1,1));
            values.Add(Native(values.Count,1,4,3,272,21,0,9,2,1));
            AddOneByteSnapshot(values,4,3,9,2,331,1);
            values.Add(Native(values.Count,2,4,3,331,22,0,9,2,1));
            AddOneByteSnapshot(values,3,2,3,1,231,1);
            values.Add(Native(values.Count,2,3,2,231,3,0,3,1,1));
            GpgxAudioTraceEvent psg=Native(values.Count,4,2,0,0x0200,
                0,0,4,0,1);
            psg.Value=0x9F;
            values.Add(psg);
            GpgxAudioTraceEvent secondPsg=Native(values.Count,4,2,0,
                0x0201,0,0,4,0,1);
            secondPsg.Value=0x9E;
            values.Add(secondPsg);
            return values.ToArray();
        }

        private static GpgxAudioTraceEvent[] ResumeEvents()
        {
            var values = new List<GpgxAudioTraceEvent>();
            values.Add(Native(values.Count, 1, 1, 2, 56, 2,
                0, 3, 1, 1));
            values.Add(Native(values.Count, 1, 3, 1, 272, 21,
                0, 9, 2, 1));
            return values.ToArray();
        }

        private static void AddResumeCompletionEvents(
            IList<GpgxAudioTraceEvent> values)
        {
            GpgxAudioTraceEvent write = Native(values.Count, 3, 3, 1,
                0x0200, 0, 0, 9, 2, 1);
            write.Value = 0x22;
            values.Add(write);
            GpgxAudioTraceEvent psg = Native(values.Count, 4, 3, 1,
                0x0201, 0, 0, 9, 2, 1);
            psg.Value = 0x9F;
            values.Add(psg);
            AddOneByteSnapshot(values, 3, 1, 9, 2, 3508, 1);
            values.Add(Native(values.Count, 2, 3, 1, 3508, 23,
                0, 9, 2, 1));
            AddOneByteSnapshot(values, 1, 2, 3, 1, 231, 1);
            values.Add(Native(values.Count, 2, 1, 2, 231, 3,
                0, 3, 1, 1));
        }

        private static GpgxAudioTraceEvent[] RequestAndOpenServiceEvents(
            uint a7, bool includeArmProof, bool includeZ80)
        {
            var values=new List<GpgxAudioTraceEvent>();
            AddResumeCompletionEvents(values);
            AddOneByteSnapshot(values,2,0,4,0,432,1);
            values.Add(Native(values.Count,2,2,0,432,6,0,4,0,1));
            values.Add(Native(values.Count,8,2,0,0,0,0,1,0,3));
            AddOneByteSnapshot(values,2,0,1,0,0,3);
            values.Add(Native(values.Count,9,2,0,0,0,0,1,0,3));
            GpgxAudioTraceEvent marker = Native(values.Count, 10, 0, 0,
                S2PreconsumptionRequestObserver.Pc,
                S2PreconsumptionRequestObserver.MarkerToken,
                0, 0, 0, 2, 4);
            marker.Value = 3;
            marker.Payload = a7;
            values.Add(marker);
            if(includeArmProof)AddUploadProofEvents(values,3);
            if(includeZ80)
                values.Add(Native(values.Count, 1,
                    (ushort)(includeArmProof?4:3), 0, 378, 5,
                    0, 4, 0, 1));
            return values.ToArray();
        }

        private static GpgxAudioTraceEvent[] RequestInsideRootKind3Events(
            uint a7)
        {
            var values=new List<GpgxAudioTraceEvent>();
            AddResumeCompletionEvents(values);
            AddOneByteSnapshot(values,2,0,4,0,432,1);
            values.Add(Native(values.Count,2,2,0,432,6,0,4,0,1));
            values.Add(Native(values.Count,8,2,0,0,0,0,1,0,3));
            AddOneByteSnapshot(values,2,0,1,0,0,3);
            values.Add(Native(values.Count,9,2,0,0,0,0,1,0,3));
            AddUploadProofEvents(values,3);
            values.Add(Native(values.Count,1,4,0,56,1,0,3,0,1));
            GpgxAudioTraceEvent marker=Native(values.Count,10,4,0,
                S2PreconsumptionRequestObserver.Pc,
                S2PreconsumptionRequestObserver.Kind3MarkerToken,
                0,3,0,2,4);
            marker.Value=3;marker.Payload=a7;values.Add(marker);
            AddOneByteSnapshot(values,4,0,3,0,231,1);
            values.Add(Native(values.Count,2,4,0,231,3,0,3,0,1));
            return values.ToArray();
        }

        private static GpgxAudioTraceEvent[] RequestInsideNestedKind3Events(
            uint a7)
        {
            var values=new List<GpgxAudioTraceEvent>();
            AddResumeCompletionEvents(values);
            AddOneByteSnapshot(values,2,0,4,0,432,1);
            values.Add(Native(values.Count,2,2,0,432,6,0,4,0,1));
            values.Add(Native(values.Count,8,2,0,0,0,0,1,0,3));
            AddOneByteSnapshot(values,2,0,1,0,0,3);
            values.Add(Native(values.Count,9,2,0,0,0,0,1,0,3));
            AddUploadProofEvents(values,3);
            values.Add(Native(values.Count,1,4,0,378,5,0,4,0,1));
            values.Add(Native(values.Count,1,5,4,56,2,0,3,1,1));
            GpgxAudioTraceEvent marker=Native(values.Count,10,5,4,
                S2PreconsumptionRequestObserver.Pc,
                S2PreconsumptionRequestObserver.Kind3MarkerToken,
                0,3,1,2,4);
            marker.Value=3;marker.Payload=a7;values.Add(marker);
            AddOneByteSnapshot(values,5,4,3,1,231,1);
            values.Add(Native(values.Count,2,5,4,231,3,0,3,1,1));
            AddOneByteSnapshot(values,4,0,4,0,432,1);
            values.Add(Native(values.Count,2,4,0,432,6,0,4,0,1));
            return values.ToArray();
        }

        private static uint FindMarkerOrdinal(
            IEnumerable<GpgxAudioTraceEvent> events)
        {
            foreach(GpgxAudioTraceEvent value in events)
                if(value.Kind==10&&value.Pc==S2PreconsumptionRequestObserver.Pc)
                    return value.Ordinal;
            throw new InvalidOperationException("missing request marker");
        }

        private static GpgxAudioTraceEvent[] ResetAndRearmEvents()
        {
            var values=new List<GpgxAudioTraceEvent>();
            values.Add(Native(values.Count,8,1,0,0,1,0,1,0,3));
            AddOneByteSnapshot(values,4,0,4,0,0,3);
            GpgxAudioTraceEvent cancelled=Native(values.Count,2,4,0,0,0,
                0,4,0,3);
            cancelled.Flags=2;
            values.Add(cancelled);
            AddOneByteSnapshot(values,1,0,1,0,0,3);
            values.Add(Native(values.Count,9,1,0,0,0,0,1,0,3));
            AddUploadProofEvents(values,2);
            return values.ToArray();
        }

        private static void AddUploadProofEvents(
            IList<GpgxAudioTraceEvent> values, ushort token)
        {
            values.Add(Native(values.Count,1,token,0,0x0EC000,9,
                0,2,0,2));
            values.Add(Native(values.Count,5,token,0,0x0EC036,1,
                0,2,0,2));
            for(int offset=0;offset<8192;offset+=8)
                values.Add(Native(values.Count,6,token,0,0x0EC036,1,
                    (ushort)offset,2,0,2,8));
            values.Add(Native(values.Count,7,token,0,0x0EC036,1,
                8192,2,0,2));
            values.Add(Native(values.Count,2,token,0,0x0EC036,10,
                0,2,0,2));
        }

        private static void AddOneByteSnapshot(
            IList<GpgxAudioTraceEvent> values, ushort token,
            ushort parent, byte serviceKind, byte depth, uint pc,
            byte sourceCpu)
        {
            values.Add(Native(values.Count, 5, token, parent, pc, 2,
                0, serviceKind, depth, sourceCpu));
            values.Add(Native(values.Count, 6, token, parent, pc, 2,
                0, serviceKind, depth, sourceCpu, 1));
            values.Add(Native(values.Count, 7, token, parent, pc, 2,
                1, serviceKind, depth, sourceCpu));
        }

        private static GpgxAudioTraceEvent Native(int ordinal, byte kind,
            ushort token, ushort parent, uint pc, ushort subject,
            ushort offset, byte serviceKind, byte depth, byte sourceCpu,
            byte payloadLength = 0)
        {
            return new GpgxAudioTraceEvent
            {
                Ordinal = (uint)ordinal, Kind = kind, ServiceToken = token,
                ParentToken = parent, Pc = pc, Subject = subject,
                Offset = offset, ServiceKindId = serviceKind, Depth = depth,
                SourceCpu = sourceCpu, PayloadLength = payloadLength
            };
        }

        private static string Fixture(string name)
        {
            return Path.GetFullPath(Path.Combine(EndToEndTests.ToolDirectory,
                "fixtures", name));
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

        private sealed class ProducerTraceApi : IGpgxAudioTraceApi,
            IS2RequestSuccessorOrdinalApi
        {
            private int phase;
            internal GpgxAudioTraceEvent[] Events =
                new GpgxAudioTraceEvent[0];
            internal uint SuccessorOrdinal;
            public uint AbiVersion { get { return 4; } }
            public uint EventSize { get { return 32; } }
            public uint Capacity { get { return 65536; } }
            public int Configure(ref GpgxAudioObserverAdapter.Config config,
                byte[] mask, GpgxAudioObserverAdapter.ServiceKind[] kinds,
                GpgxAudioObserverAdapter.ServiceHook[] hooks,
                GpgxAudioObserverAdapter.SnapshotRange[] ranges)
            { phase = 1; return config.AbiVersion == 4 ? 0 : -3; }
            public int BeginFrame()
            { if (phase != 1) return -2; phase = 2; return 0; }
            public int EndFrame()
            { if (phase != 2) return -2; phase = 3; return 0; }
            public int EventCount(out uint count, out uint overflow)
            {
                count = phase == 3 ? (uint)Events.Length : 0;
                overflow = 0;
                return phase == 3 ? 0 : -2;
            }
            public int Drain(GpgxAudioTraceEvent[] target, uint capacity,
                out uint count)
            {
                if (phase != 3) { count = 0; return -2; }
                count = (uint)Events.Length;
                if (target != null) Array.Copy(Events, target, Events.Length);
                phase = 1;
                return 0;
            }
            public int GetFirstFault(
                out GpgxAudioObserverAdapter.FirstFault fault)
            { fault = new GpgxAudioObserverAdapter.FirstFault(); return 0; }
            public int BeginPublicationEpoch()
            { return phase == 1 ? 0 : -2; }
            public int AbortFrame() { phase = 1; return 0; }
            public int Disable() { phase = 0; return 0; }
            public int S2RequestSuccessorOrdinal(out uint ordinal)
            { ordinal = SuccessorOrdinal; return phase == 2 ? 0 : -2; }
        }

        private sealed class ProducerHost :
            IS2RequestAwareRawV3CandidateHost,
            IOverrideResumeDiagnosticAudioHost
        {
            private readonly Dictionary<string, uint> registers =
                new Dictionary<string, uint>(StringComparer.Ordinal);
            private readonly ProducerTraceApi api;
            private Action callback;
            private bool audioReady;
            internal bool ExecuteCallbackOnAdvance;
            internal short[] AudioAfterAdvance = new short[0];
            internal ProducerHost(ProducerTraceApi value) { api = value; }
            internal void Set(string name, uint value)
            { registers[name] = value; }
            public int CompletedFrame { get; private set; }
            public bool IsLagged { get { return false; } }
            public int LagCount { get { return 0; } }
            public int DiagnosticAudioSampleRate { get { return 44100; } }
            public void ClearButtons() { }
            public void SetButton(string name, bool pressed) { }
            public IDisposable RegisterExecuteCallback(uint address,
                Action value)
            {
                if (address != S2PreconsumptionRequestObserver.Pc)
                    throw new InvalidOperationException("callback PC");
                callback = value;
                return new CallbackRegistration(this);
            }
            public void Advance()
            {
                if (ExecuteCallbackOnAdvance)
                {
                    ExecuteCallbackOnAdvance = false;
                    callback();
                }
                CompletedFrame++;
            }
            public void AdvanceDiagnosticAudio()
            { Advance(); audioReady = true; }
            public short[] DrainDiagnosticAudio(out int stereoFrames)
            {
                short[] result = audioReady
                    ? AudioAfterAdvance : new short[0];
                audioReady = false;
                stereoFrames = result.Length / 2;
                return (short[])result.Clone();
            }
            public byte ReadMainRamByte(int offset) { return 0; }
            public uint ReadCpuRegister(string name)
            {
                uint value;
                if (!registers.TryGetValue(name, out value))
                    throw new InvalidOperationException("missing register");
                return value;
            }
            public byte[] CaptureDriverState() { return new byte[0x2000]; }
            public IGpgxAudioTraceApi CreateRequestCandidateAudioTraceApi()
            { return api; }
            public void Dispose() { }
            private sealed class CallbackRegistration : IDisposable
            {
                private ProducerHost host;
                internal CallbackRegistration(ProducerHost value)
                { host = value; }
                public void Dispose()
                { if (host != null) { host.callback = null; host = null; } }
            }
        }

        private static void WithSyntheticInputs(Action<string, Inputs> body)
        {
            string root = TestScratch.CreateRootPath("s2-request-aware-extractor");
            try
            {
                Directory.CreateDirectory(root);
                var inputs = new Inputs { Extractor = S2RequestAwareOracleV2Extractor
                    .ForTesting(10148, 10900, 10150, 10900,
                        Fixture("gpgx-audio-service-manifests-v1.json")) };
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
                ["rom_sha1"]="8bca5dcef1af3e00098666fd892dc1c2a76333f9", ["bk2_sha256"]="e850798f882b8c580aad148bc97cb50f260cae1d336dd649fe2f4dfae6796aa5",
                ["service_manifest_sha256"]="ef8f8103c38d70e41cb09cb29751f56815a0401709dc509071aa514d614813a0", ["first_row"]=10148,
                ["exclusive_end"]=10900, ["state_start"]=0,
                ["state_exclusive_end"]=0x2000, ["production_bound"]=false,
                ["request_manifest_schema"]="openggf.s2-preconsumption-request-manifest.v1" }));
            lines.Add(Json(new JObject { ["type"]="baseline", ["state_hex"]=state,
                ["ym_port0_latch"]=0, ["ym_port1_latch"]=0,
                ["native_arm_epoch"]=1, ["native_armed"]=true,
                ["active_services"]=new JArray(), ["pending_descendants"]=new JArray(),
                ["row"]=10148 }));
            for (int row = 10148; row < 10900; row++)
            {
                var events = new JArray();
                var transfers = new JArray();
                if (row == 10150)
                {
                    events.Add(Event(0, 10, 24, 3, "16715808"));
                    transfers.Add(Transfer(row, 0, 0, 0xB5, 3, 0,
                        "16715808"));
                }
                if (row == 10151)
                {
                    events.Add(Event(0, 10, 24, 3, "20"));
                    events.Add(Event(1, 10, 24, 3, "21"));
                    transfers.Add(Transfer(row, 0, 1, 1, 0, 0, "20"));
                    transfers.Add(Transfer(row, 1, 2, 2, 1, 1, "21"));
                }
                lines.Add(Json(new JObject { ["type"]="frame", ["row"]=row,
                    ["lag"]=false, ["state_hex"]=state, ["events"]=events,
                    ["override_resume"]=JValue.CreateNull(), ["pcm"]=JValue.CreateNull(),
                    ["request_transfers"]=transfers }));
            }
            lines.Add(Json(new JObject { ["type"]="cutoff", ["state_hex"]=state,
                ["ym_port0_latch"]=0, ["ym_port1_latch"]=0,
                ["native_arm_epoch"]=1, ["native_armed"]=true,
                ["active_services"]=new JArray(), ["pending_descendants"]=new JArray(),
                ["exclusive_end"]=10900 }));
            return Encoding.UTF8.GetBytes(string.Join("\n", lines) + "\n");
        }

        private static JObject Capability(byte[] raw)
        {
            return Capability(raw, 10150, 10900);
        }

        private static JObject Capability(byte[] raw, int windowFirst,
            int windowEnd)
        {
            string[] lines = Encoding.UTF8.GetString(raw).Split(new[] { '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            long baseCount=0, allCount=0, markerCount=0, requestCount=0;
            int maximumRequestOccupancy=0;
            int resumeCount=0,pcmCount=0;
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
                        int subject=(int)evt["subject"];
                        bool marker=(int)evt["kind"]==10&&(int)evt["value"]==3
                            &&(int)evt["pc"]==0x10D6
                            &&(subject==S2PreconsumptionRequestObserver.MarkerToken
                                ||subject==S2PreconsumptionRequestObserver
                                    .Kind3MarkerToken);
                        if(marker){markerBytes.AddRange(bytes);markerCount++;}
                        else {baseBytes.AddRange(bytes);baseCount++;}
                    }
                    foreach(JToken transfer in (JArray)record["request_transfers"])
                    { requestBytes.AddRange(Canonical(transfer));requestCount++; }
                    maximumRequestOccupancy=Math.Max(maximumRequestOccupancy,
                        ((JArray)record["request_transfers"]).Count);
                    if(record["override_resume"].Type!=JTokenType.Null)
                        resumeCount++;
                    if(record["pcm"].Type!=JTokenType.Null)pcmCount++;
                }
                else if((string)record["type"]=="cutoff")cutoff=record;
            }
            JObject capability=JObject.Parse(File.ReadAllText(Path.Combine(
                EndToEndTests.ToolDirectory,"fixtures",
                "gpgx-audio-capability-s2-request-v3.template.json")));
            // The friend-only seam derives every reviewed identity and digest
            // domain from the committed candidate template. Only unavailable
            // full-run inventory evidence is synthetic.
            JObject metadata=JObject.Parse(lines[0]);
            capability["harness_executable_sha256"]=Digest(
                File.ReadAllBytes(typeof(GpgxHost).Assembly.Location));
            capability["first_row"]=(int)metadata["first_row"];
            capability["exclusive_end"]=(int)metadata["exclusive_end"];
            capability["window_first_row"]=windowFirst;
            capability["window_exclusive_end"]=windowEnd;
            capability["base_event_count"]=baseCount; capability["all_event_count"]=allCount;
            capability["marker_event_count"]=markerCount; capability["request_count"]=requestCount;
            capability["base_event_sha256"]=Digest(baseBytes.ToArray());
            capability["all_event_sha256"]=Digest(allBytes.ToArray());
            capability["marker_event_sha256"]=Digest(markerBytes.ToArray());
            capability["request_sha256"]=Digest(requestBytes.ToArray());
            capability["max_request_occupancy"]=maximumRequestOccupancy;
            capability["override_resume_count"]=resumeCount;
            capability["override_resume_sha256"]=Digest(resumeCount==0
                ?new byte[0]:Canonical(FirstEnvelope(lines,"override_resume")));
            capability["pcm_count"]=pcmCount;
            capability["pcm_sha256"]=Digest(pcmCount==0
                ?new byte[0]:Canonical(FirstEnvelope(lines,"pcm")));
            capability["cutoff_frontier_sha256"]=Digest(Canonical(cutoff));
            capability["terminal_state_sha256"]=Digest(new byte[0x2000]);
            return capability;
        }

        private static JToken FirstEnvelope(string[] lines,string name)
        {
            foreach(string line in lines)
            {
                JObject value=JObject.Parse(line);
                if((string)value["type"]=="frame"
                    &&value[name].Type!=JTokenType.Null)return value[name];
            }
            throw new InvalidOperationException("missing envelope: "+name);
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
        private static byte[] Concat(params byte[][] values)
        {
            int length = 0;
            foreach (byte[] value in values) length += value.Length;
            var result = new byte[length];
            int offset = 0;
            foreach (byte[] value in values)
            {
                Buffer.BlockCopy(value, 0, result, offset, value.Length);
                offset += value.Length;
            }
            return result;
        }
        private static byte[] StateBytes(string value)
        {
            var result = new byte[value.Length / 2];
            for (int index = 0; index < result.Length; index++)
            {
                int high = value[index * 2] <= '9'
                    ? value[index * 2] - '0' : value[index * 2] - 'a' + 10;
                int low = value[index * 2 + 1] <= '9'
                    ? value[index * 2 + 1] - '0' : value[index * 2 + 1] - 'a' + 10;
                result[index] = (byte)((high << 4) | low);
            }
            return result;
        }
        private static string Digest(byte[] bytes)
        { using(SHA256 hash=SHA256.Create())return Hex(hash.ComputeHash(bytes)); }
        private static string Hex(byte[] bytes)
        { var value=new StringBuilder(bytes.Length*2);foreach(byte b in bytes)value.Append(b.ToString("x2"));return value.ToString(); }
    }
}
