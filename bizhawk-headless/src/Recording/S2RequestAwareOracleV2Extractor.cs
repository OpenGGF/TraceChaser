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
            "8dee3a7b11bc7df8748c3cf61a2a6bca0137127d7d5178e945ad86ddfa82645d";
        private const string CandidatePatchSha256 =
            "c857b5297ce8252e41a85d868466280931d700964dbd4082575c61d1ddc34099";
        private const string CandidateRecipeSha256 =
            "39f4f96c04a8b924921ef136bb85f8b402fa443b025f4189ff1f7c386f07feb3";
        private const string CandidateProfileSha256 =
            "740dff2f4a6ae04dc84fe08c8b5e33b084c417d4beeeaeb04843fa0320139d88";
        private readonly int sourceFirst, sourceEnd, windowFirst, windowEnd;
        private readonly bool syntheticTestSeam;
        private readonly string serviceManifestPath;

        internal S2RequestAwareOracleV2Extractor()
            : this(DefaultSourceFirst, DefaultSourceEnd,
                DefaultWindowFirst, DefaultWindowEnd, false, null) { }

        private S2RequestAwareOracleV2Extractor(int sourceStart, int sourceStop,
            int windowStart, int windowStop, bool testing,
            string manifestPath)
        {
            if (sourceStart < 0 || sourceStop <= sourceStart
                || windowStart <= sourceStart || windowStop > sourceStop
                || windowStop <= windowStart)
                throw new ArgumentException("The S2 request-aware bounds are invalid.");
            sourceFirst = sourceStart; sourceEnd = sourceStop;
            windowFirst = windowStart; windowEnd = windowStop;
            syntheticTestSeam=testing;
            serviceManifestPath=manifestPath;
        }

        internal static S2RequestAwareOracleV2Extractor ForTesting(
            int sourceStart, int sourceStop, int windowStart, int windowStop,
            string manifestPath)
        { return new S2RequestAwareOracleV2Extractor(sourceStart, sourceStop,
            windowStart, windowStop, true, manifestPath); }

        /// <summary>Friend-test validation of the committed candidate shape.
        /// It deliberately does not create an extraction or publication route.</summary>
        internal static void ValidateCandidateTemplateForTesting(string path)
        {
            var extractor=new S2RequestAwareOracleV2Extractor(
                DefaultSourceFirst,DefaultSourceEnd,DefaultWindowFirst,
                DefaultWindowEnd,true,null);
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
            byte[] serviceManifestBytes=VerifiedBytes(serviceManifestPath,
                "service manifest",
                S2AudioObserverProfile.ServiceManifestSha256);
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
            CompleteRunAudioObserver.CutoffFrontier baseline =
                ValidateBaseline(firstBaseline, sourceFirst, false);
            var replayApi = new RawReplayApi();
            CompleteRunAudioObserver replay =
                GpgxAudioServiceManifest.LoadS2RequestCandidate(
                    serviceManifestBytes,
                    new S2AudioObserverProfile.PrepublicationApi(replayApi));
            try { replay.RestorePublicationBaselineForValidation(baseline,
                sourceFirst); }
            catch(Exception error)
            { throw Invalid("baseline lifecycle differs",error); }
            int latch0 = replay.YmPort0Address;
            int latch1 = replay.YmPort1Address;
            int precedingLatch0 = 0, precedingLatch1 = 0;
            JObject preceding = null;
            long baseCount = 0, allCount = 0, markerCount = 0, requestCount = 0;
            int occupancy = 0, nextGlobal = 0, expectedRow = sourceFirst;
            var resumePcm = new ResumePcmValidator();
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
                        CompleteRunAudioObserver.CutoffFrontier expectedCutoff =
                            ValidateCutoff(value, sourceEnd);
                        Require(FrontiersEqual(expectedCutoff,
                            replay.CaptureCutoffFrontier()),
                            "raw cutoff frontier differs from replay");
                        resumePcm.Complete();
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
                        Require(resumePcm.ResumeCount
                                ==Integer(capability,"override_resume_count")
                            &&resumePcm.PcmCount==Integer(capability,"pcm_count")
                            &&resumePcm.ResumeDigest
                                ==String(capability,"override_resume_sha256")
                            &&resumePcm.PcmDigest
                                ==String(capability,"pcm_sha256"),
                            "raw override/PCM inventory differs from capability");
                        if (preceding == null || expectedRow != sourceEnd)
                            throw Invalid("raw has no complete bounded window");
                        return new Projection(metadata, preceding, precedingLatch0,
                            precedingLatch1, capability, raw, value);
                    }
                    ValidateFrame(value, expectedRow);
                    JArray events = Array(value, "events");
                    var nativeEvents = new GpgxAudioTraceEvent[events.Count];
                    var markers = new Dictionary<uint, JObject>();
                    uint nativeOrdinal=0;
                    for (int eventIndex=0;eventIndex<events.Count;eventIndex++)
                    {
                        JToken token=events[eventIndex];
                        JObject evt = Object(token, "event"); ValidateEvent(evt);
                        nativeEvents[eventIndex]=NativeEvent(evt);
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
                    }
                    replayApi.Queue(nativeEvents);
                    CompleteRunAudioObserver.FrameCapture replayed;
                    try
                    {
                        replayed=replay.CaptureCanonicalFrame(expectedRow,
                            () => { });
                    }
                    catch(Exception error)
                    {
                        throw Invalid("native ABI/lifecycle differs",error);
                    }
                    latch0=replay.YmPort0Address;
                    latch1=replay.YmPort1Address;
                    resumePcm.Frame(value,replayed,expectedRow);
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
            int latch0=projection.Latch0,latch1=projection.Latch1;
            Publish(outputPath, writer => {
            JObject boundedMetadata=new JObject {
                ["type"]="metadata", ["schema"]=OracleSchema,
                ["rom_sha1"]=String(metadata,"rom_sha1"),
                ["bk2_sha256"]=String(metadata,"bk2_sha256"),
                ["service_manifest_sha256"]=String(metadata,"service_manifest_sha256"),
                ["first_row"]=windowFirst, ["exclusive_end"]=windowEnd,
                ["state_start"]=0, ["state_exclusive_end"]=0x2000,
                ["source_schema"]=RawSchema, ["source_first_row"]=sourceFirst,
                ["source_exclusive_end"]=sourceEnd,
                ["request_transfer_schema"]="openggf.s2-preconsumption-request-transfer.v1",
                ["production_bound"]=false,
                ["digest_domains"]=new JObject {
                    ["inventories"]="compact-json-lf-v1",
                    ["body"]="bounded-jsonl-body-bytes-v1",
                    ["terminal_state"]="decoded-z80-state-bytes-v1",
                    ["payload_before_cutoff"]="bounded-jsonl-before-cutoff-bytes-v1" } };
            JObject baseline=new JObject { ["type"]="baseline", ["row"]=windowFirst,
                ["source_preceding_row"]=windowFirst-1,
                ["state_hex"]=String(preceding,"state_hex"),
                ["ym_port0_latch"]=latch0, ["ym_port1_latch"]=latch1 };
            using(var evidence=new BoundedEvidence(windowFirst,windowEnd))
            {
            using(SHA256 payload=SHA256.Create())
            {
            Append(payload,Write(writer,boundedMetadata));
            byte[] baselineBytes=Write(writer,baseline);
            Append(payload,baselineBytes);evidence.Baseline(baselineBytes);
            using(var input=new StreamReader(File.OpenRead(stagedWindow),
                new UTF8Encoding(false,true),false,65536))
            {
                string line;while((line=input.ReadLine())!=null)
                {
                    JObject frame=ParseLine(line,"bounded frame");
                    evidence.Frame(frame);
                    Append(payload,Write(writer,frame));
                }
            }
            payload.TransformFinalBlock(new byte[0],0,0);
            Write(writer,evidence.Cutoff(Hex(payload.Hash)));
            }
            }
            });
        }

        /// <summary>
        /// Builds only claims that the bounded raw-v2 body itself can prove.
        /// Raw-v3/capability/attestation provenance remains in the reviewed
        /// pre-publication validation path and is deliberately not copied here.
        /// </summary>
        private sealed class BoundedEvidence : IDisposable
        {
            private readonly int first,end;
            private readonly SHA256 baseDigest=SHA256.Create();
            private readonly SHA256 allDigest=SHA256.Create();
            private readonly SHA256 markerDigest=SHA256.Create();
            private readonly SHA256 requestDigest=SHA256.Create();
            private readonly SHA256 resumeDigest=SHA256.Create();
            private readonly SHA256 pcmDigest=SHA256.Create();
            private readonly SHA256 bodyDigest=SHA256.Create();
            private long frameCount,baseCount,allCount,markerCount,requestCount;
            private long resumeCount,pcmCount,bodyBytes;
            private int occupancy;
            private byte[] terminalState;

            internal BoundedEvidence(int firstRow,int exclusiveEnd)
            { first=firstRow;end=exclusiveEnd; }

            internal void Baseline(byte[] bytes) { Body(bytes); }

            internal void Frame(JObject value)
            {
                ValidateFrame(value,first+(int)frameCount);
                Body(Canonical(value));frameCount++;
                terminalState=StateBytes(String(value,"state_hex"));
                JArray events=Array(value,"events");
                foreach(JToken token in events)
                {
                    JObject evt=Object(token,"bounded event");byte[] bytes=Canonical(evt);
                    Append(allDigest,bytes);allCount++;
                    if(Marker(evt))
                    {
                        Require(Byte(evt,"payload_length")==4
                            &&UShort(evt,"offset")==0&&Byte(evt,"flags")==0
                            &&Byte(evt,"reserved")==0,
                            "bounded action-7 marker shape differs");
                        Append(markerDigest,bytes);markerCount++;
                    }
                    else { Append(baseDigest,bytes);baseCount++; }
                }
                JArray transfers=Array(value,"request_transfers");
                occupancy=Math.Max(occupancy,transfers.Count);
                foreach(JToken token in transfers)
                { Append(requestDigest,Canonical(token));requestCount++; }
                JToken resume=value["override_resume"];
                if(resume!=null&&resume.Type!=JTokenType.Null)
                { Append(resumeDigest,Canonical(resume));resumeCount++; }
                JToken pcm=value["pcm"];
                if(pcm!=null&&pcm.Type!=JTokenType.Null)
                { Append(pcmDigest,Canonical(pcm));pcmCount++; }
            }

            internal JObject Cutoff(string payloadBeforeCutoffSha256)
            {
                Require(frameCount==end-first&&terminalState!=null,
                    "bounded output frame inventory differs");
                return new JObject { ["type"]="cutoff", ["exclusive_end"]=end,
                    ["frame_count"]=frameCount, ["base_event_count"]=baseCount,
                    ["all_event_count"]=allCount, ["marker_event_count"]=markerCount,
                    ["request_transfer_count"]=requestCount,
                    ["override_resume_count"]=resumeCount,["pcm_count"]=pcmCount,
                    ["max_request_occupancy"]=occupancy,
                    ["base_event_sha256"]=Finish(baseDigest),
                    ["all_event_sha256"]=Finish(allDigest),
                    ["marker_event_sha256"]=Finish(markerDigest),
                    ["request_transfer_sha256"]=Finish(requestDigest),
                    ["override_resume_sha256"]=Finish(resumeDigest),
                    ["pcm_sha256"]=Finish(pcmDigest),
                    ["body_byte_count"]=bodyBytes,
                    ["body_sha256"]=Finish(bodyDigest),
                    ["terminal_state_sha256"]=Hex(Sha256(terminalState)),
                    ["payload_before_cutoff_sha256"]=payloadBeforeCutoffSha256 };
            }

            private void Body(byte[] bytes)
            { Append(bodyDigest,bytes);bodyBytes+=bytes.Length; }

            private static string Finish(SHA256 value)
            { value.TransformFinalBlock(new byte[0],0,0);return Hex(value.Hash); }

            public void Dispose()
            {
                baseDigest.Dispose();allDigest.Dispose();markerDigest.Dispose();
                requestDigest.Dispose();resumeDigest.Dispose();pcmDigest.Dispose();
                bodyDigest.Dispose();
            }
        }

        private static CompleteRunAudioObserver.CutoffFrontier ValidateBaseline(
            JObject value, int row,
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
                long epoch=Integer64(value,"native_arm_epoch");
                Require(epoch>0,"baseline arm epoch differs");
                Boolean(value,"native_armed");
                List<CompleteRunAudioObserver.ServiceBuilder> active =
                    ParseServices(Array(value,"active_services"),
                        "active services",false);
                List<CompleteRunAudioObserver.ServiceBuilder> pending =
                    ParseServices(Array(value,"pending_descendants"),
                        "pending descendants",true);
                return new CompleteRunAudioObserver.CutoffFrontier(active,
                    pending,Byte(value,"ym_port0_latch"),
                    Byte(value,"ym_port1_latch"),
                    epoch,
                    Boolean(value,"native_armed"));
            }
            return null;
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
        }

        private static void ValidateEvent(JObject value)
        {
            Exact(value,"event","ordinal","service_token","parent_token","pc",
                "subject","offset","kind","service_kind","depth","source_cpu",
                "payload_length","value","flags","reserved","payload");
            Unsigned(value,"ordinal"); UShort(value,"service_token");
            UShort(value,"parent_token");Unsigned(value,"pc");
            UShort(value,"subject");UShort(value,"offset");
            Byte(value,"kind"); Byte(value,"service_kind");
            Byte(value,"depth"); Byte(value,"source_cpu"); Byte(value,"payload_length");
            Byte(value,"value"); Byte(value,"flags"); Byte(value,"reserved");
            ulong parsed; if (!ulong.TryParse(String(value,"payload"), out parsed)
                || String(value,"payload")!=parsed.ToString())
                throw Invalid("event payload differs");
        }

        private static GpgxAudioTraceEvent NativeEvent(JObject value)
        {
            ulong payload;
            if(!ulong.TryParse(String(value,"payload"),out payload))
                throw Invalid("event payload differs");
            return new GpgxAudioTraceEvent {
                Ordinal=Unsigned(value,"ordinal"),
                ServiceToken=UShort(value,"service_token"),
                ParentToken=UShort(value,"parent_token"),
                Pc=Unsigned(value,"pc"),Subject=UShort(value,"subject"),
                Offset=UShort(value,"offset"),Kind=Byte(value,"kind"),
                ServiceKindId=Byte(value,"service_kind"),
                Depth=Byte(value,"depth"),SourceCpu=Byte(value,"source_cpu"),
                PayloadLength=Byte(value,"payload_length"),
                Value=Byte(value,"value"),Flags=Byte(value,"flags"),
                Reserved=Byte(value,"reserved"),Payload=payload };
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
                && ReviewedTransferOwner(value),
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
                || Integer(marker,"service_token")
                    !=Integer(value,"service_token")
                || Byte(marker,"service_kind")!=Byte(value,"service_kind")
                || Byte(marker,"depth")!=Byte(value,"depth")
                || String(marker,"payload") != String(value,"a7"))
                throw Invalid("request transfer/action-7 marker differs");
        }

        private static bool ReviewedTransferOwner(JObject value)
        {
            int token=Integer(value,"service_token");
            int kind=Byte(value,"service_kind");
            int depth=Byte(value,"depth");
            return (token==S2PreconsumptionRequestObserver.MarkerServiceToken
                    &&kind==S2PreconsumptionRequestObserver.MarkerServiceKind
                    &&depth==S2PreconsumptionRequestObserver.MarkerDepth)
                ||(token!=S2PreconsumptionRequestObserver.MarkerServiceToken
                    &&kind==S2PreconsumptionRequestObserver.Kind3MarkerServiceKind
                    &&depth==S2PreconsumptionRequestObserver.MarkerDepth);
        }

        private static bool Marker(JObject value)
        {
            bool common=Byte(value,"kind")==10 && Unsigned(value,"value")==3
            && Unsigned(value,"pc")==S2PreconsumptionRequestObserver.Pc
            && Byte(value,"source_cpu")==S2PreconsumptionRequestObserver.MarkerSourceCpu
            && Byte(value,"payload_length")==4;
            bool root=Unsigned(value,"subject")
                    ==S2PreconsumptionRequestObserver.MarkerToken
                &&Integer(value,"service_token")
                    ==S2PreconsumptionRequestObserver.MarkerServiceToken
                &&Integer(value,"parent_token")
                    ==S2PreconsumptionRequestObserver.MarkerServiceToken
                &&Byte(value,"service_kind")
                    ==S2PreconsumptionRequestObserver.MarkerServiceKind
                &&Byte(value,"depth")==S2PreconsumptionRequestObserver.MarkerDepth;
            bool kind3=Unsigned(value,"subject")
                    ==S2PreconsumptionRequestObserver.Kind3MarkerToken
                &&Integer(value,"service_token")
                    !=S2PreconsumptionRequestObserver.MarkerServiceToken
                &&Integer(value,"parent_token")
                    ==S2PreconsumptionRequestObserver.MarkerServiceToken
                &&Byte(value,"service_kind")
                    ==S2PreconsumptionRequestObserver.Kind3MarkerServiceKind
                &&Byte(value,"depth")==S2PreconsumptionRequestObserver.MarkerDepth;
            return common&&(root||kind3);
        }
        private static bool MarkerCandidate(JObject value)
        { return Unsigned(value,"subject")==S2PreconsumptionRequestObserver.MarkerToken
            || Unsigned(value,"subject")
                ==S2PreconsumptionRequestObserver.Kind3MarkerToken
            || (Byte(value,"kind")==10 && Unsigned(value,"value")==3
                && Unsigned(value,"pc")==S2PreconsumptionRequestObserver.Pc); }

        private static void FoldLatch(JObject value, ref int latch0, ref int latch1)
        {
            if (Byte(value,"kind")==8) { latch0=0;latch1=0;return; }
            if (Byte(value,"kind")!=3) return;
            int subject=Integer(value,"subject"), data=Integer(value,"value");
            if(subject==0)latch0=data; else if(subject==2)latch1=data;
        }

        private static CompleteRunAudioObserver.CutoffFrontier ValidateCutoff(
            JObject value, int end)
        {
            Exact(value,"cutoff","type","state_hex","ym_port0_latch",
                "ym_port1_latch","native_arm_epoch","native_armed",
                "active_services","pending_descendants","exclusive_end");
            Require(String(value,"type")=="cutoff" && Integer(value,"exclusive_end")==end,
                "raw cutoff differs"); State(value,"state_hex");
            Byte(value,"ym_port0_latch"); Byte(value,"ym_port1_latch");
            Require(Integer64(value,"native_arm_epoch")>0,
                "cutoff arm epoch differs");
            Boolean(value,"native_armed");
            List<CompleteRunAudioObserver.ServiceBuilder> active =
                ParseServices(Array(value,"active_services"),
                    "active services",false);
            List<CompleteRunAudioObserver.ServiceBuilder> pending =
                ParseServices(Array(value,"pending_descendants"),
                    "pending descendants",true);
            return new CompleteRunAudioObserver.CutoffFrontier(active,pending,
                Byte(value,"ym_port0_latch"),Byte(value,"ym_port1_latch"),
                Integer64(value,"native_arm_epoch"),
                Boolean(value,"native_armed"));
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
                && Unsigned(resume,"pc")==0x0DB4
                && Integer(resume,"fix_driver_bugs")==0
                && Boolean(resume,"restores_saved_priority")
                && !Boolean(resume,"restores_psg_noise"),
                "override resume identity differs");
            UShort(resume,"service_token"); Unsigned(resume,"service_begin_ordinal");
            Unsigned(resume,"native_ordinal"); Integer(resume,"frame");
            JArray writes=Array(resume,"writes"); Require(writes.Count>0,"override resume has no writes");
            foreach(JToken write in writes)
                ValidateOverrideWrite(Object(write,"override write"));
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
                && Integer(pcm,"sample_rate")==44100 && Integer(pcm,"channels")==2
                && String(pcm,"format")=="s16le-interleaved-stereo"
                && Integer64(pcm,"stereo_frames")>=0 && Integer64(pcm,"byte_count")>=0,
                "PCM identity differs");
            HexData(String(pcm,"pcm_hex")); HexString(String(pcm,"sha256"));
            Require(String(pcm,"pcm_hex").Length==Integer64(pcm,"byte_count")*2,
                "PCM byte count differs");
        }

        private static List<CompleteRunAudioObserver.ServiceBuilder>
            ParseServices(JArray services,string label,bool complete)
        {
            var result=new List<CompleteRunAudioObserver.ServiceBuilder>();
            var seen=new HashSet<ushort>();
            long previousBegin=-1;
            foreach(JToken token in services)
            {
                JObject service=Object(token,label); Exact(service,label,"token","parent_token",
                    "kind","depth","current_parent_token","current_depth","begin_coordinate",
                    "begin_row","begin_native_ordinal","end_coordinate","begin_pc","end_pc",
                    "begin_hook_token","end_hook_token","begin_source_cpu","cancelled","complete",
                    "chips","snapshots","ancestry_transitions");
                ushort serviceToken=UShort(service,"token");
                Require(seen.Add(serviceToken),label+" has duplicate service token");
                Require(Boolean(service,"complete")==complete,
                    label+" completion state differs");
                var value=new CompleteRunAudioObserver.ServiceBuilder {
                    Token=serviceToken,
                    ParentToken=UShort(service,"parent_token"),
                    Kind=Byte(service,"kind"),Depth=Byte(service,"depth"),
                    CurrentParentToken=UShort(service,"current_parent_token"),
                    CurrentDepth=Byte(service,"current_depth"),
                    BeginCoordinate=Integer64(service,"begin_coordinate"),
                    BeginRow=Integer(service,"begin_row"),
                    BeginNativeOrdinal=Unsigned(service,"begin_native_ordinal"),
                    EndCoordinate=Integer64(service,"end_coordinate"),
                    BeginPc=Unsigned(service,"begin_pc"),
                    EndPc=Unsigned(service,"end_pc"),
                    BeginHookToken=UShort(service,"begin_hook_token"),
                    EndHookToken=UShort(service,"end_hook_token"),
                    BeginSourceCpu=Byte(service,"begin_source_cpu"),
                    Cancelled=Boolean(service,"cancelled") };
                foreach(JToken chipToken in Array(service,"chips"))
                {
                    JObject chip=Object(chipToken,"chip");ValidateChip(chip);
                    byte eventKind=Byte(chip,"event_kind");
                    byte subject=Byte(chip,"subject");
                    bool data=Boolean(chip,"data");
                    Require((eventKind==3||eventKind==4)
                        &&(eventKind!=3||subject<=3)
                        &&(eventKind!=4||subject==0)
                        &&data==(eventKind==4||subject==1||subject==3),
                        "chip shape differs");
                    value.AddChip(new CompleteRunAudioObserver.WriteRecord {
                        Coordinate=Integer64(chip,"coordinate"),
                        Ordinal=Unsigned(chip,"native_ordinal"),
                        Pc=Unsigned(chip,"pc"),Token=serviceToken,
                        Kind=eventKind,Subject=subject,
                        Value=Byte(chip,"value"),
                        SourceCpu=Byte(chip,"source_cpu"),
                        Port=Byte(chip,"port"),
                        Register=Byte(chip,"register") });
                }
                foreach(JToken snapshotToken in Array(service,"snapshots"))
                {
                    JObject snapshot=Object(snapshotToken,"snapshot");
                    ValidateSnapshot(snapshot);
                    byte[] bytes=DecodeHex(String(snapshot,"bytes_hex"),
                        "snapshot bytes");
                    value.AddSnapshot(new CompleteRunAudioObserver.SnapshotRecord {
                        RangeId=UShort(snapshot,"range_id"),
                        SourceCpu=Byte(snapshot,"source_cpu"),
                        Pc=Unsigned(snapshot,"pc"),Bytes=bytes,
                        Length=bytes.Length });
                }
                foreach(JToken transitionToken in
                    Array(service,"ancestry_transitions"))
                {
                    JObject transition=Object(transitionToken,
                        "ancestry transition");
                    ValidateTransition(transition);
                    value.AddAncestry(
                        new CompleteRunAudioObserver.AncestryRecord {
                            Coordinate=Integer64(transition,"coordinate"),
                            NativeOrdinal=Unsigned(transition,"native_ordinal"),
                            PreviousParentToken=UShort(transition,
                                "previous_parent_token"),
                            PreviousDepth=Byte(transition,"previous_depth"),
                            CurrentParentToken=UShort(transition,
                                "current_parent_token"),
                            CurrentDepth=Byte(transition,"current_depth"),
                            HookToken=UShort(transition,"hook_token"),
                            SourceCpu=Byte(transition,"source_cpu"),
                            Pc=Unsigned(transition,"pc") });
                }
                if(result.Count!=0&&value.BeginCoordinate<=previousBegin)
                    throw Invalid(label+" begin order differs");
                previousBegin=value.BeginCoordinate;
                result.Add(value);
            }
            return result;
        }

        private static bool FrontiersEqual(
            CompleteRunAudioObserver.CutoffFrontier expected,
            CompleteRunAudioObserver.CutoffFrontier actual)
        {
            if(expected==null||actual==null
                ||expected.YmPort0Address!=actual.YmPort0Address
                ||expected.YmPort1Address!=actual.YmPort1Address
                ||expected.ArmEpoch!=actual.ArmEpoch
                ||expected.IsArmed!=actual.IsArmed
                ||expected.PendingDeferredBegin!=null
                ||actual.PendingDeferredBegin!=null
                ||expected.ActiveServices.Count!=actual.ActiveServices.Count
                ||expected.PendingServices.Count!=actual.PendingServices.Count)
                return false;
            for(int index=0;index<expected.ActiveServices.Count;index++)
                if(!ServicesEqual(expected.ActiveServices[index],
                    actual.ActiveServices[index]))return false;
            for(int index=0;index<expected.PendingServices.Count;index++)
                if(!ServicesEqual(expected.PendingServices[index],
                    actual.PendingServices[index]))return false;
            return true;
        }

        private static bool ServicesEqual(
            CompleteRunAudioObserver.DriverService expected,
            CompleteRunAudioObserver.DriverService actual)
        {
            if(expected.Token!=actual.Token
                ||expected.ParentToken!=actual.ParentToken
                ||expected.Kind!=actual.Kind||expected.Depth!=actual.Depth
                ||expected.CurrentParentToken!=actual.CurrentParentToken
                ||expected.CurrentDepth!=actual.CurrentDepth
                ||expected.BeginCoordinate!=actual.BeginCoordinate
                ||expected.BeginRow!=actual.BeginRow
                ||expected.BeginNativeOrdinal!=actual.BeginNativeOrdinal
                ||expected.EndCoordinate!=actual.EndCoordinate
                ||expected.BeginPc!=actual.BeginPc||expected.EndPc!=actual.EndPc
                ||expected.BeginHookToken!=actual.BeginHookToken
                ||expected.EndHookToken!=actual.EndHookToken
                ||expected.BeginSourceCpu!=actual.BeginSourceCpu
                ||expected.Cancelled!=actual.Cancelled
                ||expected.IsComplete!=actual.IsComplete
                ||expected.OwnedChipEvents.Count!=actual.OwnedChipEvents.Count
                ||expected.Snapshots.Count!=actual.Snapshots.Count
                ||expected.AncestryTransitions.Count
                    !=actual.AncestryTransitions.Count)return false;
            for(int index=0;index<expected.OwnedChipEvents.Count;index++)
            {
                CompleteRunAudioObserver.OwnedChipEvent left=
                    expected.OwnedChipEvents[index];
                CompleteRunAudioObserver.OwnedChipEvent right=
                    actual.OwnedChipEvents[index];
                if(left.Coordinate!=right.Coordinate
                    ||left.NativeOrdinal!=right.NativeOrdinal
                    ||left.EventKind!=right.EventKind
                    ||left.Subject!=right.Subject||left.Value!=right.Value
                    ||left.Pc!=right.Pc||left.SourceCpu!=right.SourceCpu
                    ||left.IsData!=right.IsData||left.Port!=right.Port
                    ||left.Register!=right.Register)return false;
            }
            for(int index=0;index<expected.Snapshots.Count;index++)
            {
                CompleteRunAudioObserver.SnapshotGroup left=
                    expected.Snapshots[index];
                CompleteRunAudioObserver.SnapshotGroup right=
                    actual.Snapshots[index];
                if(left.RangeId!=right.RangeId
                    ||left.SourceCpu!=right.SourceCpu||left.Pc!=right.Pc
                    ||!BytesEqual(left.Bytes,right.Bytes))return false;
            }
            for(int index=0;index<expected.AncestryTransitions.Count;index++)
            {
                CompleteRunAudioObserver.AncestryTransition left=
                    expected.AncestryTransitions[index];
                CompleteRunAudioObserver.AncestryTransition right=
                    actual.AncestryTransitions[index];
                if(left.Coordinate!=right.Coordinate
                    ||left.NativeOrdinal!=right.NativeOrdinal
                    ||left.PreviousParentToken!=right.PreviousParentToken
                    ||left.PreviousDepth!=right.PreviousDepth
                    ||left.CurrentParentToken!=right.CurrentParentToken
                    ||left.CurrentDepth!=right.CurrentDepth
                    ||left.HookToken!=right.HookToken
                    ||left.SourceCpu!=right.SourceCpu||left.Pc!=right.Pc)
                    return false;
            }
            return true;
        }

        private static bool BytesEqual(byte[] left,byte[] right)
        {
            if(left==null||right==null||left.Length!=right.Length)return false;
            for(int index=0;index<left.Length;index++)
                if(left[index]!=right[index])return false;
            return true;
        }

        private static void ValidateChip(JObject value)
        { Exact(value,"chip","coordinate","native_ordinal","event_kind","subject","value","pc","source_cpu","data","port","register");
          Integer64(value,"coordinate");Unsigned(value,"native_ordinal");Byte(value,"event_kind");Byte(value,"subject");Byte(value,"value");Unsigned(value,"pc");Byte(value,"source_cpu");Boolean(value,"data");Byte(value,"port");Byte(value,"register"); }
        private static void ValidateOverrideWrite(JObject value)
        { Exact(value,"override write","native_ordinal","event_kind","subject","value","pc","source_cpu","data","port","register");
          Unsigned(value,"native_ordinal");Byte(value,"event_kind");Byte(value,"subject");Byte(value,"value");Unsigned(value,"pc");Byte(value,"source_cpu");Boolean(value,"data");Byte(value,"port");Byte(value,"register"); }
        private static void ValidateSnapshot(JObject value)
        { Exact(value,"snapshot","range_id","source_cpu","pc","bytes_hex");UShort(value,"range_id");Byte(value,"source_cpu");Unsigned(value,"pc");HexData(String(value,"bytes_hex")); }
        private static void ValidateTransition(JObject value)
        { Exact(value,"ancestry transition","coordinate","native_ordinal","previous_parent_token","previous_depth","current_parent_token","current_depth","hook_token","source_cpu","pc");Integer64(value,"coordinate");Unsigned(value,"native_ordinal");UShort(value,"previous_parent_token");Byte(value,"previous_depth");UShort(value,"current_parent_token");Byte(value,"current_depth");UShort(value,"hook_token");Byte(value,"source_cpu");Unsigned(value,"pc"); }

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
                "max_request_occupancy","override_resume_count",
                "override_resume_sha256","pcm_count","pcm_sha256",
                "cutoff_frontier_sha256","terminal_state_sha256",
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
                "override_resume_sha256","pcm_sha256",
                "cutoff_frontier_sha256","terminal_state_sha256");
            Require(String(domains,"raw_sha256")=="raw-file-bytes-v1"
                && String(domains,"event_and_request_sha256")=="compact-json-lf-v1"
                && String(domains,"override_resume_sha256")=="compact-json-lf-v1"
                && String(domains,"pcm_sha256")=="compact-json-lf-v1"
                && String(domains,"cutoff_frontier_sha256")=="compact-json-lf-v1"
                && String(domains,"terminal_state_sha256")=="decoded-z80-state-bytes-v1",
                "capability digest domains differ");
            bool allUnbound=Null(value,"harness_executable_sha256")
                && Null(value,"base_event_count") && Null(value,"all_event_count")
                && Null(value,"marker_event_count") && Null(value,"request_count")
                && Null(value,"base_event_sha256") && Null(value,"all_event_sha256")
                && Null(value,"marker_event_sha256") && Null(value,"request_sha256")
                && Null(value,"max_request_occupancy")
                && Null(value,"override_resume_count")
                && Null(value,"override_resume_sha256")
                && Null(value,"pcm_count")&&Null(value,"pcm_sha256")
                && Null(value,"cutoff_frontier_sha256")
                && Null(value,"terminal_state_sha256");
            if(allUnbound)return;
            Require(syntheticTestSeam,"unbound candidate has no authenticated inventory");
            foreach(string name in new[]{"harness_executable_sha256",
                "base_event_sha256","all_event_sha256","marker_event_sha256",
                "request_sha256","override_resume_sha256","pcm_sha256",
                "cutoff_frontier_sha256","terminal_state_sha256"})
                HexString(String(value,name));
            Require(String(value,"harness_executable_sha256")
                    ==DigestFile(typeof(GpgxHost).Assembly.Location,
                        "harness executable").Sha256,
                "capability executable identity differs");
            if(Integer64(value,"base_event_count")<0 || Integer64(value,"all_event_count")<0
                || Integer64(value,"marker_event_count")<0 || Integer64(value,"request_count")<0
                || Integer(value,"override_resume_count")<0
                || Integer(value,"override_resume_count")>1
                || Integer(value,"pcm_count")<0||Integer(value,"pcm_count")>1
                || Integer(value,"override_resume_count")!=Integer(value,"pcm_count")
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
        private static byte[] VerifiedBytes(string path,string label,
            string expectedSha256)
        {
            RequireExisting(path,label);
            byte[] bytes=File.ReadAllBytes(path);
            Require(Hex(Sha256(bytes))==expectedSha256,
                label+" identity differs");
            return bytes;
        }

        /// <summary>
        /// Replays parsed ABI-4 rows through the authoritative managed
        /// observer. It has no host, callback, capture, or publication route.
        /// </summary>
        private sealed class RawReplayApi : IGpgxAudioTraceApi
        {
            private GpgxAudioTraceEvent[] queued;
            private int phase;
            public uint AbiVersion { get { return 4; } }
            public uint EventSize { get { return 32; } }
            public uint Capacity { get { return 65536; } }
            internal void Queue(GpgxAudioTraceEvent[] events)
            {
                if(phase!=1||queued!=null)
                    throw Invalid("raw replay row overlap");
                queued=events??throw new ArgumentNullException("events");
            }
            public int Configure(ref GpgxAudioObserverAdapter.Config config,
                byte[] mask,GpgxAudioObserverAdapter.ServiceKind[] kinds,
                GpgxAudioObserverAdapter.ServiceHook[] hooks,
                GpgxAudioObserverAdapter.SnapshotRange[] ranges)
            {
                if(config.AbiVersion!=4||config.EventSize!=32
                    ||config.EventCapacity!=Capacity||config.Flags!=1)
                    return -3;
                phase=1;return 0;
            }
            public int BeginFrame()
            {if(phase!=1||queued==null)return -2;phase=2;return 0;}
            public int EndFrame()
            {if(phase!=2)return -2;phase=3;return 0;}
            public int EventCount(out uint count,out uint overflow)
            {count=phase==3?(uint)queued.Length:0;overflow=0;return phase==3?0:-2;}
            public int Drain(GpgxAudioTraceEvent[] events,uint capacity,
                out uint count)
            {
                if(phase!=3){count=0;return -2;}
                count=(uint)queued.Length;
                if(capacity<count||(count!=0&&events==null))return -3;
                if(count!=0)System.Array.Copy(queued,events,queued.Length);
                queued=null;phase=1;return 0;
            }
            public int GetFirstFault(
                out GpgxAudioObserverAdapter.FirstFault fault)
            {fault=new GpgxAudioObserverAdapter.FirstFault();return 0;}
            public int BeginPublicationEpoch(){return phase==1?0:-2;}
            public int AbortFrame(){queued=null;phase=1;return 0;}
            public int Disable(){queued=null;phase=0;return 0;}
        }

        private sealed class ResumePcmValidator
        {
            private JObject resume;
            private JObject pcm;
            private bool qualifyingSeen;
            private int resumeRow=-1;
            private bool awaitingFollowing;

            internal int ResumeCount { get { return resume==null?0:1; } }
            internal int PcmCount { get { return pcm==null?0:1; } }
            internal string ResumeDigest
            {get{return Hex(Sha256(resume==null?new byte[0]:Canonical(resume)));}}
            internal string PcmDigest
            {get{return Hex(Sha256(pcm==null?new byte[0]:Canonical(pcm)));}}

            internal void Frame(JObject frame,
                CompleteRunAudioObserver.FrameCapture capture,int row)
            {
                CompleteRunAudioObserver.DriverService selected=null;
                foreach(CompleteRunAudioObserver.DriverService service
                    in capture.Services)
                {
                    if(service.Kind!=9||service.Cancelled||!service.IsComplete
                        ||service.BeginPc!=0x0110||service.BeginHookToken!=21
                        ||service.EndPc!=0x0DB4||service.EndHookToken!=23
                        ||service.BeginSourceCpu!=1)continue;
                    if(selected!=null)
                        throw Invalid("override resume service is ambiguous");
                    selected=service;
                }

                JToken resumeToken=frame["override_resume"];
                if(resumeToken==null)
                    throw Invalid("v2 override envelope is incomplete");
                JObject rowResume=resumeToken.Type==JTokenType.Null
                    ?null:Object(resumeToken,"override resume");
                if(selected!=null&&!qualifyingSeen)
                {
                    if(rowResume==null)
                        throw Invalid("override resume selection is missing");
                    ValidateResume(rowResume,selected,capture,row);
                    resume=(JObject)rowResume.DeepClone();
                    resumeRow=row;qualifyingSeen=true;
                }
                else
                {
                    if(rowResume!=null)
                        throw Invalid("override resume selection is duplicated");
                    if(selected!=null)qualifyingSeen=true;
                }

                JToken pcmToken=frame["pcm"];
                if(pcmToken==null)
                    throw Invalid("v2 PCM envelope is incomplete");
                JObject rowPcm=pcmToken.Type==JTokenType.Null
                    ?null:Object(pcmToken,"PCM");
                if(rowResume!=null)
                {
                    if(rowPcm==null)awaitingFollowing=true;
                    else
                    {
                        ValidatePcm(rowPcm,row,"service_frame",0);
                        RecordPcm(rowPcm);
                    }
                }
                else if(awaitingFollowing)
                {
                    if(row!=resumeRow+1||rowPcm==null)
                        throw Invalid(
                            "override resume following row has no PCM packet");
                    ValidatePcm(rowPcm,row,"following_row",1);
                    RecordPcm(rowPcm);awaitingFollowing=false;
                }
                else if(rowPcm!=null)
                    throw Invalid("PCM packet has no override resume selection");
            }

            private void RecordPcm(JObject value)
            {
                if(pcm!=null)throw Invalid("PCM selection is duplicated");
                pcm=(JObject)value.DeepClone();
            }

            internal void Complete()
            {
                if(awaitingFollowing)
                    throw Invalid("override resume ended before following PCM");
                if((resume==null)!=(pcm==null))
                    throw Invalid("override resume and PCM inventory differ");
            }

            private static void ValidateResume(JObject value,
                CompleteRunAudioObserver.DriverService selected,
                CompleteRunAudioObserver.FrameCapture capture,int row)
            {
                ValidateOverrideResume(value);
                Require(Integer(value,"frame")==row
                    &&Boolean(value,"restores_saved_priority")
                    &&!Boolean(value,"restores_psg_noise")
                    &&UShort(value,"service_token")==selected.Token
                    &&Unsigned(value,"service_begin_ordinal")
                        ==selected.BeginNativeOrdinal,
                    "override resume identity differs");
                GpgxAudioTraceEvent? completion=null;
                foreach(GpgxAudioTraceEvent evt in capture.RawEvents)
                {
                    if(evt.Kind!=2||evt.Subject!=23||evt.Pc!=0x0DB4
                        ||evt.ServiceToken!=selected.Token
                        ||evt.ServiceKindId!=9||evt.SourceCpu!=1)continue;
                    if(completion.HasValue)
                        throw Invalid("override resume completion is ambiguous");
                    completion=evt;
                }
                Require(completion.HasValue
                    &&Unsigned(value,"native_ordinal")
                        ==completion.Value.Ordinal,
                    "override resume completion differs");
                JArray writes=Array(value,"writes");
                Require(writes.Count==selected.OwnedChipEvents.Count
                    &&writes.Count>0,"override resume writes differ");
                for(int index=0;index<writes.Count;index++)
                {
                    JObject raw=Object(writes[index],"override write");
                    ValidateOverrideWrite(raw);
                    CompleteRunAudioObserver.OwnedChipEvent expected=
                        selected.OwnedChipEvents[index];
                    Require(Unsigned(raw,"native_ordinal")==expected.NativeOrdinal
                        &&Byte(raw,"event_kind")==expected.EventKind
                        &&Byte(raw,"subject")==expected.Subject
                        &&Byte(raw,"value")==expected.Value
                        &&Unsigned(raw,"pc")==expected.Pc
                        &&Byte(raw,"source_cpu")==expected.SourceCpu
                        &&Boolean(raw,"data")==expected.IsData
                        &&Byte(raw,"port")==expected.Port
                        &&Byte(raw,"register")==expected.Register,
                        "override resume write differs");
                }
            }

            private static void ValidatePcm(JObject value,int row,
                string selection,int offset)
            {
                S2RequestAwareOracleV2Extractor.ValidatePcm(value,row);
                Require(String(value,"selection")==selection
                    &&Integer(value,"offset")==offset,
                    "PCM selection differs");
                long frames=Integer64(value,"stereo_frames");
                long bytes=Integer64(value,"byte_count");
                Require(frames>0&&frames<=int.MaxValue
                    &&bytes==checked(frames*4)&&bytes<=int.MaxValue,
                    "PCM frame/byte count differs");
                byte[] decoded=DecodeHex(String(value,"pcm_hex"),"PCM");
                Require(decoded.LongLength==bytes
                    &&Hex(Sha256(decoded))==String(value,"sha256"),
                    "PCM digest differs");
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
            byte[] bytes=File.ReadAllBytes(path);
            string json=StrictUtf8(bytes,label);
            if(json.Length!=0&&json[0]=='\uFEFF')
                throw Invalid(label+" contains a byte-order mark");
            RejectNonstandardJson(json,label);
            using(var reader=new JsonTextReader(new StringReader(json)))
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
        { RejectNonstandardJson(line,label);try{using(var reader=new JsonTextReader(new StringReader(line)))
          { JObject result=Object(JObject.Load(reader,new JsonLoadSettings {
              DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error }),label);
            if(reader.Read())throw Invalid(label+" has trailing JSON value");
            if(!string.Equals(line,result.ToString(Formatting.None),
                StringComparison.Ordinal))
                throw Invalid(label+" is not exact producer JSON");
            return result; }}catch(InvalidDataException){throw;}catch(Exception error)
          {throw Invalid(label+" is not strict JSON",error);} }
        private static void RejectNonstandardJson(string value,string label)
        {
            bool inString=false,escaped=false;
            for(int index=0;index<value.Length;index++)
            {
                char current=value[index];
                if(inString)
                {
                    if(escaped)escaped=false;
                    else if(current=='\\')escaped=true;
                    else if(current=='\"')inString=false;
                    continue;
                }
                if(current=='\"'){inString=true;continue;}
                if(current=='\'')
                    throw Invalid(label+" contains a single-quoted string");
                if(current=='/'&&index+1<value.Length
                    &&(value[index+1]=='/'||value[index+1]=='*'))
                    throw Invalid(label+" contains a JSON comment");
                if(current==',')
                {
                    int next=index+1;
                    while(next<value.Length
                        &&(value[next]==' '||value[next]=='\t'))next++;
                    if(next<value.Length
                        &&(value[next]=='}'||value[next]==']'))
                        throw Invalid(label+" contains a trailing comma");
                }
            }
        }
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
        private static ushort UShort(JObject value,string name)
        { uint result=Unsigned(value,name);if(result>ushort.MaxValue)throw Invalid(name+" is outside ushort range");return (ushort)result; }
        private static byte Byte(JObject value,string name)
        { int result=Integer(value,name);if(result<0||result>255)throw Invalid(name+" is outside byte range");return (byte)result; }
        private static void State(JObject value,string name)
        { string state=String(value,name);if(state.Length!=0x4000)throw Invalid("state snapshot differs");for(int i=0;i<state.Length;i++)if(!((state[i]>='0'&&state[i]<='9')||(state[i]>='a'&&state[i]<='f')))throw Invalid("state snapshot is not lowercase hex"); }
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
        private static byte[] DecodeHex(string value,string label)
        { HexData(value);var result=new byte[value.Length/2];for(int index=0;index<result.Length;index++)result[index]=(byte)((Nibble(value[index*2])<<4)|Nibble(value[index*2+1]));return result; }
        private static string StrictUtf8(byte[] value,string label)
        { try{return new UTF8Encoding(false,true).GetString(value);}catch(DecoderFallbackException error){throw Invalid(label+" is not strict UTF-8",error);} }
        private static byte[] Write(TextWriter output,JObject value)
        { byte[] bytes=Canonical(value);output.Write(Encoding.UTF8.GetString(bytes));return bytes; }
        private static void Append(HashAlgorithm digest,byte[] bytes)
        { digest.TransformBlock(bytes,0,bytes.Length,bytes,0); }
        private static void Require(bool condition,string message)
        { if(!condition)throw Invalid(message); }
        private static InvalidDataException Invalid(string message)
        { return new InvalidDataException("S2 request-aware extractor: "+message); }
        private static InvalidDataException Invalid(string message,Exception error)
        { return new InvalidDataException("S2 request-aware extractor: "+message,error); }
    }
}
