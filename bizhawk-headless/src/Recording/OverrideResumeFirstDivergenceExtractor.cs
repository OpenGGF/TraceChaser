using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>
    /// Sole raw-to-reference normalizer for the S1/S2 override-resume
    /// boundary. Inputs remain external; the returned bytes are the only
    /// values the no-replace publisher may install as fixtures.
    /// </summary>
    internal sealed class OverrideResumeFirstDivergenceExtractor
    {
        private readonly bool requireCompleteCanonicalRows;
        internal OverrideResumeFirstDivergenceExtractor()
            :this(true){}
        private OverrideResumeFirstDivergenceExtractor(bool requireRows)
        {requireCompleteCanonicalRows=requireRows;}
        internal static OverrideResumeFirstDivergenceExtractor ForTesting()
        {return new OverrideResumeFirstDivergenceExtractor(false);}
        internal sealed class Inputs
        {
            internal Inputs(string s1Raw1, string s1Attestation1,
                string s1Raw2, string s1Attestation2, string s2Raw1,
                string s2Attestation1, string s2Raw2, string s2Attestation2)
            {
                S1Raw1=s1Raw1;S1Attestation1=s1Attestation1;
                S1Raw2=s1Raw2;S1Attestation2=s1Attestation2;
                S2Raw1=s2Raw1;S2Attestation1=s2Attestation1;
                S2Raw2=s2Raw2;S2Attestation2=s2Attestation2;
            }
            internal string S1Raw1,S1Attestation1,S1Raw2,S1Attestation2;
            internal string S2Raw1,S2Attestation1,S2Raw2,S2Attestation2;
        }

        internal sealed class GameOutput
        {
            internal GameOutput(byte[] referenceGzip, byte[] metadataUtf8)
            { ReferenceGzip=referenceGzip;MetadataUtf8=metadataUtf8; }
            internal byte[] ReferenceGzip { get; private set; }
            internal byte[] MetadataUtf8 { get; private set; }
        }

        internal sealed class Output
        {
            internal Output(GameOutput s1, GameOutput s2) { S1=s1;S2=s2; }
            internal GameOutput S1 { get; private set; }
            internal GameOutput S2 { get; private set; }
        }

        private sealed class RawEvidence
        {
            internal long ByteCount;
            internal string Sha256;
            internal JObject Boundary;
            internal JObject Pcm;
        }

        private sealed class FileEvidence
        {
            internal long ByteCount;
            internal string Sha256;
        }

        private sealed class AttestationEvidence
        {
            internal byte[] Bytes;
            internal string Sha256;
            internal byte[] Normalized;
        }

        internal Output Extract(Inputs inputs)
        {
            if (inputs == null) throw new ArgumentNullException("inputs");
            return new Output(
                ExtractGame("s1",inputs.S1Raw1,inputs.S1Attestation1,
                    inputs.S1Raw2,inputs.S1Attestation2),
                ExtractGame("s2",inputs.S2Raw1,inputs.S2Attestation1,
                    inputs.S2Raw2,inputs.S2Attestation2));
        }

        private GameOutput ExtractGame(string game,string raw1,
            string attestation1,string raw2,string attestation2)
        {
            FileEvidence firstFile=InspectStrictRaw(raw1);
            FileEvidence secondFile=InspectStrictRaw(raw2);
            if(firstFile.ByteCount!=secondFile.ByteCount
                ||firstFile.Sha256!=secondFile.Sha256
                ||!FilesEqual(raw1,raw2))
                throw Invalid("duplicate "+game.ToUpperInvariant()+" raw bytes differ");
            RawEvidence first=ReadRaw(game,raw1,firstFile);
            RawEvidence second=new RawEvidence
            {ByteCount=secondFile.ByteCount,Sha256=secondFile.Sha256};
            AttestationEvidence firstAttestation=ReadAttestation(
                game,attestation1,first);
            AttestationEvidence secondAttestation=ReadAttestation(
                game,attestation2,second);
            if(!BytesEqual(firstAttestation.Normalized,
                secondAttestation.Normalized))
                throw Invalid("duplicate "+game.ToUpperInvariant()
                    +" attestations differ after timestamp normalization");

            JObject reference=new JObject
            {
                ["schema"]="openggf.override-resume-first-divergence-reference.v1",
                ["game"]=game,["boundary"]=first.Boundary,
                ["pcm"]=first.Pcm
            };
            ValidatePublishedReference(game,reference);
            byte[] logical=Utf8(Canonical(reference)+"\n");
            byte[] stored=DeterministicGzip(logical);
            JObject metadata=new JObject
            {
                ["schema"]="openggf.override-resume-first-divergence-metadata.v1",
                ["game"]=game,
                ["raw_sha256"]=new JArray(first.Sha256,second.Sha256),
                ["raw_byte_count"]=first.ByteCount,
                ["attestation_sha256"]=new JArray(
                    firstAttestation.Sha256,secondAttestation.Sha256),
                ["record_count"]=1,
                ["logical_byte_count"]=logical.LongLength,
                ["logical_sha256"]=Digest(logical),
                ["stored_byte_count"]=stored.LongLength,
                ["stored_sha256"]=Digest(stored)
            };
            ValidatePublishedMetadata(game,metadata);
            return new GameOutput(stored,Utf8(Canonical(metadata)+"\n"));
        }

        private RawEvidence ReadRaw(string game,string path,
            FileEvidence file)
        {
            string expected=game=="s1"
                ?"openggf.s1-complete-run-audio-raw.v1"
                :"openggf.s2-complete-run-audio-raw.v2";
            JObject metadata=null,boundary=null,pcm=null,terminal=null;
            int expectedRow=game=="s1"?860:769;
            int frameCount=0,s1OpenRow=-1;
            try
            {
                using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,
                    FileShare.Read,64*1024,FileOptions.SequentialScan))
                using(var reader=new StreamReader(stream,new UTF8Encoding(false,true),
                    false,64*1024))
                {
                    string line;
                    while((line=reader.ReadLine())!=null)
                    {
                        if(line.Length==0)throw Invalid("raw contains an empty record");
                        JObject row=ParseObject(line,"raw record");
                        string type=RequiredString(row,"type");
                        if(terminal!=null)throw Invalid("raw contains records after terminal");
                        if(type=="metadata")
                        {
                            if(metadata!=null)throw Invalid("raw metadata is ambiguous");
                            metadata=row;
                        }
                        else if(game=="s1"&&type=="override_resume")
                        {
                            if(boundary!=null)throw Invalid("S1 override-resume boundary is ambiguous");
                            boundary=row;
                        }
                        else if(game=="s1"&&type=="native_pcm_packet")
                        {
                            if(pcm!=null)throw Invalid("S1 override-resume PCM is ambiguous");
                            pcm=row;
                        }
                        else if(game=="s1"&&type=="frame_begin")
                        {
                            int current=RequiredInt(row,"row");
                            if(requireCompleteCanonicalRows&&current!=expectedRow)
                                throw Invalid("S1 raw rows are not contiguous");
                            if(s1OpenRow!=-1)throw Invalid("S1 frame begin is nested");
                            s1OpenRow=current;
                        }
                        else if(game=="s1"&&type=="frame_end")
                        {
                            int current=RequiredInt(row,"row");
                            if(s1OpenRow!=current)throw Invalid(
                                "S1 frame end does not close its row");
                            s1OpenRow=-1;expectedRow=current+1;frameCount++;
                        }
                        else if(game=="s2"&&type=="frame")
                        {
                            int current=RequiredInt(row,"row");
                            if(requireCompleteCanonicalRows&&current!=expectedRow)
                                throw Invalid("S2 raw rows are not contiguous");
                            expectedRow=current+1;frameCount++;
                            JToken candidate=row["override_resume"];
                            if(candidate!=null&&candidate.Type!=JTokenType.Null)
                            {
                                if(boundary!=null)throw Invalid("S2 override-resume boundary is ambiguous");
                                boundary=RequireObject(candidate,"S2 override-resume boundary");
                            }
                            candidate=row["pcm"];
                            if(candidate!=null&&candidate.Type!=JTokenType.Null)
                            {
                                if(pcm!=null)throw Invalid("S2 override-resume PCM is ambiguous");
                                pcm=RequireObject(candidate,"S2 override-resume PCM");
                            }
                        }
                        else if(type=="terminal")
                        {
                            if(terminal!=null)throw Invalid("raw terminal is ambiguous");
                            terminal=row;
                        }
                    }
                }
            }
            catch(DecoderFallbackException)
            {throw Invalid("raw is not strict UTF-8");}
            if(metadata==null||RequiredString(metadata,"schema")!=expected)
                throw Invalid("wrong "+game.ToUpperInvariant()+" raw schema");
            ValidateRawMetadata(game,metadata);
            ValidateIdentity(game,metadata);
            if(boundary==null)throw Invalid("no "+game.ToUpperInvariant()+" resume service");
            if(pcm==null)throw Invalid("no "+game.ToUpperInvariant()+" PCM packet");
            if(terminal==null)throw Invalid(game.ToUpperInvariant()+" raw is truncated");
            if(s1OpenRow!=-1)throw Invalid("S1 raw ended inside a frame");
            if(requireCompleteCanonicalRows)
            {
                int expectedEnd=game=="s1"?225101:259590;
                int expectedCount=game=="s1"?224241:258821;
                if(expectedRow!=expectedEnd||frameCount!=expectedCount)
                    throw Invalid(game.ToUpperInvariant()+" raw is truncated");
            }
            ValidateBoundary(game,boundary);
            ValidatePcm(game,boundary,pcm);
            ValidateTerminal(game,terminal);
            return new RawEvidence
            {ByteCount=file.ByteCount,Sha256=file.Sha256,
                Boundary=boundary,Pcm=pcm};
        }

        private static void ValidateIdentity(string game,JObject metadata)
        {
            if(game=="s1")
            {
                RequireEqual("69e102855d4389c3fd1a8f3dc7d193f8eee5fe5b",
                    RequiredString(metadata,"rom_sha1"),"S1 ROM");
                RequireEqual(S1CompleteRunAudioReferenceCapture.MovieSha256,
                    RequiredString(metadata,"bk2_sha256"),"S1 BK2");
                RequireEqual(860,RequiredInt(metadata,"first_row"),"S1 first row");
                RequireEqual(225101,RequiredInt(metadata,"exclusive_end"),"S1 end row");
            }
            else
            {
                RequireEqual("8bca5dcef1af3e00098666fd892dc1c2a76333f9",
                    RequiredString(metadata,"rom_sha1"),"S2 ROM");
                RequireEqual("e850798f882b8c580aad148bc97cb50f260cae1d336dd649fe2f4dfae6796aa5",
                    RequiredString(metadata,"bk2_sha256"),"S2 BK2");
                RequireEqual(769,RequiredInt(metadata,"first_row"),"S2 first row");
                RequireEqual(259590,RequiredInt(metadata,"exclusive_end"),"S2 end row");
            }
        }

        private static void ValidateRawMetadata(string game,JObject metadata)
        {
            if(game=="s1")
            {
                ExactProperties(metadata,"S1 raw metadata","type","schema",
                    "rom_sha1","bk2_sha256","first_row","exclusive_end",
                    "native_abi","native_event_size","native_capacity");
                RequireEqual("metadata",RequiredString(metadata,"type"),
                    "S1 raw metadata type");
                RequireEqual(4,RequiredInt(metadata,"native_abi"),
                    "S1 native ABI");
                RequireEqual(32,RequiredInt(metadata,"native_event_size"),
                    "S1 native event size");
                RequireEqual(65536,RequiredInt(metadata,"native_capacity"),
                    "S1 native capacity");
                return;
            }
            ExactProperties(metadata,"S2 raw metadata","type","schema",
                "rom_sha1","bk2_sha256","service_manifest_sha256",
                "first_row","exclusive_end","state_start",
                "state_exclusive_end");
            RequireEqual("metadata",RequiredString(metadata,"type"),
                "S2 raw metadata type");
            RequireEqual(S2AudioObserverProfile.ServiceManifestSha256,
                RequiredString(metadata,"service_manifest_sha256"),
                "S2 service manifest");
            RequireEqual(S2AudioObserverProfile.DriverStateStart,
                RequiredInt(metadata,"state_start"),"S2 state start");
            RequireEqual(S2AudioObserverProfile.DriverStateExclusiveEnd,
                RequiredInt(metadata,"state_exclusive_end"),
                "S2 state end");
        }

        private static void ValidateBoundary(string game,JObject boundary)
        {
            if(game=="s1")
                ExactProperties(boundary,"S1 selected boundary","type",
                    "request","admission","request_frame","admission_frame",
                    "frame","pc","service_token","native_ordinal",
                    "fix_bugs","writes_dac_disable_zero","writes");
            else
                ExactProperties(boundary,"S2 selected boundary","request",
                    "admission","request_pc","pc","service_token",
                    "service_begin_ordinal","native_ordinal","frame",
                    "fix_driver_bugs","restores_saved_priority",
                    "restores_psg_noise","writes");
            RequireEqual("cfFadeInToPrevious",RequiredString(boundary,"request"),
                game+" restore request");
            RequireNonNegative(RequiredInt(boundary,"frame"),game+" frame");
            JArray writes=boundary["writes"] as JArray;
            if(writes==null||writes.Count==0)throw Invalid(game+" resumed service owns no writes");
            long previous=-1;
            foreach(JToken token in writes)
            {
                JObject write=RequireObject(token,game+" write");
                ExactProperties(write,game+" selected write","native_ordinal",
                    "event_kind","subject","value","pc","source_cpu",
                    "data","port","register");
                long ordinal=RequiredLong(write,"native_ordinal");
                if(ordinal<1||ordinal<=previous)
                    throw Invalid(game+" chip writes are unordered");
                previous=ordinal;
                RequireNonNegative(RequiredInt(write,"event_kind"),
                    game+" write event kind");
                RequireNonNegative(RequiredInt(write,"subject"),
                    game+" write subject");
                RequireRange(RequiredInt(write,"value"),0,255,
                    game+" write value");
                RequireNonNegative(RequiredInt(write,"pc"),game+" write PC");
                RequireNonNegative(RequiredInt(write,"source_cpu"),
                    game+" write source CPU");
                RequiredBool(write,"data");
                RequireRange(RequiredInt(write,"port"),0,1,
                    game+" write port");
                RequireRange(RequiredInt(write,"register"),0,255,
                    game+" write register");
            }
            if(game=="s1")
            {
                RequireEqual("override_resume",RequiredString(boundary,"type"),
                    "S1 boundary type");
                RequireEqual("native_restore_entry",
                    RequiredString(boundary,"admission"),"S1 admission");
                RequireNonNegative(RequiredInt(boundary,"request_frame"),
                    "S1 request frame");
                RequireNonNegative(RequiredInt(boundary,"admission_frame"),
                    "S1 admission frame");
                RequireEqual(0x72B14,RequiredInt(boundary,"pc"),"S1 PC");
                RequirePositive(RequiredLong(boundary,"service_token"),
                    "S1 service token");
                RequirePositive(RequiredLong(boundary,"native_ordinal"),
                    "S1 native ordinal");
                RequireEqual(0,RequiredInt(boundary,"fix_bugs"),"FixBugs");
                if(RequiredBool(boundary,"writes_dac_disable_zero"))
                    throw Invalid("S1 FixBugs=0 invented YM $2B=$00");
            }
            else
            {
                RequireEqual("native_service_completion",
                    RequiredString(boundary,"admission"),"S2 admission");
                RequireEqual(0x0D35,RequiredInt(boundary,"request_pc"),
                    "S2 request PC");
                RequireEqual(0x0DB4,RequiredInt(boundary,"pc"),"S2 PC");
                RequirePositive(RequiredLong(boundary,"service_token"),
                    "S2 service token");
                RequirePositive(RequiredLong(boundary,
                    "service_begin_ordinal"),"S2 service begin ordinal");
                RequirePositive(RequiredLong(boundary,"native_ordinal"),
                    "S2 native ordinal");
                RequireEqual(0,RequiredInt(boundary,"fix_driver_bugs"),
                    "FixDriverBugs");
                if(!RequiredBool(boundary,"restores_saved_priority")
                    ||RequiredBool(boundary,"restores_psg_noise"))
                    throw Invalid("S2 FixDriverBugs=0 restore semantics changed");
            }
        }

        private static void ValidatePcm(string game,JObject boundary,JObject pcm)
        {
            if(game=="s1")
                ExactProperties(pcm,"S1 selected PCM","type","selection",
                    "row","offset","sample_rate","channels","format",
                    "stereo_frames","byte_count","pcm_hex","sha256");
            else
                ExactProperties(pcm,"S2 selected PCM","selection","row",
                    "offset","sample_rate","channels","format",
                    "stereo_frames","byte_count","pcm_hex","sha256");
            if(game=="s1")
                RequireEqual("native_pcm_packet",RequiredString(pcm,"type"),
                    "S1 PCM type");
            string selection=RequiredString(pcm,"selection");
            int offset=RequiredInt(pcm,"offset");
            if((selection!="service_frame"||offset!=0)
                &&(selection!="following_row"||offset!=1))
                throw Invalid("PCM packet timing is outside the exact eligible rows");
            int row=RequiredInt(pcm,"row");
            RequireNonNegative(row,"PCM row");
            RequireEqual(checked(RequiredInt(boundary,"frame")+offset),row,
                "PCM row");
            RequireEqual(44100,RequiredInt(pcm,"sample_rate"),"PCM sample rate");
            RequireEqual(2,RequiredInt(pcm,"channels"),"PCM channels");
            RequireEqual("s16le-interleaved-stereo",RequiredString(pcm,"format"),
                "PCM format");
            int frames=RequiredInt(pcm,"stereo_frames");
            int byteCount=RequiredInt(pcm,"byte_count");
            string hex=RequiredString(pcm,"pcm_hex");
            byte[] bytes=ParseHex(hex,"PCM bytes");
            if(frames<=0||byteCount!=frames*4||bytes.Length!=byteCount)
                throw Invalid("PCM packet byte/frame inventory is inconsistent");
            RequireEqual(Digest(bytes),RequiredString(pcm,"sha256"),"PCM digest");
        }

        private static void ValidatePublishedReference(string game,JObject value)
        {
            ExactProperties(value,"published reference","schema","game",
                "boundary","pcm");
            RequireEqual("openggf.override-resume-first-divergence-reference.v1",
                RequiredString(value,"schema"),"reference schema");
            RequireEqual(game,RequiredString(value,"game"),"reference game");
            JObject boundary=RequireObject(value["boundary"],
                "published boundary");
            JObject pcm=RequireObject(value["pcm"],"published PCM");
            ValidateBoundary(game,boundary);
            ValidatePcm(game,boundary,pcm);
        }

        private static void ValidatePublishedMetadata(string game,JObject value)
        {
            ExactProperties(value,"published metadata","schema","game",
                "raw_sha256","raw_byte_count","attestation_sha256",
                "record_count","logical_byte_count","logical_sha256",
                "stored_byte_count","stored_sha256");
            RequireEqual("openggf.override-resume-first-divergence-metadata.v1",
                RequiredString(value,"schema"),"metadata schema");
            RequireEqual(game,RequiredString(value,"game"),"metadata game");
            ValidateDigestPair(RequiredArray(value,"raw_sha256"),
                "raw digest");
            ValidateDigestPair(RequiredArray(value,"attestation_sha256"),
                "attestation digest");
            RequirePositive(RequiredLong(value,"raw_byte_count"),
                "raw byte count");
            RequireEqual(1,RequiredInt(value,"record_count"),"record count");
            RequirePositive(RequiredLong(value,"logical_byte_count"),
                "logical byte count");
            ValidateLowerHex(RequiredString(value,"logical_sha256"),64,
                "logical digest");
            RequirePositive(RequiredLong(value,"stored_byte_count"),
                "stored byte count");
            ValidateLowerHex(RequiredString(value,"stored_sha256"),64,
                "stored digest");
        }

        private static void ValidateDigestPair(JArray value,string label)
        {
            if(value.Count!=2)throw Invalid(label+" pair must contain two values");
            for(int index=0;index<value.Count;index++)
            {
                JToken item=value[index];
                if(item.Type!=JTokenType.String)
                    throw Invalid(label+" must be a string");
                ValidateLowerHex((string)item,64,label);
            }
        }

        private static void ValidateTerminal(string game,JObject terminal)
        {
            int expected=game=="s1"?225101:259590;
            RequireEqual(expected,RequiredInt(terminal,"exclusive_end"),
                game+" terminal");
            if(terminal["overflows"]!=null&&RequiredInt(terminal,"overflows")!=0)
                throw Invalid(game+" raw overflowed");
            if(terminal["faulted"]!=null&&RequiredBool(terminal,"faulted"))
                throw Invalid(game+" raw faulted");
        }

        private static AttestationEvidence ReadAttestation(string game,
            string path,RawEvidence raw)
        {
            byte[] bytes=ReadStrictFile(path,"attestation");
            string text=StrictUtf8(bytes,"attestation");
            JObject value=ParseObject(text.Substring(0,text.Length-1),
                "attestation");
            ExactProperties(value,"attestation","schema","capture_timestamp_utc",
                "game","raw_sha256","raw_byte_count","status","fault_count",
                "overflow_count","authority_id");
            RequireEqual("openggf.override-resume-first-divergence-attestation.v1",
                RequiredString(value,"schema"),"attestation schema");
            RequireEqual(game,RequiredString(value,"game"),"attestation game");
            RequireEqual(raw.Sha256,RequiredString(value,"raw_sha256"),
                "attested raw digest");
            RequireEqual(raw.ByteCount,RequiredLong(value,"raw_byte_count"),
                "attested raw byte count");
            RequireEqual("ok",RequiredString(value,"status"),"attestation status");
            RequireEqual(0,RequiredInt(value,"fault_count"),"fault count");
            RequireEqual(0,RequiredInt(value,"overflow_count"),"overflow count");
            string authorityId=RequiredString(value,"authority_id");
            string timestamp=RequiredString(value,"capture_timestamp_utc");
            DateTime parsed;
            if(!DateTime.TryParseExact(timestamp,"yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal
                    |DateTimeStyles.AdjustToUniversal,
                out parsed))throw Invalid("attestation timestamp is invalid");
            byte[] canonical=OverrideResumeFirstDivergenceAttestation
                .CanonicalBytes(game,raw.Sha256,raw.ByteCount,authorityId,
                    timestamp);
            if(!BytesEqual(bytes,canonical))
                throw Invalid("attestation bytes are not canonical");
            return new AttestationEvidence
            {Bytes=bytes,Sha256=Digest(bytes),
                Normalized=NormalizeTimestampOnly(bytes,timestamp)};
        }

        private static byte[] NormalizeTimestampOnly(byte[] bytes,
            string timestamp)
        {
            const string prefix="{\"schema\":\"openggf.override-resume-first-divergence-attestation.v1\","
                +"\"capture_timestamp_utc\":\"";
            const string replacement="1970-01-01T00:00:00Z";
            byte[] prefixBytes=Utf8(prefix);
            byte[] timestampBytes=Utf8(timestamp);
            byte[] replacementBytes=Utf8(replacement);
            if(timestampBytes.Length!=replacementBytes.Length
                ||bytes.Length<prefixBytes.Length+timestampBytes.Length+1)
                throw Invalid("attestation timestamp bytes are not canonical");
            for(int index=0;index<prefixBytes.Length;index++)
                if(bytes[index]!=prefixBytes[index])
                    throw Invalid("attestation timestamp field is not canonical");
            for(int index=0;index<timestampBytes.Length;index++)
                if(bytes[prefixBytes.Length+index]!=timestampBytes[index])
                    throw Invalid("attestation timestamp value is not canonical");
            if(bytes[prefixBytes.Length+timestampBytes.Length]!=(byte)'\"')
                throw Invalid("attestation timestamp terminator is not canonical");
            byte[] normalized=(byte[])bytes.Clone();
            Buffer.BlockCopy(replacementBytes,0,normalized,prefixBytes.Length,
                replacementBytes.Length);
            return normalized;
        }

        internal static byte[] DeterministicGzip(byte[] bytes)
        {
            using(var output=new MemoryStream())
            {
                using(var gzip=new GZipStream(output,CompressionMode.Compress,true))
                    gzip.Write(bytes,0,bytes.Length);
                byte[] result=output.ToArray();
                if(result.Length<18||result[0]!=0x1f||result[1]!=0x8b)
                    throw Invalid("runtime did not produce a valid gzip member");
                result[4]=result[5]=result[6]=result[7]=0;
                result[9]=255;
                return result;
            }
        }

        private static byte[] ReadStrictFile(string path,string label)
        {
            if(string.IsNullOrEmpty(path)||!Path.IsPathRooted(path)
                ||!File.Exists(path)||Directory.Exists(path)
                ||LinuxPathEntry.IsSymbolicLink(path))
                throw Invalid(label+" must be an existing absolute regular non-symlink file");
            var info=new FileInfo(path);
            if(info.Length>1024*1024)
                throw Invalid(label+" exceeds the bounded one-megabyte limit");
            byte[] bytes=File.ReadAllBytes(path);
            if(bytes.Length==0||bytes[bytes.Length-1]!=10)
                throw Invalid(label+" must be nonempty and LF-terminated");
            if(bytes.Length>=3&&bytes[0]==0xef&&bytes[1]==0xbb&&bytes[2]==0xbf)
                throw Invalid(label+" must not contain a UTF-8 BOM");
            for(int index=0;index<bytes.Length;index++)
                if(bytes[index]==13)throw Invalid(label+" must use LF, not CRLF");
            return bytes;
        }

        private static FileEvidence InspectStrictRaw(string path)
        {
            const string label="raw";
            if(string.IsNullOrEmpty(path)||!Path.IsPathRooted(path)
                ||!File.Exists(path)||Directory.Exists(path)
                ||LinuxPathEntry.IsSymbolicLink(path))
                throw Invalid(label+" must be an existing absolute regular non-symlink file");
            var info=new FileInfo(path);
            if(info.Length==0)throw Invalid("raw must be nonempty and LF-terminated");
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,
                FileShare.Read,64*1024,FileOptions.SequentialScan))
            {
                int first=stream.ReadByte(),second=stream.ReadByte(),third=stream.ReadByte();
                if(first==0xef&&second==0xbb&&third==0xbf)
                    throw Invalid("raw must not contain a UTF-8 BOM");
                stream.Position=0;
                var buffer=new byte[64*1024];int count;byte last=0;
                while((count=stream.Read(buffer,0,buffer.Length))!=0)
                {
                    for(int index=0;index<count;index++)
                        if(buffer[index]==13)throw Invalid("raw must use LF, not CRLF");
                    last=buffer[count-1];
                }
                if(last!=10)throw Invalid("raw must be nonempty and LF-terminated");
                stream.Position=0;
                using(SHA256 sha=SHA256.Create())
                    return new FileEvidence
                    {ByteCount=info.Length,Sha256=Hex(sha.ComputeHash(stream))};
            }
        }

        private static bool FilesEqual(string first,string second)
        {
            using(var a=new FileStream(first,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var b=new FileStream(second,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                var left=new byte[64*1024];var right=new byte[64*1024];
                while(true)
                {
                    int leftCount=a.Read(left,0,left.Length);
                    int rightCount=b.Read(right,0,right.Length);
                    if(leftCount!=rightCount)return false;
                    if(leftCount==0)return true;
                    for(int index=0;index<leftCount;index++)
                        if(left[index]!=right[index])return false;
                }
            }
        }

        private static bool BytesEqual(byte[] first,byte[] second)
        {
            if(first==null||second==null||first.Length!=second.Length)return false;
            for(int index=0;index<first.Length;index++)
                if(first[index]!=second[index])return false;
            return true;
        }

        private static string StrictUtf8(byte[] bytes,string label)
        {
            try{return new UTF8Encoding(false,true).GetString(bytes);}
            catch(DecoderFallbackException){throw Invalid(label+" is not strict UTF-8");}
        }

        private static JObject ParseObject(string text,string label)
        {
            try
            {
                using(var reader=new JsonTextReader(new StringReader(text)))
                {
                    reader.DateParseHandling=DateParseHandling.None;
                    JToken token=JToken.ReadFrom(reader);
                    if(reader.Read())throw Invalid(label+" has trailing JSON");
                    return RequireObject(token,label);
                }
            }
            catch(JsonException exception)
            {throw new InvalidDataException(label+" is invalid JSON",exception);}
        }

        private static JObject RequireObject(JToken token,string label)
        {var value=token as JObject;if(value==null)throw Invalid(label+" must be an object");return value;}
        private static string RequiredString(JObject value,string name)
        {JToken token=value[name];if(token==null||token.Type!=JTokenType.String||((string)token).Length==0)
            throw Invalid(name+" must be a nonempty string");return(string)token;}
        private static int RequiredInt(JObject value,string name)
        {JToken token=value[name];if(token==null||token.Type!=JTokenType.Integer)
            throw Invalid(name+" must be an integer");return checked((int)token);}
        private static long RequiredLong(JObject value,string name)
        {JToken token=value[name];if(token==null||token.Type!=JTokenType.Integer)
            throw Invalid(name+" must be an integer");return(long)token;}
        private static bool RequiredBool(JObject value,string name)
        {JToken token=value[name];if(token==null||token.Type!=JTokenType.Boolean)
            throw Invalid(name+" must be a boolean");return(bool)token;}
        private static JArray RequiredArray(JObject value,string name)
        {JToken token=value[name];var array=token as JArray;if(array==null)
            throw Invalid(name+" must be an array");return array;}
        private static void ExactProperties(JObject value,string label,params string[] names)
        {var expected=new HashSet<string>(names,StringComparer.Ordinal);foreach(JProperty p in value.Properties())
            if(!expected.Remove(p.Name))throw Invalid(label+" has unknown property "+p.Name);
            if(expected.Count!=0)throw Invalid(label+" is missing property "+expected.First());}
        private static void RequireNonNegative(long value,string label)
        {if(value<0)throw Invalid(label+" must be nonnegative");}
        private static void RequirePositive(long value,string label)
        {if(value<1)throw Invalid(label+" must be positive");}
        private static void RequireRange(int value,int minimum,int maximum,
            string label)
        {if(value<minimum||value>maximum)throw Invalid(label+" is out of range");}
        private static void RequireEqual<T>(T expected,T actual,string label)
        {if(!EqualityComparer<T>.Default.Equals(expected,actual))throw Invalid(label+" identity changed");}
        private static void ValidateLowerHex(string value,int length,string label)
        {if(value.Length!=length)throw Invalid(label+" has the wrong length");
            for(int index=0;index<value.Length;index++)if(Nibble(value[index])<0)
                throw Invalid(label+" is not lowercase hex");}
        private static byte[] ParseHex(string text,string label)
        {if((text.Length&1)!=0)throw Invalid(label+" is not even lowercase hex");var result=new byte[text.Length/2];
            for(int i=0;i<result.Length;i++){int hi=Nibble(text[i*2]),lo=Nibble(text[i*2+1]);
                if(hi<0||lo<0)throw Invalid(label+" is not lowercase hex");result[i]=(byte)((hi<<4)|lo);}return result;}
        private static int Nibble(char value)
        {if(value>='0'&&value<='9')return value-'0';if(value>='a'&&value<='f')return value-'a'+10;return-1;}
        private static string Canonical(JToken token)
        {return CanonicalToken(token).ToString(Formatting.None);}
        private static JToken CanonicalToken(JToken token)
        {JObject obj=token as JObject;if(obj!=null){var result=new JObject();foreach(JProperty p in obj.Properties()
            .OrderBy(p=>p.Name,StringComparer.Ordinal))result.Add(p.Name,CanonicalToken(p.Value));return result;}
            JArray array=token as JArray;if(array!=null){var result=new JArray();foreach(JToken item in array)
                result.Add(CanonicalToken(item));return result;}return token.DeepClone();}
        private static byte[] Utf8(string value){return new UTF8Encoding(false).GetBytes(value);}
        private static string Digest(byte[] bytes)
        {using(SHA256 sha=SHA256.Create())return Hex(sha.ComputeHash(bytes));}
        private static string Hex(byte[] bytes)
        {var value=new char[bytes.Length*2];const string alphabet="0123456789abcdef";for(int i=0;i<bytes.Length;i++)
            {value[i*2]=alphabet[bytes[i]>>4];value[i*2+1]=alphabet[bytes[i]&15];}return new string(value);}
        private static InvalidDataException Invalid(string message)
        {return new InvalidDataException(message+".");}
    }
}
