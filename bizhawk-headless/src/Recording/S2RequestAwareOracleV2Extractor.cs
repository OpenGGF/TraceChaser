using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>
    /// Closed, comparison-only projection of a candidate full-run S2 raw-v3
    /// stream.  This is deliberately not a capture route: production entry
    /// rejects an unbound capability and no CLI calls either entry point.
    /// </summary>
    internal sealed class S2RequestAwareOracleV2Extractor
    {
        internal const string OracleSchema = "openggf.s2-oracle-audio-raw.v2";
        internal const string RawSchema = "openggf.s2-complete-run-audio-raw.v3";
        internal const string CapabilitySchema =
            "openggf.s2-request-aware-raw-v3-capability.v1";
        internal const string AttestationSchema =
            "openggf.s2-request-aware-raw-v3-attestation.v1";

        private const int DefaultSourceFirst = 769;
        private const int DefaultSourceEnd = 259590;
        private const int DefaultWindowFirst = 10150;
        private const int DefaultWindowEnd = 10900;
        private const string CandidateManifestSha256 =
            "7fa043d51da88ac25a885ed371c55f33362a9af909c05b7807558643e1edc62e";
        private const string CandidatePatchSha256 =
            "03ee8c72e14c96875cdbda4dc401bed358a8e4e1314d9fc63907598b24a1ba5b";
        private const string CandidateRecipeSha256 =
            "f1bae0e92c238c8fb92fc424482e51facecc1aabb01e9443c889ef49e7450312";
        private const string CandidateProfileSha256 =
            "e955877eb60bc1ba7c281cc63bd3b9f7146e086cb14162dfaf09e38fae227719";
        private readonly int sourceFirst, sourceEnd, windowFirst, windowEnd;
        private readonly bool syntheticTestSeam;

        internal S2RequestAwareOracleV2Extractor()
            : this(DefaultSourceFirst, DefaultSourceEnd,
                DefaultWindowFirst, DefaultWindowEnd, false) { }

        private S2RequestAwareOracleV2Extractor(int sourceStart, int sourceStop,
            int windowStart, int windowStop, bool testing)
        {
            if (sourceStart < 0 || sourceStop <= sourceStart
                || windowStart <= sourceStart || windowStop > sourceStop
                || windowStop <= windowStart)
                throw new ArgumentException("The S2 request-aware bounds are invalid.");
            sourceFirst = sourceStart; sourceEnd = sourceStop;
            windowFirst = windowStart; windowEnd = windowStop;
            syntheticTestSeam=testing;
        }

        internal static S2RequestAwareOracleV2Extractor ForTesting(
            int sourceStart, int sourceStop, int windowStart, int windowStop)
        { return new S2RequestAwareOracleV2Extractor(sourceStart, sourceStop,
            windowStart, windowStop, true); }

        /// <summary>Friend-test validation of the committed candidate shape.
        /// It deliberately does not create an extraction or publication route.</summary>
        internal static void ValidateCandidateTemplateForTesting(string path)
        {
            var extractor=new S2RequestAwareOracleV2Extractor(
                DefaultSourceFirst,DefaultSourceEnd,DefaultWindowFirst,
                DefaultWindowEnd,true);
            extractor.ValidateCapability(ReadObject(path,"capability"));
        }

        /// <summary>Test-only synthetic projection; it accepts only an explicitly
        /// unbound capability and cannot establish capture authority.</summary>
        internal void ExtractForTesting(string rawPath, string capabilityPath,
            string attestationPath, string outputPath)
        { Extract(rawPath, capabilityPath, attestationPath, outputPath); }

        private void Extract(string rawPath, string capabilityPath,
            string attestationPath, string outputPath)
        {
            JObject capability = ReadObject(capabilityPath, "capability");
            ValidateCapability(capability);
            JObject attestation = ReadObject(attestationPath, "attestation");
            string stagedWindow = StagePath(outputPath);
            try
            {
                Projection projection = ValidateAndProject(rawPath, capability,
                    stagedWindow);
                ValidateAttestation(attestation, projection.Raw, capability);
                Publish(outputPath, stagedWindow, projection);
            }
            finally { TryDelete(stagedWindow); }
        }

        private Projection ValidateAndProject(string rawPath, JObject capability,
            string stagedWindow)
        {
            RequireExisting(rawPath,"raw");
            using (var rawInput = new HashingReadStream(new FileStream(rawPath,
                FileMode.Open, FileAccess.Read, FileShare.Read)))
            using (var reader = new StreamReader(rawInput,
                new UTF8Encoding(false, true), false, 65536))
            using (var window = new StreamWriter(new FileStream(stagedWindow,
                FileMode.CreateNew, FileAccess.Write, FileShare.None),
                new UTF8Encoding(false)))
            {
            string line = ReadLine(reader, "metadata");
            JObject metadata = ParseLine(line, "metadata");
            Exact(metadata, "metadata", "type", "schema", "rom_sha1",
                "bk2_sha256", "service_manifest_sha256", "first_row",
                "exclusive_end", "state_start", "state_exclusive_end",
                "production_bound", "request_manifest_schema");
            Require("metadata" == String(metadata, "type")
                && RawSchema == String(metadata, "schema"), "raw schema differs");
            Require(Integer(metadata, "first_row") == sourceFirst
                && Integer(metadata, "exclusive_end") == sourceEnd,
                "raw interval differs");
            Require(Integer(metadata, "state_start") == 0
                && Integer(metadata, "state_exclusive_end") == 0x2000,
                "raw state range differs");
            Require(Boolean(metadata,"production_bound") == false,
                "candidate raw must remain unbound");
            Require(String(metadata, "request_manifest_schema")
                == "openggf.s2-preconsumption-request-manifest.v1",
                "request manifest schema differs");
            Require(String(metadata, "rom_sha1") == S2AudioObserverProfile.RomSha1
                && String(metadata, "bk2_sha256") == S2AudioObserverProfile.MovieSha256
                && String(metadata, "service_manifest_sha256")
                    == S2AudioObserverProfile.ServiceManifestSha256
                && String(metadata, "rom_sha1") == String(capability, "rom_sha1")
                && String(metadata, "bk2_sha256") == String(capability, "bk2_sha256")
                && String(metadata, "service_manifest_sha256")
                    == String(capability, "service_manifest_sha256"),
                "raw identity differs from capability");

            JObject firstBaseline = ParseLine(ReadLine(reader, "baseline"), "baseline");
            ValidateBaseline(firstBaseline, sourceFirst, false);
            int latch0 = Integer(firstBaseline, "ym_port0_latch");
            int latch1 = Integer(firstBaseline, "ym_port1_latch");
            int precedingLatch0 = 0, precedingLatch1 = 0;
            JObject preceding = null;
            long baseCount = 0, allCount = 0, markerCount = 0, requestCount = 0;
            int occupancy = 0, nextGlobal = 0, expectedRow = sourceFirst;
            using (SHA256 baseDigest = SHA256.Create())
            using (SHA256 allDigest = SHA256.Create())
            using (SHA256 markerDigest = SHA256.Create())
            using (SHA256 requestDigest = SHA256.Create())
            {
                while (true)
                {
                    line = ReadLine(reader, "frame or cutoff");
                    JObject value = ParseLine(line, "frame or cutoff");
                    if (String(value, "type") == "cutoff")
                    {
                        if (reader.ReadLine() != null)
                            throw Invalid("raw records follow cutoff");
                        FileEvidence raw=rawInput.Finish();
                        ValidateCutoff(value, sourceEnd);
                        Require(expectedRow == sourceEnd, "raw is truncated");
                        Require(Integer(value,"ym_port0_latch")==latch0
                            && Integer(value,"ym_port1_latch")==latch1,
                            "cutoff latches differ from folded events");
                        baseDigest.TransformFinalBlock(new byte[0], 0, 0);
                        allDigest.TransformFinalBlock(new byte[0], 0, 0);
                        markerDigest.TransformFinalBlock(new byte[0], 0, 0);
                        requestDigest.TransformFinalBlock(new byte[0], 0, 0);
                        Require(baseCount == Integer64(capability, "base_event_count")
                            && allCount == Integer64(capability, "all_event_count")
                            && markerCount == Integer64(capability, "marker_event_count")
                            && requestCount == Integer64(capability, "request_count")
                            && occupancy == Integer(capability, "max_request_occupancy"),
                            "raw inventory count differs from capability");
                        Require(Hex(baseDigest.Hash) == String(capability, "base_event_sha256")
                            && Hex(allDigest.Hash) == String(capability, "all_event_sha256")
                            && Hex(markerDigest.Hash) == String(capability, "marker_event_sha256")
                            && Hex(requestDigest.Hash) == String(capability, "request_sha256"),
                            "raw inventory digest differs from capability");
                        Require(Hex(Sha256(Canonical(value)))
                            == String(capability, "cutoff_frontier_sha256"),
                            "raw cutoff differs from capability");
                        Require(Hex(Sha256(StateBytes(String(value, "state_hex"))))
                            == String(capability, "terminal_state_sha256"),
                            "raw terminal state differs from capability");
                        if (preceding == null || expectedRow != sourceEnd)
                            throw Invalid("raw has no complete bounded window");
                        return new Projection(metadata, preceding, precedingLatch0,
                            precedingLatch1, capability, raw, value);
                    }
                    ValidateFrame(value, expectedRow);
                    JArray events = Array(value, "events");
                    var markers = new Dictionary<uint, JObject>();
                    uint nativeOrdinal=0;
                    foreach (JToken token in events)
                    {
                        JObject evt = Object(token, "event"); ValidateEvent(evt);
                        Require(Unsigned(evt,"ordinal")==nativeOrdinal++,
                            "native ordinal is not zero-based contiguous");
                        byte[] eventBytes = Canonical(evt); allDigest.TransformBlock(
                            eventBytes, 0, eventBytes.Length, null, 0); allCount++;
                        if (MarkerCandidate(evt) && !Marker(evt))
                            throw Invalid("action-7 candidate source or topology differs");
                        if (Marker(evt))
                        {
                            uint ordinal = Unsigned(evt, "ordinal");
                            if (markers.ContainsKey(ordinal))
                                throw Invalid("duplicate action-7 marker ordinal");
                            markers.Add(ordinal, evt);
                            markerDigest.TransformBlock(eventBytes, 0,
                                eventBytes.Length, null, 0); markerCount++;
                        }
                        else
                        {
                            baseDigest.TransformBlock(eventBytes, 0,
                                eventBytes.Length, null, 0); baseCount++;
                        }
                        FoldLatch(evt, ref latch0, ref latch1);
                    }
                    JArray transfers = Array(value, "request_transfers");
                    if (transfers.Count > 4) throw Invalid("request occupancy exceeds slots");
                    occupancy = Math.Max(occupancy, transfers.Count);
                    var consumed = new HashSet<uint>();
                    uint previousNative = 0; bool hasNative = false;
                    for (int transferIndex = 0; transferIndex < transfers.Count;
                        transferIndex++)
                    {
                        JObject transfer = Object(transfers[transferIndex], "transfer");
                        ValidateTransfer(transfer, expectedRow, transferIndex,
                            nextGlobal++, markers, consumed, ref previousNative,
                            ref hasNative);
                        byte[] transferBytes = Canonical(transfer);
                        requestDigest.TransformBlock(transferBytes, 0,
                            transferBytes.Length, null, 0); requestCount++;
                    }
                    if (consumed.Count != markers.Count)
                        throw Invalid("action-7 marker has no request transfer");
                    if (expectedRow == windowFirst - 1)
                    {
                        preceding = (JObject)value.DeepClone();
                        precedingLatch0 = latch0; precedingLatch1 = latch1;
                    }
                    if (expectedRow >= windowFirst && expectedRow < windowEnd)
                        window.WriteLine(line);
                    expectedRow++;
                }
            }
            }
        }

        private sealed class Projection
        {
            internal JObject Metadata, Preceding, Capability, Cutoff;
            internal int Latch0, Latch1;
            internal FileEvidence Raw;
            internal Projection(JObject metadata,JObject preceding,int latch0,int latch1,
                JObject capability,FileEvidence raw,JObject cutoff)
            { Metadata=metadata;Preceding=preceding;Latch0=latch0;Latch1=latch1;
              Capability=capability;Raw=raw;Cutoff=cutoff; }
        }

        private void Publish(string outputPath, string stagedWindow,
            Projection projection)
        {
            JObject metadata=projection.Metadata, preceding=projection.Preceding;
            JObject capability=projection.Capability, cutoff=projection.Cutoff;
            int latch0=projection.Latch0,latch1=projection.Latch1;
            FileEvidence raw=projection.Raw;
            Publish(outputPath, writer => {
            Write(writer, new JObject {
                ["type"]="metadata", ["schema"]=OracleSchema,
                ["rom_sha1"]=String(metadata,"rom_sha1"),
                ["bk2_sha256"]=String(metadata,"bk2_sha256"),
                ["service_manifest_sha256"]=String(metadata,"service_manifest_sha256"),
                ["first_row"]=windowFirst, ["exclusive_end"]=windowEnd,
                ["state_start"]=0, ["state_exclusive_end"]=0x2000,
                ["source_schema"]=RawSchema, ["source_first_row"]=sourceFirst,
                ["source_exclusive_end"]=sourceEnd,
                ["source_raw_sha256"]=raw.Sha256,
                ["source_raw_byte_count"]=raw.ByteCount,
                ["source_capability_sha256"]=Hex(Sha256(Canonical(capability))),
                ["request_transfer_schema"]="openggf.s2-preconsumption-request-transfer.v1",
                ["production_bound"]=false });
            Write(writer, new JObject { ["type"]="baseline", ["row"]=windowFirst,
                ["source_preceding_row"]=windowFirst-1,
                ["state_hex"]=String(preceding,"state_hex"),
                ["ym_port0_latch"]=latch0, ["ym_port1_latch"]=latch1 });
            using(var input=new StreamReader(File.OpenRead(stagedWindow),
                new UTF8Encoding(false,true),false,65536))
            { string frame;while((frame=input.ReadLine())!=null)writer.WriteLine(frame); }
            Write(writer, new JObject { ["type"]="cutoff", ["exclusive_end"]=windowEnd,
                ["source_cutoff_exclusive_end"]=sourceEnd,
                ["source_cutoff_frontier_sha256"]=Hex(Sha256(Canonical(cutoff))),
                ["terminal_state_sha256"]=String(capability,"terminal_state_sha256") });
            });
        }

        private static void ValidateBaseline(JObject value, int row,
            bool bounded)
        {
            if (bounded) Exact(value,"baseline","type","row","source_preceding_row",
                "state_hex","ym_port0_latch","ym_port1_latch");
            else Exact(value,"baseline","type","state_hex","ym_port0_latch",
                "ym_port1_latch","native_arm_epoch","native_armed",
                "active_services","pending_descendants","row");
            Require(String(value,"type")=="baseline" && Integer(value,"row")==row,
                "baseline row differs");
            State(value,"state_hex"); Byte(value,"ym_port0_latch");
            Byte(value,"ym_port1_latch");
            if(!bounded)
            {
                Integer64(value,"native_arm_epoch"); Boolean(value,"native_armed");
                ValidateServices(Array(value,"active_services"),"active services");
                ValidateServices(Array(value,"pending_descendants"),"pending descendants");
            }
        }

        private static void ValidateFrame(JObject value, int row)
        {
            Exact(value,"frame","type","row","lag","state_hex","events",
                "override_resume","pcm","request_transfers");
            Require(String(value,"type")=="frame" && Integer(value,"row")==row,
                "raw frame rows are not contiguous");
            if (value["lag"] == null || value["lag"].Type != JTokenType.Boolean)
                throw Invalid("frame lag differs");
            State(value,"state_hex");
            ValidateOverrideResume(value["override_resume"]);
            ValidatePcm(value["pcm"],row);
        }

        private static void ValidateEvent(JObject value)
        {
            Exact(value,"event","ordinal","service_token","parent_token","pc",
                "subject","offset","kind","service_kind","depth","source_cpu",
                "payload_length","value","flags","reserved","payload");
            Unsigned(value,"ordinal"); Unsigned(value,"pc"); Unsigned(value,"subject");
            Unsigned(value,"offset"); Byte(value,"kind"); Byte(value,"service_kind");
            Byte(value,"depth"); Byte(value,"source_cpu"); Byte(value,"payload_length");
            Unsigned(value,"value"); Unsigned(value,"flags"); Unsigned(value,"reserved");
            ulong parsed; if (!ulong.TryParse(String(value,"payload"), out parsed)
                || String(value,"payload")!=parsed.ToString())
                throw Invalid("event payload differs");
        }

        private static void ValidateTransfer(JObject value, int row, int order,
            int global, IDictionary<uint,JObject> markers, ISet<uint> consumed,
            ref uint previousNative, ref bool hasNative)
        {
            Exact(value,"request transfer","row","order","global_transfer_ordinal",
                "request","slot","pc","a7","native_ordinal","source_cpu",
                "service_token","service_kind","depth","active_service_owner");
            Require(Integer(value,"row")==row && Integer(value,"order")==order
                && Integer(value,"global_transfer_ordinal")==global,
                "request transfer order differs");
            Require(Byte(value,"request") != 0 && Integer(value,"slot") >= 0
                && Integer(value,"slot") <= 3
                && Unsigned(value,"pc")==S2PreconsumptionRequestObserver.Pc
                && Byte(value,"source_cpu")==S2PreconsumptionRequestObserver.MarkerSourceCpu
                && Integer(value,"service_token")==S2PreconsumptionRequestObserver.MarkerServiceToken
                && Byte(value,"service_kind")==S2PreconsumptionRequestObserver.MarkerServiceKind
                && Byte(value,"depth")==S2PreconsumptionRequestObserver.MarkerDepth,
                "request transfer identity differs");
            uint a7; if (!uint.TryParse(String(value,"a7"), out a7)
                || String(value,"a7")!=a7.ToString())
                throw Invalid("request transfer A7 differs");
            uint native = Unsigned(value,"native_ordinal");
            if (hasNative && native <= previousNative)
                throw Invalid("request native ordinal regressed");
            hasNative=true; previousNative=native;
            JObject owner=Object(value["active_service_owner"],"active service owner");
            Exact(owner,"active service owner","token","kind","depth");
            Require(Integer(owner,"token")==Integer(value,"service_token")
                && Integer(owner,"kind")==Integer(value,"service_kind")
                && Integer(owner,"depth")==Integer(value,"depth"),
                "request service owner differs");
            JObject marker; if (!markers.TryGetValue(native,out marker)
                || !consumed.Add(native) || !Marker(marker)
                || String(marker,"payload") != String(value,"a7"))
                throw Invalid("request transfer/action-7 marker differs");
        }

        private static bool Marker(JObject value)
        { return Byte(value,"kind")==10 && Unsigned(value,"value")==3
            && Unsigned(value,"pc")==S2PreconsumptionRequestObserver.Pc
            && Unsigned(value,"subject")==S2PreconsumptionRequestObserver.MarkerToken
            && Byte(value,"source_cpu")==S2PreconsumptionRequestObserver.MarkerSourceCpu
            && Integer(value,"service_token")==S2PreconsumptionRequestObserver.MarkerServiceToken
            && Integer(value,"parent_token")==S2PreconsumptionRequestObserver.MarkerServiceToken
            && Byte(value,"service_kind")==S2PreconsumptionRequestObserver.MarkerServiceKind
            && Byte(value,"depth")==S2PreconsumptionRequestObserver.MarkerDepth
            && Byte(value,"payload_length")==4; }
        private static bool MarkerCandidate(JObject value)
        { return Unsigned(value,"subject")==S2PreconsumptionRequestObserver.MarkerToken
            || (Byte(value,"kind")==10 && Unsigned(value,"value")==3
                && Unsigned(value,"pc")==S2PreconsumptionRequestObserver.Pc); }

        private static void FoldLatch(JObject value, ref int latch0, ref int latch1)
        {
            if (Byte(value,"kind")==8) { latch0=0;latch1=0;return; }
            if (Byte(value,"kind")!=3) return;
            int subject=Integer(value,"subject"), data=Integer(value,"value");
            if(subject==0)latch0=data; else if(subject==2)latch1=data;
        }

        private static void ValidateCutoff(JObject value, int end)
        {
            Exact(value,"cutoff","type","state_hex","ym_port0_latch",
                "ym_port1_latch","native_arm_epoch","native_armed",
                "active_services","pending_descendants","exclusive_end");
            Require(String(value,"type")=="cutoff" && Integer(value,"exclusive_end")==end,
                "raw cutoff differs"); State(value,"state_hex");
            Byte(value,"ym_port0_latch"); Byte(value,"ym_port1_latch");
            Integer64(value,"native_arm_epoch"); Boolean(value,"native_armed");
            ValidateServices(Array(value,"active_services"),"active services");
            ValidateServices(Array(value,"pending_descendants"),"pending descendants");
        }

        private static void ValidateOverrideResume(JToken value)
        {
            if(value==null)throw Invalid("v2 override envelope is incomplete");
            if(value.Type==JTokenType.Null)return;
            JObject resume=Object(value,"override resume");
            Exact(resume,"override resume","request","admission","request_pc",
                "pc","service_token","service_begin_ordinal","native_ordinal",
                "frame","fix_driver_bugs","restores_saved_priority",
                "restores_psg_noise","writes");
            Require(String(resume,"request")=="cfFadeInToPrevious"
                && String(resume,"admission")=="native_service_completion"
                && Unsigned(resume,"request_pc")==0x0D35
                && Unsigned(resume,"pc")==0x0DB4 && Integer(resume,"fix_driver_bugs")==0,
                "override resume identity differs");
            Unsigned(resume,"service_token"); Unsigned(resume,"service_begin_ordinal");
            Unsigned(resume,"native_ordinal"); Integer(resume,"frame");
            Boolean(resume,"restores_saved_priority"); Boolean(resume,"restores_psg_noise");
            JArray writes=Array(resume,"writes"); Require(writes.Count>0,"override resume has no writes");
            foreach(JToken write in writes) ValidateChip(Object(write,"override write"));
        }

        private static void ValidatePcm(JToken value,int row)
        {
            if(value==null)throw Invalid("v2 PCM envelope is incomplete");
            if(value.Type==JTokenType.Null)return;
            JObject pcm=Object(value,"PCM");
            Exact(pcm,"PCM","selection","row","offset","sample_rate","channels",
                "format","stereo_frames","byte_count","pcm_hex","sha256");
            Require((String(pcm,"selection")=="service_frame" || String(pcm,"selection")=="following_row")
                && Integer(pcm,"row")==row && Integer(pcm,"offset")>=0
                && Integer(pcm,"sample_rate")>0 && Integer(pcm,"channels")==2
                && String(pcm,"format")=="s16le-interleaved-stereo"
                && Integer64(pcm,"stereo_frames")>=0 && Integer64(pcm,"byte_count")>=0,
                "PCM identity differs");
            HexData(String(pcm,"pcm_hex")); HexString(String(pcm,"sha256"));
            Require(String(pcm,"pcm_hex").Length==Integer64(pcm,"byte_count")*2,
                "PCM byte count differs");
        }

        private static void ValidateServices(JArray services,string label)
        {
            var seen=new HashSet<uint>();
            foreach(JToken token in services)
            {
                JObject service=Object(token,label); Exact(service,label,"token","parent_token",
                    "kind","depth","current_parent_token","current_depth","begin_coordinate",
                    "begin_row","begin_native_ordinal","end_coordinate","begin_pc","end_pc",
                    "begin_hook_token","end_hook_token","begin_source_cpu","cancelled","complete",
                    "chips","snapshots","ancestry_transitions");
                uint serviceToken=Unsigned(service,"token");
                Require(seen.Add(serviceToken),label+" has duplicate service token");
                Unsigned(service,"parent_token"); Byte(service,"kind"); Byte(service,"depth");
                Unsigned(service,"current_parent_token"); Byte(service,"current_depth");
                Integer64(service,"begin_coordinate"); Integer(service,"begin_row");
                Unsigned(service,"begin_native_ordinal"); Integer64(service,"end_coordinate");
                Unsigned(service,"begin_pc"); Unsigned(service,"end_pc"); Unsigned(service,"begin_hook_token");
                Unsigned(service,"end_hook_token"); Byte(service,"begin_source_cpu");
                Boolean(service,"cancelled"); Boolean(service,"complete");
                foreach(JToken chip in Array(service,"chips")) ValidateChip(Object(chip,"chip"));
                foreach(JToken snapshot in Array(service,"snapshots")) ValidateSnapshot(Object(snapshot,"snapshot"));
                foreach(JToken transition in Array(service,"ancestry_transitions")) ValidateTransition(Object(transition,"ancestry transition"));
            }
        }

        private static void ValidateChip(JObject value)
        { Exact(value,"chip","coordinate","native_ordinal","event_kind","subject","value","pc","source_cpu","data","port","register");
          Integer64(value,"coordinate");Unsigned(value,"native_ordinal");Byte(value,"event_kind");Unsigned(value,"subject");Unsigned(value,"value");Unsigned(value,"pc");Byte(value,"source_cpu");Boolean(value,"data");Byte(value,"port");Byte(value,"register"); }
        private static void ValidateSnapshot(JObject value)
        { Exact(value,"snapshot","range_id","source_cpu","pc","bytes_hex");Unsigned(value,"range_id");Byte(value,"source_cpu");Unsigned(value,"pc");HexData(String(value,"bytes_hex")); }
        private static void ValidateTransition(JObject value)
        { Exact(value,"ancestry transition","coordinate","native_ordinal","previous_parent_token","previous_depth","current_parent_token","current_depth","hook_token","source_cpu","pc");Integer64(value,"coordinate");Unsigned(value,"native_ordinal");Unsigned(value,"previous_parent_token");Byte(value,"previous_depth");Unsigned(value,"current_parent_token");Byte(value,"current_depth");Unsigned(value,"hook_token");Byte(value,"source_cpu");Unsigned(value,"pc"); }

        private void ValidateCapability(JObject value)
        {
            Exact(value,"capability","schema","production_bound","producer",
                "rom_sha1","bk2_sha256","service_manifest_sha256",
                "candidate_manifest_sha256","candidate_patch_sha256",
                "candidate_recipe_sha256","candidate_profile_sha256",
                "harness_executable_sha256",
                "first_row","exclusive_end","window_first_row","window_exclusive_end",
                "base_event_count","all_event_count","marker_event_count","request_count",
                "base_event_sha256","all_event_sha256","marker_event_sha256","request_sha256",
                "max_request_occupancy","cutoff_frontier_sha256","terminal_state_sha256",
                "digest_domains");
            Require(String(value,"schema")==CapabilitySchema
                && String(value,"producer")=="s2-complete-audio-request-candidate"
                && Integer(value,"first_row")==sourceFirst
                && Integer(value,"exclusive_end")==sourceEnd
                && Integer(value,"window_first_row")==windowFirst
                && Integer(value,"window_exclusive_end")==windowEnd,
                "capability identity differs");
            Require(String(value,"rom_sha1")==S2AudioObserverProfile.RomSha1
                && String(value,"bk2_sha256")==S2AudioObserverProfile.MovieSha256
                && String(value,"service_manifest_sha256")==S2AudioObserverProfile.ServiceManifestSha256
                && String(value,"candidate_manifest_sha256")==CandidateManifestSha256
                && String(value,"candidate_patch_sha256")==CandidatePatchSha256
                && String(value,"candidate_recipe_sha256")==CandidateRecipeSha256
                && String(value,"candidate_profile_sha256")==CandidateProfileSha256,
                "capability static identity differs");
            Require(Boolean(value,"production_bound")==false,
                "candidate capability must remain unbound");
            foreach(string name in new[]{"candidate_manifest_sha256",
                "candidate_patch_sha256","candidate_recipe_sha256",
                "candidate_profile_sha256"}) HexString(String(value,name));
            JObject domains=Object(value["digest_domains"],"digest domains");
            Exact(domains,"digest domains","raw_sha256","event_and_request_sha256",
                "cutoff_frontier_sha256","terminal_state_sha256");
            Require(String(domains,"raw_sha256")=="raw-file-bytes-v1"
                && String(domains,"event_and_request_sha256")=="compact-json-lf-v1"
                && String(domains,"cutoff_frontier_sha256")=="compact-json-lf-v1"
                && String(domains,"terminal_state_sha256")=="decoded-z80-state-bytes-v1",
                "capability digest domains differ");
            bool allUnbound=Null(value,"harness_executable_sha256")
                && Null(value,"base_event_count") && Null(value,"all_event_count")
                && Null(value,"marker_event_count") && Null(value,"request_count")
                && Null(value,"base_event_sha256") && Null(value,"all_event_sha256")
                && Null(value,"marker_event_sha256") && Null(value,"request_sha256")
                && Null(value,"max_request_occupancy")
                && Null(value,"cutoff_frontier_sha256")
                && Null(value,"terminal_state_sha256");
            if(allUnbound)return;
            Require(syntheticTestSeam,"unbound candidate has no authenticated inventory");
            foreach(string name in new[]{"harness_executable_sha256",
                "base_event_sha256","all_event_sha256","marker_event_sha256",
                "request_sha256","cutoff_frontier_sha256","terminal_state_sha256"})
                HexString(String(value,name));
            if(Integer64(value,"base_event_count")<0 || Integer64(value,"all_event_count")<0
                || Integer64(value,"marker_event_count")<0 || Integer64(value,"request_count")<0
                || Integer(value,"max_request_occupancy")<0 || Integer(value,"max_request_occupancy")>4)
                throw Invalid("capability inventory range differs");
        }

        private static void ValidateAttestation(JObject value, FileEvidence raw,
            JObject capability)
        {
            Exact(value,"attestation","schema","raw_sha256","raw_byte_count",
                "status_count","fault_count","overflow_count","authority_id",
                "capability_sha256");
            Require(String(value,"schema")==AttestationSchema
                && String(value,"raw_sha256")==raw.Sha256
                && Integer64(value,"raw_byte_count")==raw.ByteCount
                && Integer64(value,"status_count")==1 && Integer64(value,"fault_count")==0
                && Integer64(value,"overflow_count")==0
                && String(value,"authority_id")=="s2-request-candidate-unbound"
                && String(value,"capability_sha256")==Hex(Sha256(Canonical(capability))),
                "raw attestation differs");
        }

        private static void Publish(string outputPath, Action<TextWriter> write)
        {
            if(string.IsNullOrEmpty(outputPath)||!Path.IsPathRooted(outputPath))
                throw new ArgumentException("The bounded output path must be absolute.","outputPath");
            string full=Path.GetFullPath(outputPath), directory=Path.GetDirectoryName(full), name=Path.GetFileName(full);
            if(string.IsNullOrEmpty(name))throw new ArgumentException("The bounded output needs a filename.","outputPath");
            var publisher=new NoReplacePublisher();
            using(NoReplacePublisher.StagedPublicationSet staged=publisher.StageAll(directory,
                new[]{name},writers=>write(writers[0]))) { staged.Publish(); }
        }

        private sealed class FileEvidence
        {
            internal long ByteCount; internal string Sha256;
        }
        /// <summary>Hashes the one descriptor that the JSONL reader consumes.
        /// It deliberately rejects byte forms StreamReader would otherwise
        /// normalize (BOM, CRLF and missing final LF).</summary>
        private sealed class HashingReadStream : Stream
        {
            private readonly Stream inner; private readonly SHA256 hash=SHA256.Create();
            private long count; private int first=-1,last=-1; private bool finished;
            internal HashingReadStream(Stream value) { inner=value; }
            public override int Read(byte[] buffer,int offset,int length)
            {
                int read=inner.Read(buffer,offset,length);
                if(read==0)return 0;
                for(int i=0;i<read;i++)
                {
                    int value=buffer[offset+i];if(first<0)first=value;last=value;
                    if(value==13)throw Invalid("raw contains CR; JSONL requires LF only");
                }
                hash.TransformBlock(buffer,offset,read,buffer,offset);count+=read;return read;
            }
            internal FileEvidence Finish()
            {
                if(finished)throw Invalid("raw hash already finalized");finished=true;
                if(count==0||last!=10)throw Invalid("raw requires one terminal LF");
                if(first==0xEF)throw Invalid("raw UTF-8 BOM is forbidden");
                hash.TransformFinalBlock(new byte[0],0,0);
                return new FileEvidence { ByteCount=count,Sha256=Hex(hash.Hash) };
            }
            public override bool CanRead { get { return inner.CanRead; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { return inner.Length; } }
            public override long Position { get { return inner.Position; } set { throw new NotSupportedException(); } }
            public override void Flush() { }
            public override long Seek(long offset,SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer,int offset,int count) { throw new NotSupportedException(); }
            protected override void Dispose(bool disposing)
            { if(disposing){hash.Dispose();inner.Dispose();}base.Dispose(disposing); }
        }
        private static FileEvidence DigestFile(string path,string label)
        {
            RequireExisting(path,label);
            using(var digest=SHA256.Create()) using(var input=File.OpenRead(path))
            {
                long count=0;var buffer=new byte[65536];int read;
                while((read=input.Read(buffer,0,buffer.Length))>0)
                { digest.TransformBlock(buffer,0,read,buffer,0);count+=read; }
                digest.TransformFinalBlock(new byte[0],0,0);
                return new FileEvidence { ByteCount=count,Sha256=Hex(digest.Hash) };
            }
        }
        private static string StagePath(string outputPath)
        {
            if(string.IsNullOrEmpty(outputPath)||!Path.IsPathRooted(outputPath))
                throw new ArgumentException("The bounded output path must be absolute.","outputPath");
            string directory=Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if(string.IsNullOrEmpty(directory)||!Directory.Exists(directory))
                throw Invalid("bounded output directory is absent");
            return Path.Combine(directory,".s2-request-window-"+Guid.NewGuid().ToString("N")+".stage");
        }
        private static void TryDelete(string path)
        { if(!string.IsNullOrEmpty(path)&&File.Exists(path))File.Delete(path); }
        private static void RequireExisting(string path,string label)
        { if(string.IsNullOrEmpty(path)||!Path.IsPathRooted(path)||!File.Exists(path))
            throw Invalid(label+" path must be an existing absolute file"); }
        private static JObject ReadObject(string path,string label)
        {
            RequireExisting(path,label);
            using(var input=new StreamReader(File.OpenRead(path),new UTF8Encoding(false,true),false,65536))
            using(var reader=new JsonTextReader(input))
            {
                try
                {
                    JObject result=Object(JObject.Load(reader,new JsonLoadSettings {
                        DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error }),label);
                    if(reader.Read())throw Invalid(label+" has trailing record");
                    return result;
                }
                catch(InvalidDataException) { throw; }
                catch(Exception error) { throw Invalid(label+" is not strict JSON",error); }
            }
        }
        private static string ReadLine(StreamReader input,string label)
        { string line=input.ReadLine();if(line==null)throw Invalid(label+" ended early");return line; }
        private static JObject ParseLine(string line,string label)
        { try{using(var reader=new JsonTextReader(new StringReader(line)))
          { return Object(JObject.Load(reader,new JsonLoadSettings {
              DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error }),label); }}catch(Exception error)
          {throw Invalid(label+" is not strict JSON",error);} }
        private static JObject Object(JToken value,string label)
        { JObject result=value as JObject;if(result==null)throw Invalid(label+" is not an object");return result; }
        private static JArray Array(JObject value,string name)
        { JArray result=value[name] as JArray;if(result==null)throw Invalid(name+" is not an array");return result; }
        private static string String(JObject value,string name)
        { JToken token=value[name];if(token==null||token.Type!=JTokenType.String||string.IsNullOrEmpty((string)token))throw Invalid("missing string: "+name);return (string)token; }
        private static bool Boolean(JObject value,string name)
        { JToken token=value[name];if(token==null||token.Type!=JTokenType.Boolean)throw Invalid("missing boolean: "+name);return token.Value<bool>(); }
        private static bool Null(JObject value,string name)
        { JToken token=value[name];if(token==null)throw Invalid("missing property: "+name);return token.Type==JTokenType.Null; }
        private static int Integer(JObject value,string name)
        { JToken token=value[name];if(token==null||token.Type!=JTokenType.Integer)throw Invalid("missing integer: "+name);try{return token.Value<int>();}catch(Exception){throw Invalid("integer range differs: "+name);} }
        private static long Integer64(JObject value,string name)
        { JToken token=value[name];if(token==null||token.Type!=JTokenType.Integer)throw Invalid("missing integer: "+name);try{return token.Value<long>();}catch(Exception){throw Invalid("integer range differs: "+name);} }
        private static uint Unsigned(JObject value,string name)
        { JToken token=value[name];if(token==null||token.Type!=JTokenType.Integer)throw Invalid("missing unsigned integer: "+name);try{return token.Value<uint>();}catch(Exception){throw Invalid("unsigned range differs: "+name);} }
        private static byte Byte(JObject value,string name)
        { int result=Integer(value,name);if(result<0||result>255)throw Invalid(name+" is outside byte range");return (byte)result; }
        private static void State(JObject value,string name)
        { string state=String(value,name);if(state.Length!=0x4000)throw Invalid("state snapshot differs");for(int i=0;i<state.Length;i++)if(!Uri.IsHexDigit(state[i]))throw Invalid("state snapshot is not hex"); }
        private static byte[] StateBytes(string state)
        { if(state.Length!=0x4000)throw Invalid("state snapshot differs");var bytes=new byte[0x2000];for(int i=0;i<bytes.Length;i++){int high=Nibble(state[i*2]),low=Nibble(state[i*2+1]);bytes[i]=(byte)((high<<4)|low);}return bytes; }
        private static int Nibble(char value)
        { if(value>='0'&&value<='9')return value-'0';if(value>='a'&&value<='f')return value-'a'+10;throw Invalid("state snapshot is not lowercase hex"); }
        private static void Exact(JObject value,string label,params string[] names)
        { var expected=new HashSet<string>(names,StringComparer.Ordinal);if(value.Count!=expected.Count)throw Invalid(label+" has unknown or missing property");foreach(string name in names)if(value[name]==null)throw Invalid(label+" is missing property: "+name);foreach(JProperty property in value.Properties())if(!expected.Contains(property.Name))throw Invalid(label+" has unknown property: "+property.Name); }
        private static byte[] Canonical(JToken value)
        { return Encoding.UTF8.GetBytes(value.ToString(Formatting.None)+"\n"); }
        private static byte[] Sha256(byte[] value)
        { using(SHA256 digest=SHA256.Create())return digest.ComputeHash(value); }
        private static string Hex(byte[] value)
        { var output=new StringBuilder(value.Length*2);foreach(byte item in value)output.Append(item.ToString("x2"));return output.ToString(); }
        private static void HexString(string value)
        { if(value.Length!=64)throw Invalid("digest length differs");for(int i=0;i<value.Length;i++)if(!Uri.IsHexDigit(value[i])||char.IsUpper(value[i]))throw Invalid("digest differs"); }
        private static void HexData(string value)
        { if((value.Length&1)!=0)throw Invalid("hex data length differs");for(int i=0;i<value.Length;i++)if(!Uri.IsHexDigit(value[i])||char.IsUpper(value[i]))throw Invalid("hex data differs"); }
        private static string StrictUtf8(byte[] value,string label)
        { try{return new UTF8Encoding(false,true).GetString(value);}catch(DecoderFallbackException error){throw Invalid(label+" is not strict UTF-8",error);} }
        private static void Write(TextWriter output,JObject value)
        { output.Write(value.ToString(Formatting.None));output.Write('\n'); }
        private static void Require(bool condition,string message)
        { if(!condition)throw Invalid(message); }
        private static InvalidDataException Invalid(string message)
        { return new InvalidDataException("S2 request-aware extractor: "+message); }
        private static InvalidDataException Invalid(string message,Exception error)
        { return new InvalidDataException("S2 request-aware extractor: "+message,error); }
    }
}
