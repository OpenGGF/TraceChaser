using System;
using System.Security.Cryptography;

namespace OpenGGF.BizHawk.Headless
{
    public static class RomIdentity
    {
        public const string Sonic1Rev01Sha1 =
            "69E102855D4389C3FD1A8F3DC7D193F8EEE5FE5B";

        public const string Sonic2Rev01Sha1 =
            "8BCA5DCEF1AF3E00098666FD892DC1C2A76333F9";

        public const string Sonic3kLockOnSha1 =
            "CFBF98C36C776677290A872547AC47C53D2761D6";

        /// <summary>
        /// Knuckles in Sonic 2: the Sonic &amp; Knuckles lock-on image over
        /// Sonic 2 World REV01 as one user-supplied 3,407,872-byte dump
        /// (S&amp;K at $000000, S2 at $200000, the 256 KiB chip at $300000;
        /// BizHawk gamedb "Sonic and Knuckles &amp; Sonic 2 (W) [!]"). The
        /// running program is the chip's patched Sonic 2, so the S2 RAM map
        /// and recorder apply; the S2 REV01 dynamic-art ROM profile does not
        /// (its opcode windows are S2 cart offsets, not chip offsets).
        /// </summary>
        public const string Kis2LockOnSha1 =
            "6CD0537A3AEE0E012BB86D5837DDFF9342595004";

        public static string ValidateSonic1Rev01(byte[] romBytes)
        {
            string actualSha1 = ComputeSha1(romBytes);
            if (!string.Equals(
                Sonic1Rev01Sha1,
                actualSha1,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Sonic 1 World REV01 ROM SHA-1 was " + actualSha1
                    + "; expected " + Sonic1Rev01Sha1 + ".");
            }
            return actualSha1;
        }

        public static string ValidateSonic2Rev01(byte[] romBytes)
        {
            string actualSha1 = ComputeSha1(romBytes);
            if (!string.Equals(
                Sonic2Rev01Sha1,
                actualSha1,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Sonic 2 World REV01 ROM SHA-1 was " + actualSha1
                    + "; expected " + Sonic2Rev01Sha1 + ".");
            }
            return actualSha1;
        }

        /// <summary>
        /// Validates the Sonic 3 &amp; Knuckles locked-on combined image
        /// (the ROM every S3K fixture movie was recorded against). Note
        /// the fixture movies' BK2 SHA1 header and the S3K metadata
        /// rom_checksum both carry the BizHawk header hash
        /// C5B1C655C19F462ADE0AC4E17A844D10, NOT this file SHA-1.
        /// </summary>
        public static string ValidateSonic3kLockOn(byte[] romBytes)
        {
            string actualSha1 = ComputeSha1(romBytes);
            if (!string.Equals(
                Sonic3kLockOnSha1,
                actualSha1,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Sonic 3 & Knuckles locked-on ROM SHA-1 was " + actualSha1
                    + "; expected " + Sonic3kLockOnSha1 + ".");
            }
            return actualSha1;
        }

        /// <summary>
        /// Detects the trace game from the ROM's SHA-1: "s1" for Sonic 1
        /// World REV01, "s2" for Sonic 2 World REV01, "s3k" for the
        /// Sonic 3 &amp; Knuckles locked-on combined image, "kis2" for the
        /// Knuckles in Sonic 2 lock-on image (S2 recorder pipeline). The CLI
        /// dispatches on this instead of a game flag so a supplied ROM
        /// always selects the matching recorder pipeline.
        /// </summary>
        public static string DetectGame(byte[] romBytes)
        {
            string actualSha1 = ComputeSha1(romBytes);
            if (string.Equals(
                Sonic1Rev01Sha1, actualSha1, StringComparison.Ordinal))
            {
                return "s1";
            }
            if (string.Equals(
                Sonic2Rev01Sha1, actualSha1, StringComparison.Ordinal))
            {
                return "s2";
            }
            if (string.Equals(
                Sonic3kLockOnSha1, actualSha1, StringComparison.Ordinal))
            {
                return "s3k";
            }
            if (string.Equals(
                Kis2LockOnSha1, actualSha1, StringComparison.Ordinal))
            {
                return "kis2";
            }
            throw new InvalidOperationException(
                "ROM SHA-1 " + actualSha1 + " is not a supported trace ROM;"
                + " expected Sonic 1 World REV01 (" + Sonic1Rev01Sha1
                + "), Sonic 2 World REV01 (" + Sonic2Rev01Sha1
                + "), Sonic 3 & Knuckles locked-on ("
                + Sonic3kLockOnSha1 + "), or Knuckles in Sonic 2 lock-on ("
                + Kis2LockOnSha1 + ").");
        }

        public static string ComputeSha1(byte[] romBytes)
        {
            if (romBytes == null)
            {
                throw new ArgumentNullException("romBytes");
            }

            using (SHA1 sha1 = SHA1.Create())
            {
                return BitConverter.ToString(sha1.ComputeHash(romBytes))
                    .Replace("-", "");
            }
        }
    }
}
