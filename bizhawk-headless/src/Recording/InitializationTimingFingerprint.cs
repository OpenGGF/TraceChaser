using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>Canonical ROM-operation identity shared with OpenGGF NativeLevelLoadInputs.</summary>
    public static class InitializationTimingFingerprint
    {
        public struct RegionSelection
        {
            public readonly int Address;
            public readonly int Length;
            public RegionSelection(int address, int length)
            {
                if (address < 0 || length <= 0) throw new ArgumentOutOfRangeException("address/length");
                Address = address; Length = length;
            }
        }

        public static string Compute(byte[] rom, string phase,
            IList<RegionSelection> regions, IList<int> recipeChoices)
        {
            if (rom == null || regions == null || recipeChoices == null) throw new ArgumentNullException();
            if (phase != "display_clear" && phase != "level_data_decode"
                && phase != "level_setup" && phase != "initial_placement" && phase != "initial_objects" && phase != "initial_presentation")
                throw new ArgumentException("unsupported initialization phase", "phase");
            using (var payload = new MemoryStream())
            using (SHA256 sha = SHA256.Create())
            {
                WriteString(payload, "INITIALIZATION_PHASE_V1");
                WriteString(payload, phase);
                byte[] digest = sha.ComputeHash(rom);
                payload.Write(digest, 0, digest.Length);
                WriteInt(payload, regions.Count);
                foreach (RegionSelection region in regions)
                {
                    if (region.Address < 0 || region.Length <= 0
                        || (long)region.Address + region.Length > rom.Length)
                        throw new ArgumentOutOfRangeException("regions");
                    WriteInt(payload, region.Address); WriteInt(payload, region.Length);
                    payload.Write(rom, region.Address, region.Length);
                }
                WriteInt(payload, recipeChoices.Count);
                foreach (int choice in recipeChoices) WriteInt(payload, choice);
                payload.Position = 0;
                digest = sha.ComputeHash(payload);
                var result = new StringBuilder("sha256:");
                foreach (byte value in digest) result.Append(value.ToString("x2"));
                return result.ToString();
            }
        }

        private static void WriteString(Stream output, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            WriteInt(output, bytes.Length); output.Write(bytes, 0, bytes.Length);
        }

        private static void WriteInt(Stream output, int value)
        {
            output.WriteByte((byte)(value >> 24)); output.WriteByte((byte)(value >> 16));
            output.WriteByte((byte)(value >> 8)); output.WriteByte((byte)value);
        }
    }
}
