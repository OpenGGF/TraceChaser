using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace OpenGGF.BizHawk.Headless.Tests
{
    /// <summary>
    /// Negative and topology coverage for the fixed Sonic 3&amp;K Sonic/Tails
    /// pre-consumption music-mailbox observer. None of these tests start Mono,
    /// EmuHawk, or a capture; they exercise the closed manifest, profile
    /// identity, and hook-selection arithmetic only.
    /// </summary>
    internal static class S3kPreconsumptionRequestProfileTests
    {
        public static void Register(ICollection<TestMain.TestCase> tests)
        {
            tests.Add(new TestMain.TestCase(
                "S3kPreconsumptionRequestProfile loads its fixed manifest topology",
                LoadsFixedManifestTopology));
            tests.Add(new TestMain.TestCase(
                "S3kPreconsumptionRequestProfile rejects a changed manifest identity",
                RejectsChangedManifestIdentity));
            tests.Add(new TestMain.TestCase(
                "S3kPreconsumptionRequestProfile rejects the Knuckles complete-run movie",
                RejectsKnucklesMovie));
            tests.Add(new TestMain.TestCase(
                "S3kPreconsumptionRequestProfile rejects a missing target begin hook",
                RejectsMissingBeginHook));
            tests.Add(new TestMain.TestCase(
                "S3kPreconsumptionRequestProfile rejects a changed target opcode",
                RejectsChangedTargetOpcode));
            tests.Add(new TestMain.TestCase(
                "S3kPreconsumptionRequestProfile rejects a missing active-kind alternative",
                RejectsMissingAlternative));
            tests.Add(new TestMain.TestCase(
                "S3kPreconsumptionRequestProfile rejects a wrong submission parent kind",
                RejectsWrongParentKind));
            tests.Add(new TestMain.TestCase(
                "S3kPreconsumptionRequestProfile rejects a widened mailbox range",
                RejectsWidenedMailboxRange));
            tests.Add(new TestMain.TestCase(
                "S3kPreconsumptionRequestProfile selects exactly one hook per active kind",
                SelectsExactlyOneHookPerActiveKind));
            tests.Add(new TestMain.TestCase(
                "S3kPreconsumptionRequestProfile keeps the Knuckles production manifest unchanged",
                KeepsKnucklesProductionManifestUnchanged));
            tests.Add(new TestMain.TestCase(
                "S3kPreconsumptionRequestProfile raw authority is unbound and distinct",
                RawAuthorityIsUnboundAndDistinct));
        }

        private const uint BeginPc = 0x1358;
        private const uint EndPc = 0x1374;

        private static void LoadsFixedManifestTopology()
        {
            var api = new RecordingApi();
            CompleteRunAudioObserver observer =
                S3kPreconsumptionRequestProfile.CreateObserver(
                    RequestManifestPath(), api);
            AssertEx.Equal(true, observer != null);
            AssertEx.Equal(2, (int)api.Config.AbiVersion);
            AssertEx.Equal(1, (int)api.Config.Flags);
            int begin = 0, end = 0, markers = 0;
            foreach (GpgxAudioObserverAdapter.ServiceHook hook in api.Hooks)
            {
                if (hook.HookToken == 27) { begin++; }
                else if (hook.HookToken == 28) { end++; }
                else if (hook.Action == 7) { markers++; }
            }
            AssertEx.Equal(1, begin);
            AssertEx.Equal(1, end);
            AssertEx.Equal(21, markers);
        }

        private static void RejectsChangedManifestIdentity()
        {
            string path = WriteTampered(manifest => { });
            try
            {
                ExpectInvalid(() =>
                    S3kPreconsumptionRequestProfile.CreateObserver(
                        path, new RecordingApi()),
                    "SHA-256");
            }
            finally { File.Delete(path); }
        }

        private static void RejectsKnucklesMovie()
        {
            string movie = KnucklesMoviePath();
            if (!File.Exists(movie)) return;
            ExpectInvalid(
                () => S3kPreconsumptionRequestProfile.OpenMovie(movie),
                "basename");
        }

        private static void RejectsMissingBeginHook()
        {
            ExpectTamperedRejection(hooks =>
            {
                for (int index = hooks.Count - 1; index >= 0; index--)
                    if ((int)hooks[index]["token"] == 27) hooks.RemoveAt(index);
            });
        }

        private static void RejectsChangedTargetOpcode()
        {
            ExpectTamperedRejection(hooks =>
            {
                foreach (JToken hook in hooks)
                    if ((int)hook["token"] == 28)
                        hook["opcode"] = "33fc000000a11102";
            });
        }

        private static void RejectsMissingAlternative()
        {
            ExpectTamperedRejection(hooks =>
            {
                for (int index = hooks.Count - 1; index >= 0; index--)
                    if ((int)hooks[index]["token"] == 44) hooks.RemoveAt(index);
            });
        }

        private static void RejectsWrongParentKind()
        {
            ExpectTamperedRejection(hooks =>
            {
                foreach (JToken hook in hooks)
                    if ((int)hook["token"] == 27) hook["expected_kind"] = 7;
            });
        }

        private static void RejectsWidenedMailboxRange()
        {
            JObject root = LoadRequestManifest();
            foreach (JToken range in (JArray)root["games"]["s3k"]["ranges"])
                if ((int)range["id"] == 3)
                    range["exclusive_end"] = (int)range["start"] + 2;
            ExpectInvalid(() => LoadTopology(root), "boundary");
        }

        /// <summary>
        /// Models the native selector rule at
        /// native/gpgx-audio-observer/0001-buffer-z80-audio-events.patch:1785-1826:
        /// for a watched PC it counts every hook at that PC and requires exactly
        /// one whose expected_active_kind equals the current active kind. Any
        /// other count is a hook-proof fault. Kind 2 is excluded because
        /// Play_Music at $1358 lies outside SndDrvInit ($12CE..$1346) and the
        /// native arm rule forbids a non-PUSH_BEGIN hook expecting the arm kind.
        /// </summary>
        private static void SelectsExactlyOneHookPerActiveKind()
        {
            JObject root = LoadRequestManifest();
            JArray hooks = (JArray)root["games"]["s3k"]["hooks"];
            var declared = new List<int> { 0 };
            foreach (JToken kind in (JArray)root["games"]["s3k"]["kinds"])
                declared.Add((int)kind["id"]);
            foreach (int active in declared)
            {
                if (active == 2 || active == 13) continue;
                AssertEx.Equal(1, Matches(hooks, BeginPc, active));
                AssertEx.Equal(1, Matches(hooks, EndPc, active));
            }
            AssertEx.Equal(1, Matches(hooks, EndPc, 13));
            AssertEx.Equal(0, Matches(hooks, BeginPc, 13));
            AssertEx.Equal(0, Matches(hooks, BeginPc, 2));
            AssertEx.Equal(0, Matches(hooks, EndPc, 2));
        }

        private static int Matches(JArray hooks, uint pc, int activeKind)
        {
            int matches = 0;
            foreach (JToken hook in hooks)
            {
                if ((uint)(int)hook["pc"] != pc) continue;
                if ((string)hook["cpu"] != "M68K") continue;
                if ((int)hook["expected_kind"] == activeKind) matches++;
            }
            return matches;
        }

        private static void KeepsKnucklesProductionManifestUnchanged()
        {
            JObject production = JObject.Parse(
                File.ReadAllText(ProductionManifestPath()));
            foreach (JToken hook in (JArray)production["games"]["s3k"]["hooks"])
            {
                uint pc = (uint)(int)hook["pc"];
                AssertEx.Equal(true, pc != BeginPc && pc != EndPc);
            }
            foreach (JToken kind in (JArray)production["games"]["s3k"]["kinds"])
                AssertEx.Equal(true, (int)kind["id"] != 13);
        }

        private static void RawAuthorityIsUnboundAndDistinct()
        {
            S3kRawAudioAuthority authority =
                S3kPreconsumptionRequestRawAuthority.Instance;
            AssertEx.Equal(false, authority.IsProductionBound);
            AssertEx.Equal(true, authority.IncludeSubmissions);
            AssertEx.Equal("openggf.s3k-preconsumption-request-raw.v1",
                authority.Schema);
            AssertEx.Equal(0, authority.FirstRow);
            AssertEx.Equal(5400, authority.ExclusiveEnd);
            AssertEx.Equal(3, (int)authority.MailboxRangeId);
            AssertEx.Equal(true, !string.Equals(authority.Bk2Sha256,
                S3kAudioObserverProfile.MovieSha256, StringComparison.Ordinal));
            AssertEx.Equal(true, !string.Equals(authority.Schema,
                S3kCompleteAudioRawSink.Schema, StringComparison.Ordinal));
        }

        private static void ExpectTamperedRejection(Action<JArray> mutate)
        {
            JObject root = LoadRequestManifest();
            mutate((JArray)root["games"]["s3k"]["hooks"]);
            ExpectInvalid(() => LoadTopology(root), "boundary");
        }

        private static void LoadTopology(JObject root)
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, root.ToString());
                GpgxAudioServiceManifest.LoadS3kRequest(path,
                    S3kPreconsumptionRequestProfile.WrapForTopologyTesting(
                        new RecordingApi()));
            }
            finally { File.Delete(path); }
        }

        private static void ExpectInvalid(Action action, string fragment)
        {
            try { action(); }
            catch (InvalidDataException error)
            {
                if (error.Message.IndexOf(fragment,
                    StringComparison.OrdinalIgnoreCase) < 0)
                    throw new Exception(
                        "Unexpected rejection message: " + error.Message);
                return;
            }
            throw new Exception("The tampered input was accepted.");
        }

        private static JObject LoadRequestManifest()
        {
            return JObject.Parse(File.ReadAllText(RequestManifestPath()));
        }

        private static string WriteTampered(Action<JObject> mutate)
        {
            JObject root = LoadRequestManifest();
            mutate(root);
            root["games"]["s3k"]["watch_mask_bytes"] = 8192;
            string path = Path.GetTempFileName();
            File.WriteAllText(path, root.ToString() + "\n\n");
            return path;
        }

        private static string FixturePath(string name)
        {
            string binary = Path.GetDirectoryName(
                typeof(S3kPreconsumptionRequestProfileTests).Assembly.Location);
            return Path.GetFullPath(Path.Combine(binary, "..", "..",
                "fixtures/" + name));
        }

        private static string RequestManifestPath()
        {
            return FixturePath("gpgx-audio-service-manifest-s3k-request-v1.json");
        }

        private static string ProductionManifestPath()
        {
            return FixturePath("gpgx-audio-service-manifests-v1.json");
        }

        private static string KnucklesMoviePath()
        {
            string binary = Path.GetDirectoryName(
                typeof(S3kPreconsumptionRequestProfileTests).Assembly.Location);
            return Path.GetFullPath(Path.Combine(binary, "..", "..", "..", "..",
                "..", "src/test/resources/traces/s3k/_movies",
                "s3k-knuckles-complete-superemeralds.bk2"));
        }

        private sealed class RecordingApi : IGpgxAudioTraceApi
        {
            internal GpgxAudioObserverAdapter.Config Config;
            internal GpgxAudioObserverAdapter.ServiceHook[] Hooks =
                new GpgxAudioObserverAdapter.ServiceHook[0];
            public uint AbiVersion { get { return 1; } }
            public uint EventSize { get { return 32; } }
            public uint Capacity { get { return 65536; } }
            public int Configure(ref GpgxAudioObserverAdapter.Config config,
                byte[] mask, GpgxAudioObserverAdapter.ServiceKind[] kinds,
                GpgxAudioObserverAdapter.ServiceHook[] hooks,
                GpgxAudioObserverAdapter.SnapshotRange[] ranges)
            { Config = config; Hooks = hooks; return 0; }
            public int BeginFrame() { return 0; }
            public int EndFrame() { return 0; }
            public int EventCount(out uint count, out uint overflow)
            { count = 0; overflow = 0; return 0; }
            public int Drain(GpgxAudioTraceEvent[] events, uint capacity,
                out uint count) { count = 0; return 0; }
            public int GetFirstFault(
                out GpgxAudioObserverAdapter.FirstFault fault)
            { fault = default(GpgxAudioObserverAdapter.FirstFault); return 0; }
            public int BeginPublicationEpoch() { return 0; }
            public int AbortFrame() { return 0; }
            public int Disable() { return 0; }
        }
    }
}
