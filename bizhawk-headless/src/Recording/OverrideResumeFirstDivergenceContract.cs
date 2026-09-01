using System;
using System.IO;
using System.Security.Cryptography;

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
}
