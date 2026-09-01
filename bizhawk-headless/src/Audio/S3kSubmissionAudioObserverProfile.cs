using System;
using System.IO;
using System.Security.Cryptography;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>
    /// Test-only capability shape for the Play_Music mailbox boundary. It has
    /// no BK2 identity, capture runner, CLI route, or publication authority.
    /// A reviewed duplicate Knuckles capture and a new identity cascade are
    /// required before this shape can become a production profile.
    /// </summary>
    internal static class S3kSubmissionAudioObserverProfile
    {
        internal const string Schema =
            "openggf.s3k-complete-run-audio-raw.v2";
        internal const string ManifestSha256 =
            "a1736a1ec5e279299f15177192eefc737efbbe4d046d3260a942f7cb3074a16c";

        internal static readonly S3kRawAudioAuthority
            UnboundAuthorityForTesting = new S3kRawAudioAuthority(
                Schema, S3kAudioObserverProfile.RomSha1, null,
                ManifestSha256, 0, 1,
                S3kAudioObserverProfile.DriverStateStart,
                S3kAudioObserverProfile.DriverStateExclusiveEnd,
                false, true);

        internal static CompleteRunAudioObserver CreateUnboundObserver(
            string manifestPath, IGpgxAudioTraceApi api)
        {
            if (string.IsNullOrEmpty(manifestPath)
                || !Path.IsPathRooted(manifestPath))
                throw new ArgumentException(
                    "The unbound S3K submission manifest path must be absolute.",
                    "manifestPath");
            if (api == null) throw new ArgumentNullException("api");
            if (!File.Exists(manifestPath))
                throw new FileNotFoundException(
                    "The unbound S3K submission manifest is absent.", manifestPath);
            string actual = Sha256(manifestPath);
            if (!string.Equals(actual, ManifestSha256,
                StringComparison.Ordinal))
                throw new InvalidDataException(
                    "The unbound S3K submission manifest SHA-256 changed.");
            return GpgxAudioServiceManifest.Load(manifestPath, "s3k", api);
        }

        private static string Sha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream input = File.OpenRead(path))
            {
                byte[] digest = sha.ComputeHash(input);
                return BitConverter.ToString(digest).Replace("-", "")
                    .ToLowerInvariant();
            }
        }
    }
}
