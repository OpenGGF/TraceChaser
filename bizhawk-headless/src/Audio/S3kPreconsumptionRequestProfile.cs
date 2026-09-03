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
            "a2986032425af20fce19abd9e4bb0a1deabb142707510fe1d1830995adaaaf49";

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
        internal const ushort MailboxRangeId = 3;
        internal const int MailboxAddress = 0x1C0A;

        private const ushort UploadBeginToken = 7;
        private const ushort UploadCompletionToken = 8;
        private const byte ArmProofCompletion = 1;
        private const byte PrearmPermitted = 2;

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
                ValidateHooks(hooks);
                config.AbiVersion = 5;
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

            /// <summary>
            /// The boundary is exactly two parent-independent observations and
            /// nothing else. Neither declares a service kind or an expected
            /// active kind, so neither can claim a parent or open a lifecycle,
            /// and each must be the only hook at its instruction.
            /// </summary>
            private static void ValidateHooks(
                GpgxAudioObserverAdapter.ServiceHook[] hooks)
            {
                ulong beginOpcode = PackOpcode(BeginOpcode);
                ulong endOpcode = PackOpcode(EndOpcode);
                bool beginSeen = false, endSeen = false;
                bool uploadBeginSeen = false, uploadCompletionSeen = false;
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
                        RequireObservation(hook, BeginPc, beginOpcode, 0);
                        if (beginSeen) throw InvalidBoundary();
                        beginSeen = true;
                    }
                    else if (hook.HookToken == EndToken)
                    {
                        RequireObservation(hook, EndPc, endOpcode, 1);
                        if (endSeen) throw InvalidBoundary();
                        endSeen = true;
                    }
                    else if (hook.Action == 13 || hook.Pc == BeginPc
                        || hook.Pc == EndPc)
                    {
                        // nothing else may observe or share these instructions
                        throw InvalidBoundary();
                    }
                    hooks[index] = hook;
                }
                if (!uploadBeginSeen || !uploadCompletionSeen)
                    throw InvalidUploadChain();
                if (!beginSeen || !endSeen) throw InvalidBoundary();
            }

            private static void RequireObservation(
                GpgxAudioObserverAdapter.ServiceHook hook, uint pc,
                ulong opcode, int rangeCount)
            {
                if (hook.Action != 13 || hook.Cpu != 2 || hook.Pc != pc
                    || hook.ServiceKindId != 0 || hook.ExpectedActiveKind != 0
                    || hook.Flags != 0 || hook.RangeCount != rangeCount
                    || hook.OpcodeLength != 8 || hook.Opcode != opcode)
                    throw InvalidBoundary();
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
