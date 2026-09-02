using System;
using System.IO;
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
            "d6648cf77204f26041356a788a81cecc6a7006ee5d6694f266211b172d4babec";

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
