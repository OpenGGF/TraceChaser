using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BizHawk.Emulation.Cores.Consoles.Sega.gpgx;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>
    /// The bounded S2 request-window producer, driven by explicit arguments.
    /// Capture runs one recording over one movie-row interval and writes the
    /// raw-v3 stream; extraction projects that stream onto the bounded
    /// comparison payload with the capability and attestation it derives from
    /// the stream itself.
    ///
    /// Nothing here pins a window, a recording or an emulator build: the
    /// caller supplies the movie and its SHA-256, the interval, the service
    /// and candidate manifests, and the output paths, and the installed
    /// observer identity is checked by the profile that already owns it. The
    /// payload stays production_bound:false, so it is comparison-only
    /// reference data and can hydrate nothing.
    /// </summary>
    internal static class S2RequestWindowProducer
    {
        internal const string AttestationSchema =
            "openggf.s2-request-aware-raw-v3-attestation.v1";
        internal const string AuthorityId = "s2-request-candidate-unbound";
        private const int MarkerKind = 10;
        private const int MarkerValue = 3;
        internal const string DriverStateSchema = "openggf.s2-driver-state-reference.v2";
        private const int DriverRamRangeId = 3;
        private const int DriverRamStart = 0x12FE;
        private const int DriverRamExclusiveEnd = 0x2000;
        private const int VIntServiceKind = 3;

        /// <summary>Names the three files an extraction writes.</summary>
        internal sealed class ExtractionOutputs
        {
            internal ExtractionOutputs(string payload, string capability,
                string attestation)
            {
                Payload = payload;
                Capability = capability;
                Attestation = attestation;
            }

            internal string Payload { get; private set; }
            internal string Capability { get; private set; }
            internal string Attestation { get; private set; }
        }

        /// <summary>
        /// Runs the movie from its first row to <paramref name="exclusiveEnd"/>
        /// and publishes rows [firstRow, exclusiveEnd) as raw-v3.
        /// </summary>
        internal static void Capture(string romPath, string moviePath,
            string movieSha256, string serviceManifestPath,
            string candidateManifestPath, int firstRow, int exclusiveEnd,
            string outputPath, Func<string, GPGX.GPGXSyncSettings, IGpgxHost> openHost,
            TextWriter stdout)
        {
            if (openHost == null) throw new ArgumentNullException("openHost");
            if (stdout == null) throw new ArgumentNullException("stdout");
            RequireInterval(firstRow, exclusiveEnd);
            RequireAbsentAbsolute(outputPath, "raw output");
            S2AudioObserverProfile.ValidateRom(romPath);
            string actual = Sha256File(moviePath);
            if (!string.Equals(actual, RequireHex(movieSha256, "movie SHA-256"),
                StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The supplied movie SHA-256 does not match " + moviePath
                    + ": " + actual + ".");
            }
            Bk2Movie movie = Bk2Reader.Read(moviePath);
            if (movie.FrameCount < exclusiveEnd)
            {
                throw new InvalidDataException(
                    "The movie has " + movie.FrameCount.ToString(
                        CultureInfo.InvariantCulture)
                    + " rows, short of the requested window end "
                    + exclusiveEnd.ToString(CultureInfo.InvariantCulture) + ".");
            }

            using (var host = new RequestWindowHost(
                (GpgxHost)openHost(romPath, movie.SyncSettings)))
            using (var writer = new StreamWriter(outputPath, false,
                new UTF8Encoding(false)))
            using (S2CompleteAudioCaptureRunner.RequestAwareRawV3Candidate producer =
                S2CompleteAudioCaptureRunner.OpenRequestAwareRawV3CandidateForWindow(
                    candidateManifestPath, serviceManifestPath, host, writer,
                    firstRow, exclusiveEnd, actual))
            using (IEnumerator<Bk2Frame> rows =
                movie.OpenFrameStream().GetEnumerator())
            {
                for (int row = 0; row < exclusiveEnd; row++)
                {
                    if (!rows.MoveNext())
                    {
                        throw new InvalidDataException(
                            "The movie ended before the requested window.");
                    }
                    try { producer.AdvanceRow(row, rows.Current); }
                    catch (Exception error)
                    {
                        throw new InvalidOperationException(
                            "The request-window capture failed at movie row "
                            + row.ToString(CultureInfo.InvariantCulture) + ": "
                            + error.Message, error);
                    }
                }
                producer.Complete();
            }
            stdout.Write("Request-window raw output: " + outputPath + "\n"
                + "Raw SHA-256: " + Sha256File(outputPath) + "\n");
        }

        /// <summary>
        /// Projects one captured raw-v3 stream onto the bounded payload,
        /// writing the derived capability and attestation beside it.
        /// </summary>
        internal static ExtractionOutputs Extract(string rawPath,
            string serviceManifestPath, string candidateTemplatePath,
            int firstRow, int exclusiveEnd, string outputDirectory,
            TextWriter stdout)
        {
            if (stdout == null) throw new ArgumentNullException("stdout");
            RequireInterval(firstRow, exclusiveEnd);
            RequireExistingAbsolute(rawPath, "raw");
            RequireExistingAbsolute(candidateTemplatePath, "capability template");
            RequireAbsoluteDirectory(outputDirectory);
            var outputs = new ExtractionOutputs(
                Path.Combine(outputDirectory, "s2-request-window.oracle-raw-v2.jsonl"),
                Path.Combine(outputDirectory, "s2-request-window.capability.json"),
                Path.Combine(outputDirectory, "s2-request-window.attestation.json"));
            foreach (string path in new[] { outputs.Payload, outputs.Capability,
                outputs.Attestation })
            {
                RequireAbsentAbsolute(path, "extraction output");
            }

            byte[] raw = File.ReadAllBytes(rawPath);
            JObject capability = DeriveCapability(raw, candidateTemplatePath,
                firstRow, exclusiveEnd);
            string movieSha256 = (string)capability["bk2_sha256"];
            File.WriteAllText(outputs.Capability,
                capability.ToString(Formatting.None), new UTF8Encoding(false));
            File.WriteAllText(outputs.Attestation,
                Attestation(raw, capability).ToString(Formatting.None),
                new UTF8Encoding(false));
            S2RequestAwareOracleV2Extractor.ForWindow(firstRow, exclusiveEnd,
                firstRow, exclusiveEnd, serviceManifestPath, movieSha256)
                .ExtractWindow(rawPath, outputs.Capability, outputs.Attestation,
                    outputs.Payload);
            stdout.Write("Request-window payload: " + outputs.Payload + "\n"
                + "Payload SHA-256: " + Sha256File(outputs.Payload) + "\n"
                + "Capability SHA-256: " + Sha256File(outputs.Capability) + "\n"
                + "Attestation SHA-256: " + Sha256File(outputs.Attestation) + "\n");
            return outputs;
        }


        /// <summary>
        /// The bounded S2 driver-state reference. One row per completed driver
        /// service, sampled by the observer core as a completion snapshot on
        /// the driver's own service boundaries: the two zVInt returns, the
        /// common exit in zUpdateDAC at Z80 PC 00E7h and the DAC-queued exit at
        /// 010Fh (s2.sounddriver.asm:496-502 and :531-535), plus the
        /// SoundDriverLoad release. Never mid-invocation, and never on a frame
        /// boundary: a service that overruns its frame completes in a later one
        /// and owns its whole span of writes.
        ///
        /// Writes are partitioned by the same boundary, so every YM/PSG write
        /// appears exactly once in stream order, and the request markers
        /// observed since the previous boundary ride along so a request
        /// resolves against the service that consumed it. The frame field is
        /// provenance only: nothing compared is derived from it.
        /// </summary>
        internal static void CaptureDriverState(string romPath, string moviePath,
            string movieSha256, string oracleManifestPath, int firstRow,
            int exclusiveEnd, string outputPath,
            Func<string, GPGX.GPGXSyncSettings, IGpgxHost> openHost, TextWriter stdout)
        {
            if (openHost == null) throw new ArgumentNullException("openHost");
            if (stdout == null) throw new ArgumentNullException("stdout");
            RequireInterval(firstRow, exclusiveEnd);
            RequireAbsentAbsolute(outputPath, "driver-state output");
            RequireExistingAbsolute(oracleManifestPath, "oracle manifest");
            S2AudioObserverProfile.ValidateRom(romPath);
            string actualMovie = Sha256File(moviePath);
            if (!string.Equals(actualMovie,
                RequireHex(movieSha256, "movie SHA-256"), StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The supplied movie SHA-256 does not match " + moviePath
                    + ": " + actualMovie + ".");
            }
            byte[] manifestBytes = File.ReadAllBytes(oracleManifestPath);
            string manifestSha256 = Digest(manifestBytes);
            Bk2Movie movie = Bk2Reader.Read(moviePath);
            if (movie.FrameCount < exclusiveEnd)
            {
                throw new InvalidDataException("The movie has "
                    + movie.FrameCount.ToString(CultureInfo.InvariantCulture)
                    + " rows, short of the requested window end "
                    + exclusiveEnd.ToString(CultureInfo.InvariantCulture) + ".");
            }

            using (GpgxHost host = (GpgxHost)openHost(romPath, movie.SyncSettings))
            using (var writer = new StreamWriter(outputPath, false,
                new UTF8Encoding(false)))
            {
                CompleteRunAudioObserver observer =
                    GpgxAudioServiceManifest.LoadS2RequestCandidate(manifestBytes,
                        new S2AudioObserverProfile.PrepublicationApi(
                            host.CreateAudioTraceApi()));
                writer.Write(new JObject
                {
                    ["row"] = "metadata",
                    ["schema"] = DriverStateSchema,
                    ["rom_sha1"] = S2AudioObserverProfile.RomSha1,
                    ["bk2_sha256"] = actualMovie,
                    ["first_row"] = firstRow,
                    ["exclusive_end"] = exclusiveEnd,
                    ["snapshot_start"] = DriverRamStart,
                    ["snapshot_exclusive_end"] = DriverRamExclusiveEnd,
                    ["sampling"] = "zvint_return_completion_snapshot",
                    ["tick_semantics"] = "one_completed_driver_service",
                    ["writes_partition"] = "service_completion_boundary",
                    ["frame_field"] = "provenance_only",
                    ["production_bound"] = false,
                    ["manifest"] = Path.GetFileName(oracleManifestPath),
                    ["manifest_sha256"] = manifestSha256
                }.ToString(Formatting.None) + "\n");

                int port0Latch = 0, port1Latch = 0;
                int ticks = 0, zeroServiceFrames = 0, multiServiceFrames = 0;
                long writeCount = 0, residualWrites = 0;
                var pendingWrites = new JArray();
                var pendingRequests = new JArray();
                var ram = new byte[DriverRamExclusiveEnd - DriverRamStart];
                var seen = new bool[ram.Length];
                bool collecting = false, complete = false;
                using (IEnumerator<Bk2Frame> rows =
                    movie.OpenFrameStream().GetEnumerator())
                {
                    for (int frame = 0; frame < exclusiveEnd; frame++)
                    {
                        if (!rows.MoveNext())
                            throw new InvalidDataException(
                                "The movie ended before the requested window.");
                        Bk2Frame current = rows.Current;
                        int frameTicks = 0;
                        int published = frame;
                        observer.CaptureFrame(
                            () => { S1TraceCaptureRunner.ApplyFrame(current, host); host.Advance(); },
                            (events, count) =>
                            {
                                for (int index = 0; index < count; index++)
                                {
                                    GpgxAudioTraceEvent value = events[index];
                                    switch (value.Kind)
                                    {
                                        case 3:
                                            if (value.Subject == 0) { port0Latch = value.Value; break; }
                                            if (value.Subject == 2) { port1Latch = value.Value; break; }
                                            {
                                                int port = value.Subject < 2 ? 0 : 1;
                                                pendingWrites.Add(new JArray("ym", port,
                                                    port == 0 ? port0Latch : port1Latch,
                                                    value.Value, value.SourceCpu));
                                            }
                                            break;
                                        case 4:
                                            pendingWrites.Add(new JArray("psg", value.Value,
                                                value.SourceCpu));
                                            break;
                                        case 10:
                                            // The request marker rides along as an
                                            // observation; it carries no compared value.
                                            pendingRequests.Add(new JObject
                                            {
                                                ["marker"] = value.Subject,
                                                ["a7"] = value.Payload.ToString(
                                                    CultureInfo.InvariantCulture)
                                            });
                                            break;
                                        case 5:
                                            if (value.Subject != DriverRamRangeId) break;
                                            Array.Clear(ram, 0, ram.Length);
                                            Array.Clear(seen, 0, seen.Length);
                                            collecting = true; complete = false;
                                            break;
                                        case 6:
                                            if (value.Subject != DriverRamRangeId || !collecting) break;
                                            CopySnapshotPayload(value, ram, seen);
                                            break;
                                        case 7:
                                            if (value.Subject != DriverRamRangeId || !collecting) break;
                                            if (value.Offset != ram.Length)
                                                throw new InvalidDataException(
                                                    "The driver-RAM snapshot ended at offset "
                                                    + value.Offset + ".");
                                            for (int b = 0; b < ram.Length; b++)
                                                if (!seen[b])
                                                    throw new InvalidDataException(
                                                        "The driver-RAM snapshot missed byte " + b + ".");
                                            collecting = false; complete = true;
                                            break;
                                        case 2:
                                            // Only the vertical-interrupt service
                                            // defines a tick here. SoundDriverLoad
                                            // completes long before any mid-run
                                            // window and carries the full-RAM range
                                            // rather than this one.
                                            if (value.ServiceKindId != VIntServiceKind) break;
                                            if (!complete)
                                                throw new InvalidDataException(
                                                    "Service kind " + value.ServiceKindId
                                                    + " completed without a driver-RAM snapshot"
                                                    + " at movie row " + published + ".");
                                            frameTicks++;
                                            if (published >= firstRow)
                                            {
                                                var tick = new JObject
                                                {
                                                    ["row"] = "tick",
                                                    ["tick"] = ticks,
                                                    ["frame"] = published,
                                                    ["lag"] = host.IsLagged,
                                                    ["service"] = "vint",
                                                    ["writes"] = pendingWrites,
                                                    ["requests"] = pendingRequests,
                                                    ["ram"] = HexBytes(ram)
                                                };
                                                writer.Write(tick.ToString(Formatting.None) + "\n");
                                                ticks++;
                                                // Counted over the published window
                                                // only; writes before it belong to
                                                // services this reference never shows.
                                                writeCount += pendingWrites.Count;
                                            }
                                            pendingWrites = new JArray();
                                            pendingRequests = new JArray();
                                            complete = false;
                                            break;
                                    }
                                }
                            });
                        if (frame >= firstRow)
                        {
                            if (frameTicks == 0) zeroServiceFrames++;
                            else if (frameTicks > 1) multiServiceFrames++;
                        }
                    }
                }
                // Writes after the final completed service in the window belong
                // to no published tick.
                residualWrites = pendingWrites.Count;
                writer.Write(new JObject
                {
                    ["row"] = "terminal",
                    ["ticks"] = ticks,
                    ["frames"] = exclusiveEnd - firstRow,
                    ["write_count"] = writeCount,
                    ["residual_write_count"] = residualWrites,
                    ["zero_service_frames"] = zeroServiceFrames,
                    ["multi_service_frames"] = multiServiceFrames
                }.ToString(Formatting.None) + "\n");
                writer.Flush();
                stdout.Write("Driver-state output: " + outputPath + "\n"
                    + "Ticks: " + ticks.ToString(CultureInfo.InvariantCulture) + "\n"
                    + "Zero-service frames: "
                    + zeroServiceFrames.ToString(CultureInfo.InvariantCulture) + "\n"
                    + "Multi-service frames: "
                    + multiServiceFrames.ToString(CultureInfo.InvariantCulture) + "\n");
            }
            stdout.Write("Driver-state SHA-256: " + Sha256File(outputPath) + "\n");
        }

        private static void CopySnapshotPayload(GpgxAudioTraceEvent value,
            byte[] ram, bool[] seen)
        {
            int offset = (int)value.Offset;
            int length = value.PayloadLength;
            if (length < 0 || length > 8 || offset < 0 || offset + length > ram.Length)
                throw new InvalidDataException(
                    "The driver-RAM snapshot chunk is out of range at offset " + offset + ".");
            ulong payload = value.Payload;
            for (int index = 0; index < length; index++)
            {
                ram[offset + index] = (byte)(payload & 0xFF);
                seen[offset + index] = true;
                payload >>= 8;
            }
        }

        private static string HexBytes(byte[] value)
        {
            var builder = new StringBuilder(value.Length * 2);
            for (int index = 0; index < value.Length; index++)
                builder.Append(value[index].ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        /// <summary>
        /// Builds the capability from the committed template plus the exact
        /// inventory of the raw stream. Every identity comes from the template
        /// or the stream; nothing is invented, and nothing is authenticated
        /// beyond what the stream itself already asserts.
        /// </summary>
        internal static JObject DeriveCapability(byte[] raw,
            string candidateTemplatePath, int windowFirst, int windowEnd)
        {
            if (raw == null) throw new ArgumentNullException("raw");
            string[] lines = Encoding.UTF8.GetString(raw).Split(new[] { '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 3)
            {
                throw new InvalidDataException("The raw stream is too short.");
            }
            long baseCount = 0, allCount = 0, markerCount = 0, requestCount = 0;
            int maximumRequestOccupancy = 0, resumeCount = 0, pcmCount = 0;
            var baseBytes = new List<byte>();
            var allBytes = new List<byte>();
            var markerBytes = new List<byte>();
            var requestBytes = new List<byte>();
            JObject cutoff = null;
            JToken firstResume = null, firstPcm = null;
            byte[] terminalState = null;
            foreach (string line in lines)
            {
                JObject record = JObject.Parse(line);
                string type = (string)record["type"];
                if (type == "frame")
                {
                    foreach (JToken token in (JArray)record["events"])
                    {
                        var value = (JObject)token;
                        byte[] bytes = Canonical(value);
                        allBytes.AddRange(bytes);
                        allCount++;
                        // The extractor's own marker classification: either
                        // request marker token, or the fixed action-7 record at
                        // the request PC.
                        bool marker =
                            (int)value["subject"]
                                == S2PreconsumptionRequestObserver.MarkerToken
                            || (int)value["subject"]
                                == S2PreconsumptionRequestObserver.Kind3MarkerToken
                            || ((int)value["kind"] == MarkerKind
                                && (int)value["value"] == MarkerValue
                                && (int)value["pc"]
                                    == S2PreconsumptionRequestObserver.Pc);
                        if (marker)
                        {
                            markerBytes.AddRange(bytes);
                            markerCount++;
                        }
                        else
                        {
                            baseBytes.AddRange(bytes);
                            baseCount++;
                        }
                    }
                    var transfers = (JArray)record["request_transfers"];
                    foreach (JToken transfer in transfers)
                    {
                        requestBytes.AddRange(Canonical(transfer));
                        requestCount++;
                    }
                    maximumRequestOccupancy = Math.Max(maximumRequestOccupancy,
                        transfers.Count);
                    if (record["override_resume"].Type != JTokenType.Null)
                    {
                        resumeCount++;
                        if (firstResume == null) firstResume = record["override_resume"];
                    }
                    if (record["pcm"].Type != JTokenType.Null)
                    {
                        pcmCount++;
                        if (firstPcm == null) firstPcm = record["pcm"];
                    }
                }
                else if (type == "cutoff")
                {
                    cutoff = record;
                    terminalState = DecodeHex((string)record["state_hex"]);
                }
            }
            if (cutoff == null || terminalState == null)
            {
                throw new InvalidDataException("The raw stream has no cutoff.");
            }
            JObject metadata = JObject.Parse(lines[0]);
            JObject capability = JObject.Parse(
                File.ReadAllText(candidateTemplatePath));
            capability["bk2_sha256"] = (string)metadata["bk2_sha256"];
            capability["harness_executable_sha256"] = Digest(
                File.ReadAllBytes(typeof(GpgxHost).Assembly.Location));
            capability["first_row"] = (int)metadata["first_row"];
            capability["exclusive_end"] = (int)metadata["exclusive_end"];
            capability["window_first_row"] = windowFirst;
            capability["window_exclusive_end"] = windowEnd;
            capability["base_event_count"] = baseCount;
            capability["all_event_count"] = allCount;
            capability["marker_event_count"] = markerCount;
            capability["request_count"] = requestCount;
            capability["base_event_sha256"] = Digest(baseBytes.ToArray());
            capability["all_event_sha256"] = Digest(allBytes.ToArray());
            capability["marker_event_sha256"] = Digest(markerBytes.ToArray());
            capability["request_sha256"] = Digest(requestBytes.ToArray());
            capability["max_request_occupancy"] = maximumRequestOccupancy;
            capability["override_resume_count"] = resumeCount;
            capability["override_resume_sha256"] = Digest(resumeCount == 0
                ? new byte[0] : Canonical(firstResume));
            capability["pcm_count"] = pcmCount;
            capability["pcm_sha256"] = Digest(pcmCount == 0
                ? new byte[0] : Canonical(firstPcm));
            capability["cutoff_frontier_sha256"] = Digest(Canonical(cutoff));
            capability["terminal_state_sha256"] = Digest(terminalState);
            return capability;
        }

        private static JObject Attestation(byte[] raw, JObject capability)
        {
            return new JObject
            {
                ["schema"] = AttestationSchema,
                ["raw_sha256"] = Digest(raw),
                ["raw_byte_count"] = raw.Length,
                ["status_count"] = 1,
                ["fault_count"] = 0,
                ["overflow_count"] = 0,
                ["authority_id"] = AuthorityId,
                ["capability_sha256"] = Digest(Canonical(capability))
            };
        }

        private static void RequireInterval(int firstRow, int exclusiveEnd)
        {
            if (firstRow < 0 || exclusiveEnd <= firstRow)
            {
                throw new ArgumentException(
                    "The request window is not a valid interval.");
            }
        }

        private static void RequireAbsentAbsolute(string path, string label)
        {
            if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path)
                || File.Exists(path))
            {
                throw new InvalidOperationException(
                    "The " + label + " must be an absolute absent file.");
            }
        }

        private static void RequireExistingAbsolute(string path, string label)
        {
            if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path)
                || !File.Exists(path))
            {
                throw new InvalidOperationException(
                    "The " + label + " must be an existing absolute file.");
            }
        }

        private static void RequireAbsoluteDirectory(string path)
        {
            if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path)
                || !Directory.Exists(path))
            {
                throw new InvalidOperationException(
                    "The output directory must be an existing absolute"
                    + " directory.");
            }
        }

        private static string RequireHex(string value, string label)
        {
            if (value == null || value.Length != 64)
            {
                throw new ArgumentException("The " + label
                    + " must be 64 lowercase hexadecimal characters.");
            }
            foreach (char character in value)
            {
                if (!((character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f')))
                {
                    throw new ArgumentException("The " + label
                        + " must be 64 lowercase hexadecimal characters.");
                }
            }
            return value;
        }

        private static byte[] DecodeHex(string value)
        {
            if (value == null || value.Length % 2 != 0)
            {
                throw new InvalidDataException("The terminal state hex is invalid.");
            }
            var result = new byte[value.Length / 2];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = Convert.ToByte(
                    value.Substring(index * 2, 2), 16);
            }
            return result;
        }

        private static byte[] Canonical(JToken value)
        {
            return Encoding.UTF8.GetBytes(
                value.ToString(Formatting.None) + "\n");
        }

        private static string Digest(byte[] value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(value))
                    .Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static string Sha256File(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream))
                    .Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        /// <summary>
        /// The host adapter the bounded producer needs: the emulator host plus
        /// the driver-state source and the diagnostic audio seam.
        /// </summary>
        private sealed class RequestWindowHost
            : IS2RequestAwareRawV3CandidateHost,
              IOverrideResumeDiagnosticAudioHost
        {
            private readonly GpgxHost inner;
            private readonly GpgxS2CompleteAudioStateSource state;

            internal RequestWindowHost(GpgxHost value)
            {
                inner = value ?? throw new ArgumentNullException("value");
                state = new GpgxS2CompleteAudioStateSource(inner);
            }

            public int CompletedFrame { get { return inner.CompletedFrame; } }
            public bool IsLagged { get { return inner.IsLagged; } }
            public int LagCount { get { return inner.LagCount; } }
            public int DiagnosticAudioSampleRate { get { return 44100; } }
            public void ClearButtons() { inner.ClearButtons(); }
            public void SetButton(string name, bool pressed)
            { inner.SetButton(name, pressed); }
            public IDisposable RegisterExecuteCallback(uint address,
                Action callback)
            { return inner.RegisterExecuteCallback(address, callback); }
            public void Advance() { inner.Advance(); }
            public byte ReadMainRamByte(int offset)
            { return inner.ReadMainRamByte(offset); }
            public uint ReadCpuRegister(string name)
            { return inner.ReadCpuRegister(name); }
            public byte[] CaptureDriverState()
            { return state.CaptureDriverState(); }
            public IGpgxAudioTraceApi CreateRequestCandidateAudioTraceApi()
            { return inner.CreateAudioTraceApi(); }
            public void AdvanceDiagnosticAudio()
            { inner.AdvanceDiagnosticAudio(); }
            public short[] DrainDiagnosticAudio(out int stereoFrames)
            { return inner.DrainDiagnosticAudio(out stereoFrames); }
            public void Dispose() { inner.Dispose(); }
        }
    }
}
