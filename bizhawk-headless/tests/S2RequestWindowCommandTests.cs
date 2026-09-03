using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BizHawk.Headless.Gpgx;

namespace OpenGGF.BizHawk.Headless.Tests
{
    /// <summary>
    /// Contract for the bounded S2 request-window command: every input is an
    /// explicit argument, both modes reject the other mode's arguments, and the
    /// capability the extractor reads is derived from the raw stream it will
    /// read rather than from a pinned window or a pinned recording.
    /// </summary>
    internal static class S2RequestWindowCommandTests
    {
        public static void Register(ICollection<TestMain.TestCase> tests)
        {
            tests.Add(new TestMain.TestCase(
                "S2RequestWindowCommandTests parse an explicit capture window",
                ParsesAnExplicitCaptureWindow));
            tests.Add(new TestMain.TestCase(
                "S2RequestWindowCommandTests reject cross-mode and malformed arguments",
                RejectsCrossModeAndMalformedArguments));
            tests.Add(new TestMain.TestCase(
                "S2RequestWindowCommandTests derive the capability from the raw stream",
                DerivesTheCapabilityFromTheRawStream));
            tests.Add(new TestMain.TestCase(
                "S2RequestWindowCommandTests refuse an unrequested command",
                RefusesAnUnrequestedCommand));
        }

        private static void ParsesAnExplicitCaptureWindow()
        {
            using (var scratch = new Scratch())
            {
                string rom = scratch.File("rom.gen");
                string movie = scratch.File("movie.bk2");
                string manifest = scratch.File("manifests.json");
                string candidate = scratch.File("candidate.json");
                RequestWindowCommandOptions options =
                    RequestWindowCommandOptions.Parse(new[]
                    {
                        "--request-window-mode", "capture",
                        "--rom", rom,
                        "--movie", movie,
                        "--movie-sha256", new string('a', 64),
                        "--service-manifest", manifest,
                        "--candidate-manifest", candidate,
                        "--bizhawk-home", scratch.Root,
                        "--first-row", "13650",
                        "--exclusive-end", "14400",
                        "--output", Path.Combine(scratch.Root, "raw.jsonl")
                    });

                AssertEx.Equal(RequestWindowCommandOptions.CaptureMode,
                    options.Mode);
                AssertEx.Equal(13650, options.FirstRow);
                AssertEx.Equal(14400, options.ExclusiveEnd);
                AssertEx.Equal(movie, options.MoviePath);
                AssertEx.Equal(new string('a', 64), options.MovieSha256);
                AssertEx.Equal(true, RequestWindowCommandOptions.IsRequested(
                    new[] { "--request-window-mode", "capture" }));
            }
        }

        private static void RejectsCrossModeAndMalformedArguments()
        {
            using (var scratch = new Scratch())
            {
                string manifest = scratch.File("manifests.json");
                string raw = scratch.File("raw.jsonl");
                string template = scratch.File("template.json");
                string[] extract =
                {
                    "--request-window-mode", "extract",
                    "--raw", raw,
                    "--service-manifest", manifest,
                    "--capability-template", template,
                    "--first-row", "2700",
                    "--exclusive-end", "3450",
                    "--output-directory", scratch.Root
                };
                RequestWindowCommandOptions options =
                    RequestWindowCommandOptions.Parse(extract);
                AssertEx.Equal(RequestWindowCommandOptions.ExtractMode,
                    options.Mode);

                var withRom = new List<string>(extract);
                withRom.Add("--rom");
                withRom.Add(manifest);
                AssertEx.Throws<ArgumentException>(
                    () => RequestWindowCommandOptions.Parse(withRom.ToArray()),
                    "not supported in this request-window mode");

                AssertEx.Throws<ArgumentException>(
                    () => RequestWindowCommandOptions.Parse(new[]
                    {
                        "--request-window-mode", "extract",
                        "--raw", raw,
                        "--service-manifest", manifest,
                        "--capability-template", template,
                        "--first-row", "3450",
                        "--exclusive-end", "3450",
                        "--output-directory", scratch.Root
                    }),
                    "not a valid interval");

                AssertEx.Throws<ArgumentException>(
                    () => RequestWindowCommandOptions.Parse(new[]
                    {
                        "--request-window-mode", "capture",
                        "--rom", raw,
                        "--movie", raw,
                        "--movie-sha256", "not-a-digest",
                        "--service-manifest", manifest,
                        "--candidate-manifest", template,
                        "--bizhawk-home", scratch.Root,
                        "--first-row", "0",
                        "--exclusive-end", "1",
                        "--output", Path.Combine(scratch.Root, "absent.jsonl")
                    }),
                    "hexadecimal");

                AssertEx.Throws<ArgumentException>(
                    () => RequestWindowCommandOptions.Parse(new[]
                    {
                        "--request-window-mode", "publish",
                        "--service-manifest", manifest,
                        "--first-row", "0", "--exclusive-end", "1"
                    }),
                    "must be exactly");
            }
        }

        private static void DerivesTheCapabilityFromTheRawStream()
        {
            using (var scratch = new Scratch())
            {
                string template = scratch.File("template.json",
                    TemplateJson());
                byte[] raw = Encoding.UTF8.GetBytes(RawStream());

                JObject capability = S2RequestWindowProducer.DeriveCapability(
                    raw, template, 4, 6);

                // The recording and the interval come from the stream and the
                // caller, never from a constant in the harness.
                AssertEx.Equal(new string('b', 64),
                    (string)capability["bk2_sha256"]);
                AssertEx.Equal(4, (int)capability["first_row"]);
                AssertEx.Equal(6, (int)capability["exclusive_end"]);
                AssertEx.Equal(4, (int)capability["window_first_row"]);
                AssertEx.Equal(6, (int)capability["window_exclusive_end"]);
                AssertEx.Equal(1L, (long)capability["marker_event_count"]);
                AssertEx.Equal(1L, (long)capability["base_event_count"]);
                AssertEx.Equal(2L, (long)capability["all_event_count"]);
                AssertEx.Equal(1L, (long)capability["request_count"]);
                AssertEx.Equal(1, (int)capability["max_request_occupancy"]);
                AssertEx.Equal(0, (int)capability["override_resume_count"]);
                AssertEx.Equal(false, (bool)capability["production_bound"]);

                AssertEx.Throws<InvalidDataException>(
                    () => S2RequestWindowProducer.DeriveCapability(
                        Encoding.UTF8.GetBytes("{}\n"), template, 4, 6),
                    "too short");
            }
        }

        private static void RefusesAnUnrequestedCommand()
        {
            AssertEx.Equal(false,
                RequestWindowCommandOptions.IsRequested(new[] { "--rom", "x" }));
            using (var stdout = new StringWriter())
            using (var stderr = new StringWriter())
            {
                AssertEx.Equal(1, Program.RunRequestWindow(
                    new[] { "--request-window-mode", "capture" }, stdout, stderr));
            }
        }

        private static string TemplateJson()
        {
            return new JObject
            {
                ["schema"] = "openggf.s2-request-aware-raw-v3-capability.v1",
                ["production_bound"] = false,
                ["producer"] = "s2-complete-audio-request-candidate",
                ["rom_sha1"] = new string('c', 40),
                ["bk2_sha256"] = new string('d', 64),
                ["service_manifest_sha256"] = new string('e', 64),
                ["candidate_manifest_sha256"] = new string('f', 64),
                ["first_row"] = 0,
                ["exclusive_end"] = 1
            }.ToString(Formatting.None);
        }

        /// <summary>
        /// A minimal well-formed raw-v3 stream: two rows, one carrying the
        /// request marker event beside an ordinary bus event and one transfer.
        /// </summary>
        private static string RawStream()
        {
            string state = new string('0', 0x4000);
            var lines = new List<string>();
            lines.Add(new JObject
            {
                ["type"] = "metadata",
                ["schema"] = "openggf.s2-complete-run-audio-raw.v3",
                ["rom_sha1"] = new string('c', 40),
                ["bk2_sha256"] = new string('b', 64),
                ["service_manifest_sha256"] = new string('e', 64),
                ["first_row"] = 4,
                ["exclusive_end"] = 6,
                ["state_start"] = 0,
                ["state_exclusive_end"] = 0x2000,
                ["production_bound"] = false,
                ["request_manifest_schema"] =
                    "openggf.s2-preconsumption-request-manifest.v1"
            }.ToString(Formatting.None));
            lines.Add(new JObject
            {
                ["type"] = "baseline", ["state_hex"] = state,
                ["ym_port0_latch"] = 0, ["ym_port1_latch"] = 0,
                ["native_arm_epoch"] = 1, ["native_armed"] = true,
                ["active_services"] = new JArray(),
                ["pending_descendants"] = new JArray(), ["row"] = 4
            }.ToString(Formatting.None));
            for (int row = 4; row < 6; row++)
            {
                var events = new JArray();
                var transfers = new JArray();
                if (row == 5)
                {
                    events.Add(Event(0, 3, 1, 0x100));
                    events.Add(Event(1, 10, 24, 0x10d6));
                    transfers.Add(new JObject
                    {
                        ["row"] = row, ["order"] = 0,
                        ["global_transfer_ordinal"] = 0, ["request"] = 0x81,
                        ["slot"] = 0, ["pc"] = 0x10d6, ["a7"] = "4660",
                        ["native_ordinal"] = 0, ["source_cpu"] = 2,
                        ["service_token"] = 0, ["service_kind"] = 0,
                        ["depth"] = 0,
                        ["active_service_owner"] = new JObject
                        { ["token"] = 0, ["kind"] = 0, ["depth"] = 0 }
                    });
                }
                lines.Add(new JObject
                {
                    ["type"] = "frame", ["row"] = row, ["lag"] = false,
                    ["state_hex"] = state, ["events"] = events,
                    ["override_resume"] = JValue.CreateNull(),
                    ["pcm"] = JValue.CreateNull(),
                    ["request_transfers"] = transfers
                }.ToString(Formatting.None));
            }
            lines.Add(new JObject
            {
                ["type"] = "cutoff", ["state_hex"] = state,
                ["ym_port0_latch"] = 0, ["ym_port1_latch"] = 0,
                ["native_arm_epoch"] = 1, ["native_armed"] = true,
                ["active_services"] = new JArray(),
                ["pending_descendants"] = new JArray(), ["exclusive_end"] = 6
            }.ToString(Formatting.None));
            return string.Join("\n", lines) + "\n";
        }

        private static JObject Event(int ordinal, int kind, int subject, int pc)
        {
            return new JObject
            {
                ["ordinal"] = ordinal, ["service_token"] = 0,
                ["parent_token"] = 0, ["pc"] = pc, ["subject"] = subject,
                ["offset"] = 0, ["kind"] = kind, ["service_kind"] = 0,
                ["depth"] = 0, ["source_cpu"] = kind == 10 ? 2 : 1,
                ["payload_length"] = kind == 10 ? 4 : 0,
                ["value"] = kind == 10 ? 3 : 1, ["flags"] = 0,
                ["reserved"] = 0, ["payload"] = "0"
            };
        }

        private sealed class Scratch : IDisposable
        {
            internal Scratch()
            {
                Root = Path.Combine(Path.GetTempPath(),
                    "s2-request-window-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Root);
            }

            internal string Root { get; private set; }

            internal string File(string name)
            {
                return File(name, "{}");
            }

            internal string File(string name, string content)
            {
                string path = Path.Combine(Root, name);
                System.IO.File.WriteAllText(path, content);
                return path;
            }

            public void Dispose()
            {
                try { Directory.Delete(Root, true); }
                catch (IOException) { }
            }
        }
    }
}
