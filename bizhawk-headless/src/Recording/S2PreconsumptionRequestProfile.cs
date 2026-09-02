using System;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>
    /// Fixed but deliberately unbound request-observation candidate. It is not
    /// a capture authority until a separately reviewed capability authenticates
    /// the candidate manifest, native observer, and complete-run inventory.
    /// </summary>
    internal static class S2PreconsumptionRequestProfile
    {
        internal const string CandidateNativePatchFile =
            "bizhawk-headless/native/gpgx-audio-observer-candidates/0001-s2-request-successor-ordinal.patch";
        internal const string CandidateNativePatchSha256 =
            "03ee8c72e14c96875cdbda4dc401bed358a8e4e1314d9fc63907598b24a1ba5b";
        internal const string CandidateNativeRecipeFile =
            "bizhawk-headless/native/gpgx-audio-observer-candidates/s2-request-selftest-recipe.json";
        internal const string CandidateNativeRecipeSha256 =
            "f1bae0e92c238c8fb92fc424482e51facecc1aabb01e9443c889ef49e7450312";

        internal sealed class Candidate
        {
            internal Candidate(uint pc, string opcode, ushort markerToken,
                bool productionBound)
            {
                Pc = pc; Opcode = opcode; MarkerToken = markerToken;
                ProductionBound = productionBound;
            }

            internal uint Pc { get; private set; }
            internal string Opcode { get; private set; }
            internal ushort MarkerToken { get; private set; }
            internal bool ProductionBound { get; private set; }

            internal void RequireProductionAuthority()
            {
                if (!ProductionBound) throw new InvalidOperationException(
                    "The fixed S2 request candidate is unbound and cannot capture production authority.");
            }
        }

        internal static Candidate LoadCandidate(string path)
        {
            if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path)
                || !File.Exists(path))
                throw new InvalidDataException(
                    "The S2 request candidate manifest must be an existing absolute file.");
            JObject root = JObject.Parse(File.ReadAllText(path));
            RequireEqual("openggf.s2-preconsumption-request-manifest.v1",
                RequiredString(root, "schema"), "schema");
            if ((bool?)root["production_bound"] != false)
                throw new InvalidDataException("The S2 request candidate must remain unbound.");
            RequireEqual(S2AudioObserverProfile.ServiceManifestSha256,
                RequiredString(root, "base_service_manifest_sha256"),
                "base manifest identity");
            RequireEqual(CandidateNativePatchFile,
                RequiredString(root, "candidate_native_patch_file"),
                "candidate native patch file");
            RequireEqual(CandidateNativePatchSha256,
                RequiredString(root, "candidate_native_patch_sha256"),
                "candidate native patch identity");
            RequireEqual(CandidateNativeRecipeFile,
                RequiredString(root, "candidate_native_recipe_file"),
                "candidate native recipe file");
            RequireEqual(CandidateNativeRecipeSha256,
                RequiredString(root, "candidate_native_recipe_sha256"),
                "candidate native recipe identity");
            JObject transfer = root["request_transfer"] as JObject;
            if (transfer == null) throw new InvalidDataException(
                "The S2 request candidate has no transfer definition.");
            RequireEqual("M68K", RequiredString(transfer, "cpu"), "CPU");
            if ((uint?)transfer["pc"] != S2PreconsumptionRequestObserver.Pc
                || RequiredString(transfer, "opcode") != "13801009"
                || (int?)transfer["native_action"] != 7
                || (int?)transfer["marker_event_kind"] != 10
                || (int?)transfer["marker_value"] != 3
                || (ushort?)transfer["marker_token"]
                    != S2PreconsumptionRequestObserver.MarkerToken)
                throw new InvalidDataException(
                    "The S2 request candidate fixed hook identity differs.");
            JArray kinds = transfer["marker_expected_kinds"] as JArray;
            if (kinds == null || kinds.Count != 1 || (int?)kinds[0] != 0)
                throw new InvalidDataException(
                    "The S2 request candidate marker topology differs.");
            RequireEqual("docs/s2disasm/s2.asm",
                RequiredString(transfer, "source_file"), "source file");
            RequireEqual("sndDriverInput accepted M68K-to-Z80 transfer",
                RequiredString(transfer, "source_label"), "source label");
            return new Candidate(S2PreconsumptionRequestObserver.Pc,
                "13801009", S2PreconsumptionRequestObserver.MarkerToken,
                false);
        }

        /// <summary>
        /// Builds the candidate observer only from the authenticated v2
        /// manifest plus the one fixed, comparison-only M68K hook. The
        /// returned observer remains unbound: no production capability or
        /// CLI route accepts this profile.
        /// </summary>
        internal static CompleteRunAudioObserver CreateObserver(
            Candidate candidate, string baseServiceManifestPath,
            IGpgxAudioTraceApi api)
        {
            if (candidate == null) throw new ArgumentNullException("candidate");
            if (api == null) throw new ArgumentNullException("api");
            if (candidate.Pc != S2PreconsumptionRequestObserver.Pc
                || candidate.Opcode != "13801009"
                || candidate.MarkerToken
                    != S2PreconsumptionRequestObserver.MarkerToken
                || candidate.ProductionBound)
                throw new InvalidDataException(
                    "The S2 request candidate fixed hook profile differs.");
            if (string.IsNullOrEmpty(baseServiceManifestPath)
                || !Path.IsPathRooted(baseServiceManifestPath)
                || !File.Exists(baseServiceManifestPath))
                throw new InvalidDataException(
                    "The S2 candidate base service manifest must be an existing absolute file.");
            RequireEqual(S2AudioObserverProfile.ServiceManifestSha256,
                Sha256File(baseServiceManifestPath),
                "base manifest file identity");
            return GpgxAudioServiceManifest.LoadS2RequestCandidate(
                baseServiceManifestPath,
                new S2AudioObserverProfile.PrepublicationApi(api));
        }

        private static string Sha256File(string path)
        {
            using (FileStream input = File.OpenRead(path))
            using (SHA256 digest = SHA256.Create())
            {
                byte[] value = digest.ComputeHash(input);
                char[] result = new char[value.Length * 2];
                const string hex = "0123456789abcdef";
                for (int index = 0; index < value.Length; index++)
                {
                    result[index * 2] = hex[value[index] >> 4];
                    result[index * 2 + 1] = hex[value[index] & 15];
                }
                return new string(result);
            }
        }

        private static string RequiredString(JObject value, string name)
        {
            string result = (string)value[name];
            if (string.IsNullOrEmpty(result)) throw new InvalidDataException(
                "The S2 request candidate field is missing: " + name + ".");
            return result;
        }

        private static void RequireEqual(string expected, string actual,
            string field)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                throw new InvalidDataException(
                    "The S2 request candidate " + field + " differs.");
        }
    }
}
