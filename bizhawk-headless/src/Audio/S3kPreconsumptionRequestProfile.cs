using System;
using System.IO;
using System.Security.Cryptography;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>
    /// Fixed Sonic 3&amp;K Sonic/Tails pre-consumption music-mailbox observer
    /// profile. It is a distinct diagnostic identity for the bounded prefix
    /// [0,5400) of the 466,334-row Sonic/Tails movie. It never accepts the
    /// Knuckles complete-run movie, hash, interval, manifest, or profile, and
    /// it confers no publication authority until a reviewed capability and a
    /// duplicate capture exist.
    ///
    /// The observed boundary is Play_Music (docs/skdisasm/sonic3k.asm:1493-1497):
    /// stopZ80 at M68K $1358, move.b d0,(Z80_RAM+zMusicNumber).l, startZ80 at
    /// $1374. The one-byte $1C0A snapshot is taken at $1374 before the
    /// bus-release instruction executes, so it is definitely read while the
    /// Z80 is still stopped. No request value is inferred from later state.
    /// </summary>
    internal static class S3kPreconsumptionRequestProfile
    {
        internal const string Schema =
            "openggf.s3k-preconsumption-request-raw.v1";

        internal const string RomSha1 =
            "cfbf98c36c776677290a872547ac47c53d2761d6";
        internal const string MovieBaseName = "s3k-complete-sonic-tails.bk2";
        internal const string MovieSha256 =
            "82eabfbc65e33c160ce209baa1ca3f967cb677fe22350bc100625d8c41a8e1bf";
        internal const string MovieHeaderHash =
            "C5B1C655C19F462ADE0AC4E17A844D10";
        internal const int MovieRowCount = 466334;
        internal const string ManifestSha256 =
            "75aeed7b3e0d0c4f1accee3f9beda426ad67c2ea60cb3ed100093e244c598dcc";

        internal const int FirstRow = 0;
        internal const int ExclusiveEnd = 5400;
        internal const int DriverStateStart = 0x1C00;
        internal const int DriverStateExclusiveEnd = 0x2000;

        internal const uint BeginPc = 0x1358;
        internal const uint EndPc = 0x1370;
        internal const string BeginOpcode = "33fc010000a11100";
        internal const string EndOpcode = "33fc000000a11100";
        internal const ushort BeginToken = 27;
        internal const ushort EndToken = 28;
        internal const byte SubmissionKind = 13;
        internal const byte ParentKind = 8;
        internal const ushort MailboxRangeId = 3;
        internal const int MailboxAddress = 0x1C0A;

        private const ushort UploadBeginToken = 7;
        private const ushort UploadCompletionToken = 8;
        private const byte ArmProofCompletion = 1;
        private const byte PrearmPermitted = 2;

        /// <summary>
        /// The non-target active kinds that Play_Music can be reached under.
        /// Kind 2 (SoundDriverLoad) is excluded because $1358 lies outside
        /// SndDrvInit ($12CE..$1346) and because the native arm rule forbids a
        /// non-PUSH_BEGIN hook expecting the arm kind. Kind 13 is excluded at
        /// $1358 because the submission never nests inside itself.
        /// </summary>
        private static readonly byte[] BeginAlternativeKinds =
            new byte[] { 0, 1, 3, 5, 6, 7, 9, 10, 11, 12 };
        private static readonly ushort[] BeginAlternativeTokens =
            new ushort[] { 29, 30, 31, 32, 33, 34, 35, 36, 37, 38 };

        /// <summary>
        /// The $1374 alternatives additionally retain kind 8 defensively: if a
        /// $1358 visit did not open a submission child, kind 8 is still the
        /// active service when the release instruction is reached.
        /// </summary>
        private static readonly byte[] EndAlternativeKinds =
            new byte[] { 0, 1, 3, 5, 6, 7, 9, 10, 11, 12, 8 };
        private static readonly ushort[] EndAlternativeTokens =
            new ushort[] { 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49 };

        internal static CompleteRunAudioObserver CreateObserver(
            string manifestPath, IGpgxAudioTraceApi api)
        {
            if (string.IsNullOrEmpty(manifestPath)
                || !Path.IsPathRooted(manifestPath))
                throw new ArgumentException(
                    "The S3K request service-manifest path must be absolute.",
                    "manifestPath");
            if (api == null) throw new ArgumentNullException("api");
            if (!File.Exists(manifestPath))
                throw new FileNotFoundException(
                    "The S3K request service manifest is absent.", manifestPath);
            string actual = Sha256(manifestPath);
            if (!string.Equals(actual, ManifestSha256, StringComparison.Ordinal))
                throw new InvalidDataException(
                    "The S3K request service-manifest SHA-256 was " + actual
                    + "; expected " + ManifestSha256 + ".");
            return GpgxAudioServiceManifest.LoadS3kRequest(
                manifestPath, new RequestPrepublicationApi(api));
        }

        /// <summary>
        /// Test-only seam exposing the fixed topology validator without the
        /// manifest SHA-256 gate. It confers no capture authority: the raw
        /// sink still demands the closed request authority and CreateObserver
        /// still pins the manifest file identity.
        /// </summary>
        internal static IGpgxAudioTraceApi WrapForTopologyTesting(
            IGpgxAudioTraceApi api)
        {
            return new RequestPrepublicationApi(api);
        }

        internal static Bk2Movie OpenMovie(string moviePath)
        {
            if (string.IsNullOrEmpty(moviePath) || !Path.IsPathRooted(moviePath))
                throw new ArgumentException(
                    "The S3K request movie path must be absolute.", "moviePath");
            if (!File.Exists(moviePath))
                throw new FileNotFoundException(
                    "The S3K request movie is absent.", moviePath);
            if (!string.Equals(Path.GetFileName(moviePath), MovieBaseName,
                StringComparison.Ordinal))
                throw new InvalidDataException(
                    "The S3K request movie basename is not " + MovieBaseName + ".");
            string actual = Sha256(moviePath);
            if (!string.Equals(actual, MovieSha256, StringComparison.Ordinal))
                throw new InvalidDataException(
                    "The S3K request movie SHA-256 was " + actual
                    + "; expected " + MovieSha256 + ".");
            Bk2Movie movie = Bk2Reader.Read(moviePath);
            if (movie.FrameCount != MovieRowCount
                || !string.Equals(movie.Sha1, MovieHeaderHash,
                    StringComparison.Ordinal))
                throw new InvalidDataException(
                    "The S3K request movie header identity is not exact.");
            return movie;
        }

        internal static void ValidateRom(string romPath)
        {
            if (string.IsNullOrEmpty(romPath) || !Path.IsPathRooted(romPath))
                throw new ArgumentException(
                    "The locked-on S3K ROM path must be absolute.", "romPath");
            if (!File.Exists(romPath))
                throw new FileNotFoundException(
                    "The locked-on S3K ROM is absent.", romPath);
            string actual;
            try
            {
                actual = RomIdentity.ValidateSonic3kLockOn(
                    File.ReadAllBytes(romPath)).ToLowerInvariant();
            }
            catch (Exception error)
            {
                throw new InvalidDataException(
                    "The locked-on S3K ROM identity is not exact.", error);
            }
            if (!string.Equals(actual, RomSha1, StringComparison.Ordinal))
                throw new InvalidDataException(
                    "The locked-on S3K ROM SHA-1 was " + actual
                    + "; expected " + RomSha1 + ".");
        }

        private static string Sha256(string path)
        {
            byte[] digest;
            using (SHA256 sha = SHA256.Create())
            using (FileStream input = File.OpenRead(path))
                digest = sha.ComputeHash(input);
            char[] value = new char[digest.Length * 2];
            const string alphabet = "0123456789abcdef";
            for (int index = 0; index < digest.Length; index++)
            {
                value[index * 2] = alphabet[digest[index] >> 4];
                value[index * 2 + 1] = alphabet[digest[index] & 15];
            }
            return new string(value);
        }

        private static ulong PackOpcode(string hex)
        {
            ulong value = 0;
            for (int index = 0; index < hex.Length / 2; index++)
                value |= (ulong)Convert.ToByte(hex.Substring(index * 2, 2), 16)
                    << (8 * index);
            return value;
        }

        private static InvalidDataException InvalidBoundary()
        {
            return new InvalidDataException(
                "The fixed S3K Play_Music mailbox boundary changed.");
        }

        private static InvalidDataException InvalidUploadChain()
        {
            return new InvalidDataException(
                "The reviewed S3K SndDrvInit proof chain changed.");
        }

        private sealed class RequestPrepublicationApi : IGpgxAudioTraceApi
        {
            private readonly IGpgxAudioTraceApi inner;

            internal RequestPrepublicationApi(IGpgxAudioTraceApi innerApi)
            {
                inner = innerApi;
            }

            public uint AbiVersion { get { return inner.AbiVersion; } }
            public uint EventSize { get { return inner.EventSize; } }
            public uint Capacity { get { return inner.Capacity; } }

            public int Configure(ref GpgxAudioObserverAdapter.Config config,
                byte[] mask, GpgxAudioObserverAdapter.ServiceKind[] kinds,
                GpgxAudioObserverAdapter.ServiceHook[] hooks,
                GpgxAudioObserverAdapter.SnapshotRange[] ranges)
            {
                if (config.AbiVersion != 1 || config.Flags != 0
                    || config.MaxContinuationFrames != 4)
                    throw new InvalidDataException(
                        "The reviewed S3K request manifest did not produce its legacy exact configuration.");
                ValidateMailboxRange(ranges);
                ValidateKinds(kinds);
                ValidateHooks(hooks);
                config.AbiVersion = 2;
                config.Flags = 1;
                return inner.Configure(ref config, mask, kinds, hooks, ranges);
            }

            private static void ValidateMailboxRange(
                GpgxAudioObserverAdapter.SnapshotRange[] ranges)
            {
                int seen = 0;
                foreach (GpgxAudioObserverAdapter.SnapshotRange range in ranges)
                {
                    if (range.RangeId != MailboxRangeId) continue;
                    if (range.Start != MailboxAddress || range.Length != 1)
                        throw InvalidBoundary();
                    seen++;
                }
                if (seen != 1) throw InvalidBoundary();
            }

            private static void ValidateKinds(
                GpgxAudioObserverAdapter.ServiceKind[] kinds)
            {
                bool submissionSeen = false;
                bool parentSeen = false;
                foreach (GpgxAudioObserverAdapter.ServiceKind kind in kinds)
                {
                    if (kind.KindId == SubmissionKind)
                    {
                        if (submissionSeen || kind.Flags != 0
                            || kind.CancellationRangeCount != 1
                            || kind.ContinuationFrameLimit != 0)
                            throw InvalidBoundary();
                        submissionSeen = true;
                    }
                    else if (kind.KindId == ParentKind)
                    {
                        if (parentSeen || (kind.Flags & 4) == 0)
                            throw InvalidBoundary();
                        parentSeen = true;
                    }
                }
                if (!submissionSeen || !parentSeen) throw InvalidBoundary();
            }

            private static void ValidateHooks(
                GpgxAudioObserverAdapter.ServiceHook[] hooks)
            {
                ulong beginOpcode = PackOpcode(BeginOpcode);
                ulong endOpcode = PackOpcode(EndOpcode);
                bool beginSeen = false, endSeen = false;
                bool uploadBeginSeen = false, uploadCompletionSeen = false;
                var beginAlternatives = new bool[BeginAlternativeTokens.Length];
                var endAlternatives = new bool[EndAlternativeTokens.Length];
                for (int index = 0; index < hooks.Length; index++)
                {
                    GpgxAudioObserverAdapter.ServiceHook hook = hooks[index];
                    if (hook.HookToken == UploadBeginToken)
                    {
                        if (uploadBeginSeen || hook.Action != 1 || hook.Cpu != 2
                            || hook.Pc != 0x12CE || hook.ServiceKindId != 2
                            || hook.ExpectedActiveKind != 0 || hook.Flags != 0)
                            throw InvalidUploadChain();
                        hook.Flags = PrearmPermitted;
                        uploadBeginSeen = true;
                    }
                    else if (hook.HookToken == UploadCompletionToken)
                    {
                        if (uploadCompletionSeen || hook.Action != 2
                            || hook.Cpu != 2 || hook.Pc != 0x1346
                            || hook.ServiceKindId != 0
                            || hook.ExpectedActiveKind != 2
                            || hook.Flags != ArmProofCompletion)
                            throw InvalidUploadChain();
                        hook.Flags = ArmProofCompletion | PrearmPermitted;
                        uploadCompletionSeen = true;
                    }
                    else if (hook.HookToken == BeginToken)
                    {
                        if (beginSeen || hook.Action != 1 || hook.Cpu != 2
                            || hook.Pc != BeginPc
                            || hook.ServiceKindId != SubmissionKind
                            || hook.ExpectedActiveKind != ParentKind
                            || hook.Flags != 0 || hook.RangeCount != 0
                            || hook.OpcodeLength != 8
                            || hook.Opcode != beginOpcode)
                            throw InvalidBoundary();
                        beginSeen = true;
                    }
                    else if (hook.HookToken == EndToken)
                    {
                        if (endSeen || hook.Action != 2 || hook.Cpu != 2
                            || hook.Pc != EndPc || hook.ServiceKindId != 0
                            || hook.ExpectedActiveKind != SubmissionKind
                            || hook.Flags != 0 || hook.RangeCount != 1
                            || hook.OpcodeLength != 8
                            || hook.Opcode != endOpcode)
                            throw InvalidBoundary();
                        endSeen = true;
                    }
                    else
                    {
                        MatchAlternative(hook, BeginAlternativeTokens,
                            BeginAlternativeKinds, beginAlternatives, BeginPc,
                            beginOpcode);
                        MatchAlternative(hook, EndAlternativeTokens,
                            EndAlternativeKinds, endAlternatives, EndPc,
                            endOpcode);
                        if (hook.Action == 7
                            && (hook.Pc != BeginPc && hook.Pc != EndPc))
                            throw InvalidBoundary();
                    }
                    hooks[index] = hook;
                }
                if (!uploadBeginSeen || !uploadCompletionSeen)
                    throw InvalidUploadChain();
                if (!beginSeen || !endSeen) throw InvalidBoundary();
                foreach (bool seen in beginAlternatives)
                    if (!seen) throw InvalidBoundary();
                foreach (bool seen in endAlternatives)
                    if (!seen) throw InvalidBoundary();
            }

            private static void MatchAlternative(
                GpgxAudioObserverAdapter.ServiceHook hook, ushort[] tokens,
                byte[] kinds, bool[] seen, uint pc, ulong opcode)
            {
                for (int index = 0; index < tokens.Length; index++)
                {
                    if (hook.HookToken != tokens[index]) continue;
                    if (seen[index] || hook.Action != 7 || hook.Cpu != 2
                        || hook.Pc != pc || hook.ServiceKindId != 0
                        || hook.ExpectedActiveKind != kinds[index]
                        || hook.Flags != 0 || hook.RangeCount != 0
                        || hook.OpcodeLength != 8 || hook.Opcode != opcode)
                        throw InvalidBoundary();
                    seen[index] = true;
                    return;
                }
            }

            public int BeginFrame() { return inner.BeginFrame(); }
            public int EndFrame() { return inner.EndFrame(); }
            public int EventCount(out uint count, out uint overflow)
            { return inner.EventCount(out count, out overflow); }
            public int Drain(GpgxAudioTraceEvent[] events, uint capacity,
                out uint count)
            { return inner.Drain(events, capacity, out count); }
            public int GetFirstFault(
                out GpgxAudioObserverAdapter.FirstFault fault)
            { return inner.GetFirstFault(out fault); }
            public int BeginPublicationEpoch()
            { return inner.BeginPublicationEpoch(); }
            public int AbortFrame() { return inner.AbortFrame(); }
            public int Disable() { return inner.Disable(); }
        }
    }
}
