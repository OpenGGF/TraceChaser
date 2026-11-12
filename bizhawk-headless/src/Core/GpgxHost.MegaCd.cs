using System;
using System.IO;
using System.Security.Cryptography;
using BizHawk.Emulation.Common;
using BizHawk.Emulation.Cores;
using BizHawk.Emulation.Cores.Consoles.Sega.gpgx;
using BizHawk.Emulation.DiscSystem;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>
    /// Mega CD / Sega CD boot for the Sonic CD recorder (lane K2,
    /// 2026-11-12). The disc (CUE/BIN) and BIOS are user-supplied and passed
    /// explicitly; nothing is discovered. The BIOS is served only for the
    /// exact firmware slot the core requests and only when its SHA-1 matches
    /// the caller's pin. The boot recipe is the one proven by the
    /// OpenGGF Sonic CD oracle and player-reference probes: default
    /// <see cref="GPGX.GPGXSyncSettings"/> unless the movie supplies its own,
    /// deterministic emulation, GameInfo system GEN.
    /// </summary>
    public sealed partial class GpgxHost : IScdWordRamReader
    {
        private MemoryDomain wordRam2M;

        public static GpgxHost OpenMegaCd(
            string cuePath,
            string biosPath,
            string firmwareId,
            string biosSha1,
            GPGX.GPGXSyncSettings syncSettings)
        {
            if (string.IsNullOrEmpty(cuePath) || !Path.IsPathRooted(cuePath))
                throw new ArgumentException("An absolute CUE path is required.", "cuePath");
            if (string.IsNullOrEmpty(biosPath) || !Path.IsPathRooted(biosPath))
                throw new ArgumentException("An absolute BIOS path is required.", "biosPath");
            if (syncSettings == null) throw new ArgumentNullException("syncSettings");
            byte[] bios = File.ReadAllBytes(biosPath);
            string actual = Sha1Hex(bios);
            if (!string.Equals(actual, biosSha1, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "BIOS SHA-1 is " + actual + "; expected " + biosSha1 + ".");
            Disc disc = Disc.LoadAutomagic(cuePath);
            if (disc == null) throw new InvalidDataException("Disc could not be loaded: " + cuePath);
            var game = new GameInfo { Name = "Sonic CD", System = VSystemID.Raw.GEN };
            var comm = new CoreComm(
                _ => { },
                (_, __) => { },
                new PinnedFirmwareProvider(firmwareId, bios),
                CoreComm.CorePreferencesFlags.None,
                null);
            var parameters = new CoreLoadParameters<GPGX.GPGXSettings, GPGX.GPGXSyncSettings>
            {
                Comm = comm,
                Game = game,
                Settings = new GPGX.GPGXSettings(),
                SyncSettings = syncSettings,
                DeterministicEmulationRequested = true
            };
            parameters.Discs.Add(new DiscAsset(disc, Path.GetFileName(cuePath)));
            var core = new GPGX(parameters);
            if (!core.IsMegaCD)
            {
                core.Dispose();
                throw new InvalidOperationException("GPGX did not boot the disc as Mega CD.");
            }
            var host = new GpgxHost(core);
            host.wordRam2M = core.ServiceProvider.GetService<IMemoryDomains>()["CD WORD RAM (2M)"];
            if (host.wordRam2M == null || host.wordRam2M.Size != 262144L)
            {
                host.Dispose();
                throw new InvalidOperationException(
                    "GPGX did not expose the exact 262144-byte CD WORD RAM (2M) domain.");
            }
            return host;
        }

        /// <summary>Main-CPU view of 2M Word RAM ($200000-$23FFFF); offset 0 is $200000.</summary>
        public byte ReadWordRam2MByte(int offset)
        {
            if (wordRam2M == null) throw new InvalidOperationException("Not a Mega CD host.");
            if (offset < 0 || offset >= wordRam2M.Size) throw new ArgumentOutOfRangeException("offset");
            return wordRam2M.PeekByte(offset);
        }

        private static string Sha1Hex(byte[] bytes)
        {
            using (SHA1 sha = SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private sealed class DiscAsset : IDiscAsset
        {
            internal DiscAsset(Disc disc, string name) { DiscData = disc; DiscName = name; }
            public Disc DiscData { get; set; }
            public DiscType DiscType { get { return DiscType.MegaCD; } }
            public string DiscName { get; set; }
        }

        /// <summary>Serves one pinned firmware image for one exact firmware id.</summary>
        private sealed class PinnedFirmwareProvider : ICoreFileProvider
        {
            private readonly string id;
            private readonly byte[] image;

            internal PinnedFirmwareProvider(string id, byte[] image)
            {
                this.id = id;
                this.image = image;
            }

            public byte[] GetFirmware(FirmwareID requested, string msg = null)
            {
                if (requested.ToString() != id)
                    throw new InvalidOperationException(
                        "Firmware " + requested + " requested; only " + id + " is pinned.");
                return (byte[])image.Clone();
            }

            public byte[] GetFirmwareOrThrow(FirmwareID requested, string msg = null)
            {
                return GetFirmware(requested, msg);
            }

            public (byte[] FW, GameInfo Game) GetFirmwareWithGameInfoOrThrow(
                FirmwareID requested, string msg = null)
            {
                return (GetFirmware(requested, msg), new GameInfo());
            }

            public string GetRetroSaveRAMDirectory(IGameInfo game)
            {
                throw new InvalidOperationException("Retro paths are unavailable.");
            }

            public string GetRetroSystemPath(IGameInfo game)
            {
                throw new InvalidOperationException("Retro paths are unavailable.");
            }

            public string GetUserPath(string sysID, bool temp)
            {
                throw new InvalidOperationException("User paths are unavailable.");
            }
        }
    }
}
