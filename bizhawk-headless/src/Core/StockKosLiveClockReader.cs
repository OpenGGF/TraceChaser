// Read-only stock GPGX CPU-clock diagnostic; no recorder or timing authority.
// Origin: S3K ending6+46/fdf native-clock prerequisite, 2026-10-17; preserved prototype
// SHA256 f4f2fbc81b2c7e0680b1596fab14f2795308ade5ca005a2926710f0da2c6ee5b.
// Inputs: pinned stock ELF, explicit BizHawk 2.11 DLL directory, reviewed harness SHA256,
// and an owning GpgxHost opened between Guard.BeforeOpen and BindAfterOpen on one thread.
// Copies three raw scalars only at caller-reviewed synchronous execute PCs. No guest calls,
// writes, SaveState, phase-origin inference, IRQ decoding or readiness authority.
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using BizHawk.BizInvoke;
using BizHawk.Common;

namespace OpenGGF.BizHawk.Headless
{
    public sealed class StockKosLiveClockReader : IDisposable
    {
        const long Cpu = 0x36F025767E0L;
        const int CpuSize = 10728;
        const long WritableStart = 0x36F0034D000L, WritableEnd = 0x36F0257EF00L;
        const string ElfHash = "b4cc6dabc069a6f1b87790212d80f665d216e603aa4990955cc816d5bf98d218";
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        // Exact stock PT_LOAD, PF_W clear, file-backed bytes. No exclusions or relocation allowance.
        static readonly long[,] ReadOnlyLoads = {
            { 0x36F00000000L, 0, 0x2D8 },
            { 0x36F00001000L, 0x1000, 0x1C5442 },
            { 0x36F001C7000L, 0x1C7000, 0x186000 }
        };

        public sealed class RawClock
        {
            public readonly int Cycles, RefreshDeadline;
            public readonly uint EncodedIntLevel;
            public readonly uint ExecutePc;
            internal RawClock(int cycles, int refresh, uint irq, uint pc)
            { Cycles = cycles; RefreshDeadline = refresh; EncodedIntLevel = irq; ExecutePc = pc; }
        }

        public sealed class Guard : IDisposable
        {
            internal readonly byte[] Elf;
            internal readonly int ThreadId;
            readonly string[] paths, hashes;
            readonly List<FileStream> files = new List<FileStream>();
            readonly object lifetimeGate = new object();
            internal bool Closed, Bound;

            Guard(string dllDirectory, string elfPath, string reviewedHarnessHash)
            {
                ThreadId = Thread.CurrentThread.ManagedThreadId;
                string dll = Path.GetFullPath(dllDirectory);
                paths = new[] {
                    Path.Combine(dll, "gpgx.wbx.zst"),
                    Path.Combine(dll, "BizHawk.Emulation.Cores.dll"),
                    Path.Combine(dll, "BizHawk.Emulation.Common.dll"),
                    Path.Combine(dll, "BizHawk.BizInvoke.dll"),
                    Path.Combine(dll, "libwaterboxhost.so"),
                    typeof(GpgxHost).Assembly.Location, Path.GetFullPath(elfPath),
                    Path.Combine(dll, "BizHawk.Common.dll")
                };
                hashes = new[] {
                    "c4231296ec5ba59b431df22b68e234ae7bfbbfc87b6e72fa471234ac1b220d12",
                    "0144e6e236be68ce126eb771dcb5a9ae7c153a083fa0333f345ac37b4a60acf7",
                    "f20cd009f6f5b0a95bd47b66c48dc8de85afcd7ae0cc6aab3486baf55f501fb4",
                    "8d05389bf0e02be1244bdc7a2adcd93b4cff95acf199fc927987ca699760a1b7",
                    "d2367818aafb4e520ad5ab005b5762c61506b0c819c4d79687235acfb0fc0c78",
                    reviewedHarnessHash, ElfHash,
                    "438a49d6a45d9fcac17016240ae205d1af7a4632865f6f70468b684b82323f33"
                };
                try {
                    if (IntPtr.Size != 8 || !BitConverter.IsLittleEndian ||
                        Environment.OSVersion.Platform != PlatformID.Unix || !File.Exists("/proc/self/maps"))
                        throw Refuse("only the pinned Linux x64 little-endian runtime is described");
                    foreach (string hash in hashes) RequireHashShape(hash);
                    foreach (string path in paths)
                        files.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
                    Recheck();
                    RequireAssembly("BizHawk.Emulation.Cores", paths[1]);
                    RequireAssembly("BizHawk.Emulation.Common", paths[2]);
                    RequireAssembly("BizHawk.BizInvoke", paths[3]);
                    RequireAssembly("BizHawk.Common", paths[7]);
                    Elf = File.ReadAllBytes(paths[6]);
                    if (HashBytes(Elf) != ElfHash) throw Refuse("ELF bytes changed while reading");
                    // Full ELF hash authenticates these exact machine operands, not source layout alone.
                    RequireBytes(Elf, 0xEF7DE, "49bce06757026f0300004139bc240c280000");
                    RequireBytes(Elf, 0xEFC93, "418b84240c280000413b842410280000");
                    RequireBytes(Elf, 0x33F1E, "48bbe06757026f030000448babb42800008b83b8280000");
                } catch { Dispose(); throw; }
            }

            public static Guard BeforeOpen(string dllDirectory, string stockElfPath, string reviewedHarnessSha256)
            { return new Guard(dllDirectory, stockElfPath, reviewedHarnessSha256); }

            internal void Recheck()
            {
                lock (lifetimeGate) {
                if (Closed || Thread.CurrentThread.ManagedThreadId != ThreadId)
                    throw Refuse("guard lifetime/thread mismatch");
                for (int i = 0; i < paths.Length; i++) {
                    files[i].Position = 0;
                    using (var sha = SHA256.Create())
                        if (Hex(sha.ComputeHash(files[i])) != hashes[i]) throw Refuse("open artifact changed: " + paths[i]);
                    using (var f = File.OpenRead(paths[i]))
                    using (var sha = SHA256.Create())
                        if (Hex(sha.ComputeHash(f)) != hashes[i]) throw Refuse("path artifact changed: " + paths[i]);
                }
                }
            }
            internal void VerifyHeldNativeLibrary(StockKosLiveClockReader owner)
            {
                lock (lifetimeGate) {
                    if (Closed || Thread.CurrentThread.ManagedThreadId != ThreadId || !ReferenceEquals(owner.guard, this))
                        throw Refuse("held native file ownership/lifetime differs");
                    owner.VerifyNativeLibraryFile(files[4]); // Sole existing native-library stream; no caller FD input.
                }
            }
            public void Dispose()
            {
                lock (lifetimeGate) {
                    if (Thread.CurrentThread.ManagedThreadId != ThreadId) throw Refuse("guard dispose on owning thread only");
                    if (!Closed) { Closed = true; foreach (var f in files) f.Dispose(); }
                }
            }
        }

        readonly Guard guard;
        readonly GpgxHost host;
        readonly object core, elf;
        readonly IImportResolver rawResolver;
        readonly IMonitor monitor;
        readonly FieldInfo hostDisposed, hostCore, coreElf, enterCount;
        readonly List<IDisposable> registrations = new List<IDisposable>();
        bool closed, failed, finalVerified;
        public bool FinalIdentityVerified { get { return finalVerified && !failed; } }

        StockKosLiveClockReader(Guard before, GpgxHost owner)
        {
            guard = before; host = owner;
            if (guard == null || host == null || guard.Bound) throw Refuse("one guard and owning host required");
            guard.Recheck();
            hostCore = Field(typeof(GpgxHost), "core");
            hostDisposed = Field(typeof(GpgxHost), "disposed");
            if (!hostCore.IsInitOnly || hostDisposed.FieldType != typeof(bool)) throw Refuse("host shape changed");
            core = hostCore.GetValue(host);
            if (core == null || core.GetType().FullName != "BizHawk.Emulation.Cores.Consoles.Sega.gpgx.GPGX")
                throw Refuse("not the pinned GPGX owner");
            coreElf = Field(core.GetType(), "_elf"); elf = coreElf.GetValue(core);
            if (elf == null || elf.GetType().FullName != "BizHawk.Emulation.Cores.Waterbox.WaterboxHost")
                throw Refuse("not the pinned Waterbox owner");
            rawResolver = elf as IImportResolver; monitor = elf as IMonitor;
            enterCount = Field(elf.GetType(), "_enterCount");
            if (!coreElf.IsInitOnly || rawResolver == null || monitor == null || enterCount.FieldType != typeof(int))
                throw Refuse("raw resolver/monitor contract absent");
            CheckOwner();
            if ((int)enterCount.GetValue(elf) != 0) throw Refuse("bind only after Open, outside guest execution");
            VerifyNativeLibrary(); // BEFORE monitor activation, raw guest resolver or GPGX Marshal.Copy.
            monitor.Enter();
            try { VerifyLiveIdentity(); }
            finally { monitor.Exit(); }
            guard.Bound = true;
        }

        // Call immediately after the existing GpgxHost.Open returns, before any Advance.
        public static StockKosLiveClockReader BindAfterOpen(Guard before, GpgxHost owner)
        { return new StockKosLiveClockReader(before, owner); }

        // Callback wrapper proves read context; no public arbitrary-pointer/raw-read entry point exists.
        // The caller must supply source-authenticated instruction PCs; this is not a context runner.
        public void ObserveAt(uint pc, Action<RawClock> observer)
        {
            CheckOwner();
            if ((int)enterCount.GetValue(elf) != 0 || observer == null || finalVerified || (pc & 1) != 0 || pc > 0xFFFFFF)
                throw Refuse("invalid observer/PC or observations already finalized");
            registrations.Add(host.RegisterExecuteCallback(pc, () => {
                try {
                    CheckOwner();
                    if ((int)enterCount.GetValue(elf) <= 0) throw Refuse("callback outside active owning host");
                    RawClock raw;
                    monitor.Enter();
                    try {
                        CheckAnchors();
                        raw = new RawClock(ReadCpuInt(0x280C), ReadCpuInt(0x2810),
                            unchecked((uint)ReadCpuInt(0x28B8)), pc);
                    } finally { monitor.Exit(); }
                    observer(raw); // Copies only; any failure poisons final acceptance.
                } catch { failed = true; throw; }
            }));
        }

        // Required after the final observation and before treating any raw rows as accepted.
        // Stops only this adapter's callbacks, then repeats path and live immutable identity.
        public void VerifyAfterObservations()
        {
            CheckOwner();
            if ((int)enterCount.GetValue(elf) != 0) throw Refuse("finalize only outside guest execution");
            StopCallbacks();
            try {
                guard.Recheck();
                VerifyNativeLibrary();
                monitor.Enter();
                try { VerifyLiveIdentity(); }
                finally { monitor.Exit(); }
                VerifyNativeLibrary();
                finalVerified = true;
            } catch { failed = true; throw; }
        }

        void CheckOwner()
        {
            if (closed || failed || guard.Closed || Thread.CurrentThread.ManagedThreadId != guard.ThreadId)
                throw Refuse("reader lifetime/thread/failure mismatch");
            if ((bool)hostDisposed.GetValue(host) || !ReferenceEquals(hostCore.GetValue(host), core) ||
                !ReferenceEquals(coreElf.GetValue(core), elf)) throw Refuse("owning host changed/disposed");
        }
        void CheckAnchors()
        {
            // These are RAW guest addresses from WaterboxHost's IImportResolver implementation.
            if (rawResolver.GetProcAddrOrThrow("gpgx_advance").ToInt64() != 0x36F001B9CF0L ||
                rawResolver.GetProcAddrOrThrow("gpgx_getregs").ToInt64() != 0x36F001BE7C0L)
                throw Refuse("raw owning-host anchors differ; never use adjusted delegates");
        }
        void VerifyLiveIdentity()
        {
            CheckOwner(); CheckAnchors();
            byte[] chunk = new byte[65536];
            for (int r = 0; r < ReadOnlyLoads.GetLength(0); r++) {
                long address = ReadOnlyLoads[r, 0], offset = ReadOnlyLoads[r, 1], length = ReadOnlyLoads[r, 2];
                for (long pos = 0; pos < length; pos += chunk.Length) {
                    int n = (int)Math.Min(chunk.Length, length - pos);
                    Marshal.Copy(new IntPtr(checked(address + pos)), chunk, 0, n);
                    for (int i = 0; i < n; i++)
                        if (chunk[i] != guard.Elf[checked((int)(offset + pos + i))])
                            throw Refuse("immutable live PT_LOAD differs; no patch exclusions");
                }
            }
        }
        int ReadCpuInt(int offset)
        {
            long address = checked(Cpu + offset);
            if (offset < 0 || offset > CpuSize - 4 || address < WritableStart || address + 4 > WritableEnd)
                throw Refuse("CPU field outside authenticated object/segment");
            return Marshal.ReadInt32(new IntPtr(address));
        }
        // Existing static NativeImpl is inspected only through an already-constructed owning _elf.
        // No function pointer is called. This authenticates native RX, not native RW/GOT or consumed GPGX ELF.
        void VerifyNativeLibrary()
        { guard.VerifyHeldNativeLibrary(this); }
        void VerifyNativeLibraryFile(FileStream file)
        {
            CheckOwner();
            if ((int)enterCount.GetValue(elf) != 0) throw Refuse("native attestation requires no active guest call");
            Type expected = elf.GetType().Assembly.GetType("BizHawk.Emulation.Cores.Waterbox.WaterboxHostNative", true);
            FieldInfo implementation = elf.GetType().GetField("NativeImpl", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (implementation == null || !implementation.IsInitOnly || implementation.FieldType != expected)
                throw Refuse("NativeImpl source shape differs");
            object instance = implementation.GetValue(null);
            if (instance == null || instance.GetType().BaseType != expected) throw Refuse("native invoker source shape differs");
            FieldInfo raw = NativeEntry(instance, "wbx_get_proc_addr_raw"), activate = NativeEntry(instance, "wbx_activate_host");
            if (!file.CanRead || !file.CanSeek || file.CanWrite) throw Refuse("held native stream access differs");
            long savedPosition = file.Position;
            Microsoft.Win32.SafeHandles.SafeFileHandle handle = null;
            bool added = false;
            try {
                handle = file.SafeFileHandle; // Mono6.12.0.206: actual Unix FD; getter may flush read buffering.
                handle.DangerousAddRef(ref added);
                long number = handle.DangerousGetHandle().ToInt64();
                if (!added || handle.IsClosed || handle.IsInvalid || number < 0 || number > int.MaxValue)
                    throw Refuse("held native descriptor unavailable");
                int fd = (int)number;
                string identity, beforeFd = HeldFdIdentity(fd, out identity);
                long bias, checkBias;
                string before = NativeMapIdentity(implementation, instance, raw, activate, identity, out bias);
                byte[] image = ReadHeldNativeImage(file);
                string checkIdentity;
                if (beforeFd != HeldFdIdentity(fd, out checkIdentity) || identity != checkIdentity ||
                    before != NativeMapIdentity(implementation, instance, raw, activate, identity, out checkBias))
                    throw Refuse("held FD/mount/native maps changed before RX read");
                byte[] chunk = new byte[65536];
                for (int pos = 0; pos < 0x67C00; pos += chunk.Length) {
                    int n = Math.Min(chunk.Length, 0x67C00 - pos);
                    Marshal.Copy(new IntPtr(checked(bias + 0x21880 + pos)), chunk, 0, n);
                    for (int i = 0; i < n; i++) if (chunk[i] != image[0x20880 + pos + i])
                        throw Refuse("complete native RX differs; no relocation exclusions");
                }
                ReadHeldNativeImage(file); // Full same-stream SHA again, not a pathname reopen.
                if (beforeFd != HeldFdIdentity(fd, out checkIdentity) || identity != checkIdentity ||
                    before != NativeMapIdentity(implementation, instance, raw, activate, identity, out checkBias))
                    throw Refuse("held FD/mount/native maps changed during RX read");
            } finally {
                try { file.Position = savedPosition; }
                finally {
                    if (added) handle.DangerousRelease();
                    GC.KeepAlive(file); GC.KeepAlive(guard);
                }
            }
        }
        static byte[] ReadHeldNativeImage(FileStream file)
        {
            byte[] image = new byte[688512];
            if (file.Length != image.Length) throw Refuse("held native object size differs");
            file.Position = 0;
            int pos = 0, n;
            while (pos < image.Length && (n = file.Read(image, pos, image.Length - pos)) > 0) pos += n;
            if (pos != image.Length || file.ReadByte() != -1 ||
                HashBytes(image) != "d2367818aafb4e520ad5ab005b5762c61506b0c819c4d79687235acfb0fc0c78")
                throw Refuse("held native file incomplete/changed/hash differs");
            return image;
        }
        static string HeldFdIdentity(int fd, out string identity)
        {
            string[] info = File.ReadAllLines("/proc/self/fdinfo/" + fd.ToString(CultureInfo.InvariantCulture));
            ulong mount = UniqueFdField(info, "mnt_id"), inode = UniqueFdField(info, "ino");
            if (mount == 0 || inode == 0) throw Refuse("zero FD mount/inode identity");
            string selected = null, device = null;
            foreach (string line in File.ReadAllLines("/proc/self/mountinfo")) {
                string[] fields = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 10) throw Refuse("unrecognized mountinfo row");
                ulong id = Decimal(fields[0]);
                if (id != mount) continue;
                int separator = Array.IndexOf(fields, "-");
                if (selected != null || separator < 6 || fields.Length != separator + 4)
                    throw Refuse("duplicate/invalid FD mountinfo record");
                string[] dev = fields[2].Split(':');
                if (dev.Length != 2) throw Refuse("invalid mountinfo decimal device");
                ulong major = Decimal(dev[0]), minor = Decimal(dev[1]);
                if (major > uint.MaxValue || minor > uint.MaxValue) throw Refuse("mount device out of range");
                device = major.ToString("x", CultureInfo.InvariantCulture) + ":" + minor.ToString("x", CultureInfo.InvariantCulture);
                selected = line;
            }
            if (selected == null) throw Refuse("held FD mount absent from current mountinfo");
            identity = device + ":" + inode.ToString(CultureInfo.InvariantCulture);
            return fd.ToString(CultureInfo.InvariantCulture) + "|" + mount.ToString(CultureInfo.InvariantCulture) + "|" + identity + "|" + selected;
        }
        static ulong UniqueFdField(string[] lines, string name)
        {
            string found = null;
            foreach (string line in lines) {
                int colon = line.IndexOf(':');
                if (colon < 0 || line.Substring(0, colon).Trim() != name) continue;
                if (found != null) throw Refuse("duplicate fdinfo " + name);
                found = line.Substring(colon + 1).Trim();
            }
            if (found == null) throw Refuse("missing fdinfo " + name);
            return Decimal(found);
        }
        static ulong Decimal(string s)
        { return ulong.Parse(s, NumberStyles.None, CultureInfo.InvariantCulture); }
        static FieldInfo NativeEntry(object instance, string method)
        {
            FieldInfo f = instance.GetType().GetField("EntryPtrField" + method,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (f == null || f.FieldType != typeof(IntPtr) || f.IsInitOnly) throw Refuse("raw native entry field differs");
            return f; // Native adapter passes dlsym pointers through unchanged; never call this value.
        }
        sealed class Mapping
        {
            internal long Start, End, Offset;
            internal string Device, Inode, Path, Permissions, Line;
            internal string Identity { get { return Device + ":" + Inode; } }
            internal void RequireRx()
            {
                if (Permissions[0] != 'r' || Permissions[1] != '-' || Permissions[2] != 'x' ||
                    Inode == "0" || !Path.StartsWith("/", StringComparison.Ordinal) ||
                    Path.StartsWith("/memfd:", StringComparison.Ordinal) ||
                    Path.EndsWith(" (deleted)", StringComparison.Ordinal)) throw Refuse("not stable file-backed nonwritable RX");
            }
        }
        static List<Mapping> ReadMaps()
        {
            var maps = new List<Mapping>();
            foreach (string line in File.ReadAllLines("/proc/self/maps")) {
                var m = System.Text.RegularExpressions.Regex.Match(line,
                    "\\A([0-9a-fA-F]+)-([0-9a-fA-F]+) ([r-][w-][x-][ps]) ([0-9a-fA-F]+) ([0-9a-fA-F]+):([0-9a-fA-F]+) +([0-9]+) *(.*)\\z");
                if (!m.Success) throw Refuse("unrecognized proc maps row");
                ulong start = ulong.Parse(m.Groups[1].Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
                ulong end = ulong.Parse(m.Groups[2].Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
                if (end <= start) throw Refuse("invalid unsigned maps range");
                if (start > long.MaxValue) continue; // Kernel vsyscall/high-half rows cannot cover these user pointers.
                var row = new Mapping { Start = checked((long)start), End = checked((long)end),
                    Permissions = m.Groups[3].Value, Offset = HexLong(m.Groups[4].Value),
                    Device = HexLong(m.Groups[5].Value).ToString("x") + ":" + HexLong(m.Groups[6].Value).ToString("x"),
                    Inode = ulong.Parse(m.Groups[7].Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                    Path = m.Groups[8].Value, Line = line };
                if (row.Start < 0 || row.End <= row.Start || row.Offset < 0) throw Refuse("invalid maps range");
                maps.Add(row);
            }
            return maps;
        }
        static Mapping MapAt(List<Mapping> maps, long address)
        {
            Mapping found = null;
            foreach (var m in maps) if (m.Start <= address && address < m.End) {
                if (found != null) throw Refuse("overlapping maps at native address"); found = m;
            }
            if (found == null) throw Refuse("native pointer/range unmapped");
            found.RequireRx(); return found;
        }
        static string NativeMapIdentity(FieldInfo implementation, object instance, FieldInfo raw, FieldInfo activate,
            string heldIdentity, out long bias)
        {
            if (!ReferenceEquals(implementation.GetValue(null), instance)) throw Refuse("native implementation changed");
            long p = ((IntPtr)raw.GetValue(instance)).ToInt64(), q = ((IntPtr)activate.GetValue(instance)).ToInt64();
            if (p <= 0 || q <= 0) throw Refuse("null/unsupported native pointer");
            bias = checked(p - 0x78630);
            if (bias < 0 || checked(q - 0x782F0) != bias || (bias & 4095) != 0) throw Refuse("native anchor load biases differ");
            var maps = ReadMaps(); Mapping first = MapAt(maps, p), second = MapAt(maps, q);
            if (first.Identity != heldIdentity || second.Identity != heldIdentity || checked(first.Offset + p - first.Start) != 0x77630 ||
                checked(second.Offset + q - second.Start) != 0x772F0) throw Refuse("native anchor file identities/offsets differ");
            string signature = p.ToString("x") + ":" + q.ToString("x") + "|" + first.Line + "|" + second.Line;
            long start = checked(bias + 0x21880), end = checked(bias + 0x89480);
            for (long pos = start; pos < end;) {
                Mapping m = MapAt(maps, pos);
                if (m.Identity != first.Identity || checked(m.Offset + pos - m.Start) != 0x20880 + pos - start)
                    throw Refuse("native RX coverage/file offset differs");
                signature += "|" + m.Line; pos = Math.Min(m.End, end);
            }
            return signature;
        }
        static long HexLong(string s)
        { return checked((long)ulong.Parse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)); }
        void StopCallbacks()
        { foreach (var r in registrations) r.Dispose(); registrations.Clear(); }
        public void Dispose()
        {
            if (closed) return;
            if (Thread.CurrentThread.ManagedThreadId != guard.ThreadId) throw Refuse("dispose on owning thread only");
            StopCallbacks(); closed = true;
            // Guard is separately owned; disposal does not claim successful final attestation.
        }
        static FieldInfo Field(Type type, string name)
        { return type.GetField(name, PrivateInstance) ?? throw Refuse("missing exact field: " + type.FullName + "." + name); }
        static void RequireAssembly(string name, string path)
        {
            Assembly found = null;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                if (a.GetName().Name == name) { if (found != null) throw Refuse("duplicate managed assembly"); found = a; }
            if (found == null || Path.GetFullPath(found.Location) != Path.GetFullPath(path))
                throw Refuse("managed assembly path differs: " + name);
        }
        static void RequireHashShape(string s)
        { if (s == null || s.Length != 64 || !System.Text.RegularExpressions.Regex.IsMatch(s, "\\A[0-9a-f]{64}\\z")) throw Refuse("reviewed SHA256 required"); }
        static void RequireBytes(byte[] b, int offset, string hex)
        { for (int i = 0; i < hex.Length / 2; i++) if (b[offset + i] != Convert.ToByte(hex.Substring(i * 2, 2), 16)) throw Refuse("CPU operand bytes differ"); }
        static string HashBytes(byte[] b) { using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(b)); }
        static string Hex(byte[] b) { return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant(); }
        static InvalidOperationException Refuse(string why) { return new InvalidOperationException("Clock reader unavailable: " + why); }
    }
}
