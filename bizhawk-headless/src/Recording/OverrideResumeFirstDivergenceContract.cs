using System;
using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>
    /// Narrow observation-only audio surface used by the override-resume
    /// producer. It deliberately exposes no gameplay or chip mutation.
    /// </summary>
    internal interface IOverrideResumeDiagnosticAudioHost
    {
        int DiagnosticAudioSampleRate { get; }
        void AdvanceDiagnosticAudio();
        short[] DrainDiagnosticAudio(out int stereoFrames);
    }

    /// <summary>
    /// Shared S1/S2 packet rule for the first native frame containing the
    /// resumed service, with exactly one following-row fallback owned by the
    /// caller. Every frame uses an empty pre-drain, one sound-producing native
    /// advance, and one immediate post-drain.
    /// </summary>
    internal static class OverrideResumeDiagnosticAudio
    {
        internal sealed class Packet
        {
            private readonly byte[] bytes;
            private string pcmHex;
            private string sha256;

            internal Packet(int sampleRate, int stereoFrames, byte[] value)
            {
                SampleRate = sampleRate;
                StereoFrames = stereoFrames;
                bytes = value;
            }

            internal int SampleRate { get; private set; }
            internal int StereoFrames { get; private set; }
            internal int ByteCount { get { return bytes.Length; } }
            internal string PcmHex
            {
                get { return pcmHex ?? (pcmHex = Hex(bytes)); }
            }
            internal string Sha256
            {
                get
                {
                    if (sha256 == null)
                        using (SHA256 digest = SHA256.Create())
                            sha256 = Hex(digest.ComputeHash(bytes));
                    return sha256;
                }
            }
            internal byte[] Bytes { get { return (byte[])bytes.Clone(); } }
            internal bool IsEmpty { get { return StereoFrames == 0; } }
        }

        internal static Packet AdvanceAndDrain(
            IOverrideResumeDiagnosticAudioHost host)
        {
            if (host == null) throw new ArgumentNullException("host");
            int carriedFrames;
            short[] carried = host.DrainDiagnosticAudio(out carriedFrames);
            ValidatePacket(carried, carriedFrames, "carry-over");
            if (carriedFrames != 0)
                throw new InvalidDataException(
                    "Override-resume diagnostic audio carry-over was nonempty.");

            host.AdvanceDiagnosticAudio();

            int stereoFrames;
            short[] samples = host.DrainDiagnosticAudio(out stereoFrames);
            ValidatePacket(samples, stereoFrames, "post-advance");
            if (host.DiagnosticAudioSampleRate <= 0)
                throw new InvalidDataException(
                    "Override-resume diagnostic audio sample rate is invalid.");
            return new Packet(host.DiagnosticAudioSampleRate, stereoFrames,
                SerializeLittleEndian(samples, stereoFrames));
        }

        private static void ValidatePacket(
            short[] samples, int stereoFrames, string label)
        {
            if (stereoFrames < 0 || samples == null
                || samples.Length != checked(stereoFrames * 2))
                throw new InvalidDataException(
                    "Override-resume " + label
                    + " diagnostic packet is not exact interleaved stereo.");
        }

        private static byte[] SerializeLittleEndian(
            short[] samples, int stereoFrames)
        {
            var bytes = new byte[checked(stereoFrames * 4)];
            for (int index = 0; index < samples.Length; index++)
            {
                ushort value = unchecked((ushort)samples[index]);
                bytes[index * 2] = (byte)value;
                bytes[index * 2 + 1] = (byte)(value >> 8);
            }
            return bytes;
        }

        private static string Hex(byte[] value)
        {
            var result = new char[value.Length * 2];
            const string alphabet = "0123456789abcdef";
            for (int index = 0; index < value.Length; index++)
            {
                result[index * 2] = alphabet[value[index] >> 4];
                result[index * 2 + 1] = alphabet[value[index] & 15];
            }
            return new string(result);
        }
    }

    /// <summary>Hashes the exact UTF-8 characters forwarded to a raw sink.</summary>
    internal sealed class OverrideResumeRawDigestTextWriter : TextWriter
    {
        internal sealed class Evidence
        {
            internal Evidence(long byteCount,string sha256)
            {ByteCount=byteCount;Sha256=sha256;}
            internal long ByteCount {get;private set;}
            internal string Sha256 {get;private set;}
        }
        private readonly TextWriter inner;
        private readonly SHA256 digest=SHA256.Create();
        private readonly Encoding encoding=new UTF8Encoding(false,true);
        private long byteCount;
        private bool finished;

        internal OverrideResumeRawDigestTextWriter(TextWriter value)
        {inner=value??throw new ArgumentNullException("value");NewLine="\n";}
        public override Encoding Encoding {get{return encoding;}}
        public override void Write(char value){Write(new[]{value},0,1);}
        public override void Write(string value)
        {if(value==null)return;Write(value.ToCharArray(),0,value.Length);}
        public override void Write(char[] buffer,int index,int count)
        {
            if(finished)throw new InvalidOperationException(
                "The raw digest writer is already finalized.");
            if(buffer==null)throw new ArgumentNullException("buffer");
            byte[] bytes=encoding.GetBytes(buffer,index,count);
            if(bytes.Length!=0)digest.TransformBlock(bytes,0,bytes.Length,bytes,0);
            byteCount=checked(byteCount+bytes.Length);
            inner.Write(buffer,index,count);
        }
        public override void Flush(){inner.Flush();}
        internal Evidence Finish()
        {
            if(finished)throw new InvalidOperationException(
                "The raw digest writer is already finalized.");
            inner.Flush();digest.TransformFinalBlock(new byte[0],0,0);
            finished=true;
            return new Evidence(byteCount,Hex(digest.Hash));
        }
        private static string Hex(byte[] value)
        {var result=new char[value.Length*2];const string alphabet="0123456789abcdef";
            for(int i=0;i<value.Length;i++){result[i*2]=alphabet[value[i]>>4];
                result[i*2+1]=alphabet[value[i]&15];}return new string(result);}
    }

    internal static class OverrideResumeFirstDivergenceAttestation
    {
        /// <summary>
        /// Produces the sole accepted attestation byte form: one compact
        /// object in the field order below, UTF-8 without BOM, followed by
        /// exactly one LF. Duplicate-capture comparison may replace only the
        /// 20 timestamp value bytes; every other byte remains authoritative.
        /// </summary>
        internal static string Serialize(string game,
            OverrideResumeRawDigestTextWriter.Evidence evidence,
            string authorityId,DateTime timestampUtc)
        {
            return Create(game,evidence,authorityId,timestampUtc)
                .ToString(Formatting.None)+"\n";
        }

        internal static JObject Create(string game,
            OverrideResumeRawDigestTextWriter.Evidence evidence,
            string authorityId,DateTime timestampUtc)
        {
            if(game!="s1"&&game!="s2")throw new ArgumentException(
                "Attestation game must be s1 or s2.","game");
            if(evidence==null)throw new ArgumentNullException("evidence");
            if(string.IsNullOrEmpty(authorityId))throw new ArgumentException(
                "Attestation authority identity is required.","authorityId");
            DateTime utc=timestampUtc.ToUniversalTime();
            return CreateCanonicalObject(game,evidence.Sha256,
                evidence.ByteCount,authorityId,utc.ToString(
                    "yyyy-MM-dd'T'HH:mm:ss'Z'",CultureInfo.InvariantCulture));
        }

        internal static byte[] CanonicalBytes(string game,string rawSha256,
            long rawByteCount,string authorityId,string timestamp)
        {
            string value=CreateCanonicalObject(game,rawSha256,rawByteCount,
                authorityId,timestamp).ToString(Formatting.None)+"\n";
            return new UTF8Encoding(false,true).GetBytes(value);
        }

        private static JObject CreateCanonicalObject(string game,
            string rawSha256,long rawByteCount,string authorityId,
            string timestamp)
        {
            return new JObject
            {
                ["schema"]="openggf.override-resume-first-divergence-attestation.v1",
                ["capture_timestamp_utc"]=timestamp,
                ["game"]=game,["raw_sha256"]=rawSha256,
                ["raw_byte_count"]=rawByteCount,["status"]="ok",
                ["fault_count"]=0,["overflow_count"]=0,
                ["authority_id"]=authorityId
            };
        }
    }
}
