using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>
    /// Reduces two independently captured S3K request raw streams to the bounded
    /// set of source-observed music-mailbox requests.
    ///
    /// Both streams must be byte-identical and must both declare the fixed
    /// Sonic/Tails request schema, identity and interval. The extractor adds
    /// nothing: every emitted request is a byte a capture read out of Z80 RAM
    /// $1C0A while the bus was held. It rejects a hand-inserted value by
    /// construction, because it copies only what both attested streams contain.
    /// </summary>
    internal static class S3kRequestObservationExtractor
    {
        internal const string Schema =
            "openggf.s3k-preconsumption-request-observations.v1";

        internal sealed class Observation
        {
            internal Observation(int row, int request)
            { Row = row; Request = request; }
            internal int Row { get; private set; }
            internal int Request { get; private set; }
        }

        internal static string Extract(string firstRawPath, string secondRawPath,
            string outputPath)
        {
            RequireAbsoluteExisting(firstRawPath, "first raw stream");
            RequireAbsoluteExisting(secondRawPath, "second raw stream");
            if (string.IsNullOrEmpty(outputPath) || !Path.IsPathRooted(outputPath))
                throw new ArgumentException(
                    "The extractor output path must be absolute.", "outputPath");
            if (File.Exists(outputPath))
                throw new IOException("The extractor output already exists: " + outputPath);
            if (string.Equals(Path.GetFullPath(firstRawPath),
                Path.GetFullPath(secondRawPath), StringComparison.Ordinal))
                throw new ArgumentException(
                    "The two captures must be distinct files.", "secondRawPath");

            string firstDigest = Sha256(firstRawPath);
            string secondDigest = Sha256(secondRawPath);
            if (!string.Equals(firstDigest, secondDigest, StringComparison.Ordinal))
                throw new InvalidDataException(
                    "The two captures disagree: " + firstDigest + " and " + secondDigest + ".");

            JObject metadata;
            List<Observation> observations = Read(firstRawPath, out metadata);
            JObject secondMetadata;
            List<Observation> confirming = Read(secondRawPath, out secondMetadata);
            if (observations.Count != confirming.Count)
                throw new InvalidDataException("The two captures observed different requests.");
            for (int index = 0; index < observations.Count; index++)
                if (observations[index].Row != confirming[index].Row
                    || observations[index].Request != confirming[index].Request)
                    throw new InvalidDataException(
                        "The two captures observed different requests.");

            var rows = new JArray();
            foreach (Observation value in observations)
                rows.Add(new JObject
                {
                    ["row"] = value.Row,
                    ["request"] = value.Request
                });

            var result = new JObject
            {
                ["schema"] = Schema,
                ["production_bound"] = false,
                ["rom_sha1"] = metadata["rom_sha1"],
                ["bk2_basename"] = metadata["bk2_basename"],
                ["bk2_sha256"] = metadata["bk2_sha256"],
                ["bk2_row_count"] = metadata["bk2_row_count"],
                ["service_manifest_sha256"] = metadata["service_manifest_sha256"],
                ["first_row"] = metadata["first_row"],
                ["exclusive_end"] = metadata["exclusive_end"],
                ["observed_at_pc"] = metadata["request_end_pc"],
                ["observed_opcode"] = metadata["request_end_opcode"],
                ["mailbox_address"] = metadata["request_mailbox_address"],
                ["capture_sha256"] = firstDigest,
                ["duplicate_capture_sha256"] = secondDigest,
                ["observations"] = rows
            };
            string staging = outputPath + ".staging";
            if (File.Exists(staging)) File.Delete(staging);
            File.WriteAllText(staging,
                result.ToString(Newtonsoft.Json.Formatting.Indented) + "\n");
            File.Move(staging, outputPath);
            return outputPath;
        }

        private static List<Observation> Read(string path, out JObject metadata)
        {
            metadata = null;
            var result = new List<Observation>();
            int lastRow = -1;
            using (StreamReader input = File.OpenText(path))
            {
                string line;
                while ((line = input.ReadLine()) != null)
                {
                    if (line.Length == 0) continue;
                    JObject row = JObject.Parse(line);
                    string kind = (string)row["type"];
                    if (kind == "metadata")
                    {
                        if (metadata != null)
                            throw new InvalidDataException("Duplicate metadata row.");
                        if ((string)row["schema"]
                                != S3kPreconsumptionRequestProfile.Schema
                            || (string)row["authority"]
                                != "S3K_SONIC_TAILS_REQUEST_DIAGNOSTIC")
                            throw new InvalidDataException(
                                "The raw stream is not the fixed S3K request capture.");
                        metadata = row;
                        continue;
                    }
                    if (kind != "frame") continue;
                    if (metadata == null)
                        throw new InvalidDataException("A frame row preceded the metadata row.");
                    int rowIndex = (int)row["row"];
                    if (rowIndex <= lastRow)
                        throw new InvalidDataException("Frame rows are not ascending.");
                    lastRow = rowIndex;
                    JArray requests = row["requests"] as JArray;
                    if (requests == null) continue;
                    foreach (JToken request in requests)
                    {
                        // Only the release-side observation carries the byte; the
                        // entry-side marker declares no range.
                        if ((int)request["hook_token"]
                            != S3kPreconsumptionRequestProfile.EndToken) continue;
                        if ((uint)(int)request["pc"]
                                != S3kPreconsumptionRequestProfile.EndPc
                            || (int)request["range_id"]
                                != S3kPreconsumptionRequestProfile.MailboxRangeId)
                            throw new InvalidDataException(
                                "A request observation is not the fixed mailbox boundary.");
                        string hex = (string)request["bytes_hex"];
                        if (hex == null || hex.Length != 2)
                            throw new InvalidDataException(
                                "A request observation is not exactly one byte.");
                        result.Add(new Observation(rowIndex,
                            int.Parse(hex, NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture)));
                    }
                }
            }
            if (metadata == null)
                throw new InvalidDataException("The raw stream has no metadata row.");
            if (result.Count == 0)
                throw new InvalidDataException("The raw stream observed no requests.");
            return result;
        }

        private static void RequireAbsoluteExisting(string path, string label)
        {
            if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path)
                || !File.Exists(path))
                throw new ArgumentException(
                    "The " + label + " must be an existing absolute file.", "path");
        }

        private static string Sha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream input = File.OpenRead(path))
            {
                byte[] digest = sha.ComputeHash(input);
                char[] value = new char[digest.Length * 2];
                const string alphabet = "0123456789abcdef";
                for (int index = 0; index < digest.Length; index++)
                {
                    value[index * 2] = alphabet[digest[index] >> 4];
                    value[index * 2 + 1] = alphabet[digest[index] & 15];
                }
                return new string(value);
            }
        }
    }
}
