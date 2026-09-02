using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace OpenGGF.BizHawk.Headless
{
    internal enum OverrideResumeBundlePublicationResult
    {
        Durable,
        CommittedButDurabilityUnconfirmed
    }

    internal interface IOverrideResumeBundlePublicationHooks
    {
        void BeforeNativeOperation(string name);
        void AtBarrier(string name, string privateName);
    }

    internal sealed class OverrideResumeBundlePublicationHooks
        : IOverrideResumeBundlePublicationHooks
    {
        internal static readonly OverrideResumeBundlePublicationHooks None =
            new OverrideResumeBundlePublicationHooks();
        private OverrideResumeBundlePublicationHooks() { }
        public void BeforeNativeOperation(string name) { }
        public void AtBarrier(string name, string privateName) { }
    }

    internal struct OverrideResumeNativeResult
    {
        internal OverrideResumeNativeResult(int returnValue, int error)
        { ReturnValue = returnValue; Error = error; }
        internal int ReturnValue { get; private set; }
        internal int Error { get; private set; }
    }

    internal interface IOverrideResumeBundleNativeAdapter
    {
        OverrideResumeNativeResult RenameAt2(int oldDirectoryFd,
            string oldPath, int newDirectoryFd, string newPath, uint flags);
        OverrideResumeNativeResult Fsync(int fd, string operationName);
    }

    internal sealed class OverrideResumeBundleNativeAdapter
        : IOverrideResumeBundleNativeAdapter
    {
        internal static readonly OverrideResumeBundleNativeAdapter Instance =
            new OverrideResumeBundleNativeAdapter();
        private OverrideResumeBundleNativeAdapter() { }

        public OverrideResumeNativeResult RenameAt2(int oldDirectoryFd,
            string oldPath, int newDirectoryFd, string newPath, uint flags)
        {
            int result = NativeRenameAt2(oldDirectoryFd, oldPath,
                newDirectoryFd, newPath, flags);
            return new OverrideResumeNativeResult(result,
                result == 0 ? 0 : Marshal.GetLastWin32Error());
        }

        public OverrideResumeNativeResult Fsync(int fd, string operationName)
        {
            int result = NativeFsync(fd);
            return new OverrideResumeNativeResult(result,
                result == 0 ? 0 : Marshal.GetLastWin32Error());
        }

        [DllImport("libc", EntryPoint = "renameat2", CharSet = CharSet.Ansi,
            SetLastError = true)]
        private static extern int NativeRenameAt2(int olddirfd, string oldpath,
            int newdirfd, string newpath, uint flags);
        [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
        private static extern int NativeFsync(int fd);
    }

    internal sealed class OverrideResumeInjectedNativeException : IOException
    {
        internal OverrideResumeInjectedNativeException(string operation,
            int error) : base("injected native failure for " + operation
                + " with errno " + error + ".")
        {
            Error = error;
        }
        internal int Error { get; private set; }
    }

    /// <summary>
    /// Linux-only publisher for the fixed override-resume bundle. The private
    /// sibling is complete and durable before one no-replace directory rename
    /// makes it public. No failure path removes a public name.
    /// </summary>
    internal sealed class OverrideResumeFirstDivergencePublisher
    {
        internal const string BundleName =
            "override-resume-first-divergence-v1";
        internal const string NamespaceStabilityPrecondition =
            "All publishers cooperate through the exclusive fixture-root lock; "
            + "the authoritative root and ancestors remain namespace-stable and "
            + "protected from rename and mount mutation. Same-credential rename "
            + "and mount mutation after validation is unsupported.";

        private static readonly string[] FileNames =
        {
            "s1/s1-override-resume-reference.v1.jsonl.gz",
            "s1/s1-override-resume-metadata.v1.json",
            "s2/s2-override-resume-reference.v1.jsonl.gz",
            "s2/s2-override-resume-metadata.v1.json"
        };

        private readonly OverrideResumeFirstDivergenceExtractor extractor;
        private readonly IOverrideResumeBundlePublicationHooks hooks;
        private readonly IOverrideResumeBundleNativeAdapter native;

        internal OverrideResumeFirstDivergencePublisher(
            OverrideResumeFirstDivergenceExtractor value)
            : this(value, OverrideResumeBundlePublicationHooks.None,
                OverrideResumeBundleNativeAdapter.Instance) { }

        internal OverrideResumeFirstDivergencePublisher(
            OverrideResumeFirstDivergenceExtractor value,
            IOverrideResumeBundlePublicationHooks publicationHooks)
            : this(value, publicationHooks,
                OverrideResumeBundleNativeAdapter.Instance) { }

        internal OverrideResumeFirstDivergencePublisher(
            OverrideResumeFirstDivergenceExtractor value,
            IOverrideResumeBundlePublicationHooks publicationHooks,
            IOverrideResumeBundleNativeAdapter nativeAdapter)
        {
            extractor = value ?? throw new ArgumentNullException("value");
            hooks = publicationHooks
                ?? throw new ArgumentNullException("publicationHooks");
            native = nativeAdapter
                ?? throw new ArgumentNullException("nativeAdapter");
        }

        internal OverrideResumeBundlePublicationResult Publish(
            OverrideResumeFirstDivergenceExtractor.Inputs inputs,
            string tracechaserRoot, string inputRepositoryRoot,
            string fixtureRoot)
        {
            ValidatePaths(tracechaserRoot, inputRepositoryRoot, fixtureRoot);
            using (var transaction = LinuxBundleTransaction.Open(
                inputRepositoryRoot, fixtureRoot, hooks, native))
            {
                OverrideResumeFirstDivergenceExtractor.Output output =
                    extractor.Extract(inputs);
                byte[][] values =
                {
                    output.S1.ReferenceGzip, output.S1.MetadataUtf8,
                    output.S2.ReferenceGzip, output.S2.MetadataUtf8
                };
                return transaction.Publish(values);
            }
        }

        private static void ValidatePaths(string tracechaserRoot,
            string inputRepositoryRoot, string fixtureRoot)
        {
            string input = Absolute(inputRepositoryRoot,
                "Input repository root");
            string trace = Absolute(tracechaserRoot, "TraceChaser root");
            string fixture = Absolute(fixtureRoot, "Fixture root");
            if (trace != Path.Combine(input, "tools", "tracechaser"))
                throw new ArgumentException(
                    "TraceChaser root must be the pinned consumer submodule path.");
            if (fixture != Path.Combine(input, "src", "test", "resources",
                    "audio", "parity"))
                throw new ArgumentException(
                    "Fixture root must be the requested consumer audio/parity subtree.");
        }

        private static string Absolute(string path, string label)
        {
            if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path))
                throw new ArgumentException(label + " must be absolute.");
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        }

        private sealed class LinuxBundleTransaction : IDisposable
        {
            private const int ENoEnt = 2;
            private const int EExist = 17;
            private const int EInterrupted = 4;
            private const int AtSymlinkNoFollow = 0x100;
            private const int OReadOnly = 0;
            private const int OWriteOnly = 1;
            private const int OCreate = 0x40;
            private const int OExclusive = 0x80;
            private const int ODirectory = 0x10000;
            private const int ONoFollow = 0x20000;
            private const int OCloseExec = 0x80000;
            private const uint DirectoryMode = 0x1C0;
            private const uint FileMode = 0x180;
            private const int LockExclusive = 2;
            private const uint RenameNoReplace = 1;
            private const ulong ResolveNoSymlinks = 0x04;
            private const ulong ResolveBeneath = 0x08;
            private const long SysOpenAt2 = 437;
            private const uint StatxType = 0x0001;
            private const uint StatxIno = 0x0100;
            private const uint StatxMountId = 0x1000;
            private const int StatxBufferSize = 256;
            private const int SIfmt = 0xF000;
            private const int SIfreg = 0x8000;
            private const int SIfdir = 0x4000;

            private readonly string repositoryPath;
            private readonly string fixturePath;
            private readonly IOverrideResumeBundlePublicationHooks hooks;
            private readonly IOverrideResumeBundleNativeAdapter native;
            private readonly Identity rootIdentity;
            private int slashFd;
            private int repositoryFd;
            private int rootFd;

            private LinuxBundleTransaction(string repository,
                string fixture, IOverrideResumeBundlePublicationHooks value,
                IOverrideResumeBundleNativeAdapter nativeAdapter, int slash,
                int repo, int root, Identity identity)
            {
                repositoryPath = repository;
                fixturePath = fixture;
                hooks = value;
                native = nativeAdapter;
                slashFd = slash;
                repositoryFd = repo;
                rootFd = root;
                rootIdentity = identity;
            }

            internal static LinuxBundleTransaction Open(string repository,
                string fixture, IOverrideResumeBundlePublicationHooks hooks,
                IOverrideResumeBundleNativeAdapter native)
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                        || RuntimeInformation.ProcessArchitecture
                            != Architecture.X64)
                    throw new PlatformNotSupportedException(
                        "Atomic bundle publication requires Linux x86-64 openat2.");
                int slash = -1, repo = -1, root = -1;
                try
                {
                    slash = NativeOpen(hooks, "/",
                        OReadOnly | ODirectory | ONoFollow | OCloseExec);
                    repo = OpenTrusted(hooks, slash,
                        repository.TrimStart('/'));
                    int trace = OpenTrusted(hooks, repo, "tools/tracechaser");
                    NativeClose(trace);
                    root = OpenTrusted(hooks, repo,
                        "src/test/resources/audio/parity");
                    NativeCall(hooks, "flock");
                    if (Flock(root, LockExclusive) != 0)
                        throw Failure("flock fixture root", fixture);
                    Identity identity = ReadIdentity(hooks, root, ".",
                        "fixture root");
                    RequireDirectory(identity, "fixture root");
                    return new LinuxBundleTransaction(repository, fixture,
                        hooks, native, slash, repo, root, identity);
                }
                catch
                {
                    NativeClose(root);
                    NativeClose(repo);
                    NativeClose(slash);
                    throw;
                }
            }

            internal OverrideResumeBundlePublicationResult Publish(
                byte[][] values)
            {
                if (values == null || values.Length != FileNames.Length)
                    throw new ArgumentException("Exact bundle bytes are required.");
                RequireAbsent(rootFd, BundleName);
                string privateName = "." + BundleName + ".tmp."
                    + Guid.NewGuid().ToString("N");
                NativeCall(hooks, "mkdirat-private");
                if (MkdirAt(rootFd, privateName, DirectoryMode) != 0)
                    throw Failure("create private bundle", privateName);
                int privateFd = -1, s1Fd = -1, s2Fd = -1;
                try
                {
                    privateFd = OpenDirectory(rootFd, privateName,
                        "private bundle");
                    SetMode(privateFd, DirectoryMode, "private bundle");
                    Identity privateIdentity = ReadIdentity(hooks, privateFd,
                        ".", "private bundle");
                    hooks.AtBarrier("private-created", privateName);
                    s1Fd = CreateDirectory(privateFd, "s1");
                    s2Fd = CreateDirectory(privateFd, "s2");
                    Identity s1Identity = ReadIdentity(hooks, s1Fd, ".",
                        "staged s1 directory");
                    Identity s2Identity = ReadIdentity(hooks, s2Fd, ".",
                        "staged s2 directory");
                    WriteMember(s1Fd,
                        "s1-override-resume-reference.v1.jsonl.gz", values[0]);
                    WriteMember(s1Fd,
                        "s1-override-resume-metadata.v1.json", values[1]);
                    WriteMember(s2Fd,
                        "s2-override-resume-reference.v1.jsonl.gz", values[2]);
                    WriteMember(s2Fd,
                        "s2-override-resume-metadata.v1.json", values[3]);

                    hooks.AtBarrier("before-staged-validation", privateName);
                    ValidateBundle(privateFd, s1Fd, s2Fd, values);
                    SyncDirectory(s1Fd, "fsync-s1");
                    SyncDirectory(s2Fd, "fsync-s2");
                    SyncDirectory(privateFd, "fsync-private");

                    hooks.AtBarrier("before-root-revalidation", privateName);
                    RevalidateRoot();
                    RequireAbsent(rootFd, BundleName);
                    Identity namedPrivate = ReadIdentity(hooks, rootFd,
                        privateName, "private bundle name");
                    if (!Same(privateIdentity, namedPrivate, SIfdir))
                        throw new IOException(
                            "Private bundle identity changed before commit.");
                    RequireNamedDirectoryIdentity(privateFd, "s1",
                        s1Identity);
                    RequireNamedDirectoryIdentity(privateFd, "s2",
                        s2Identity);
                    ValidateBundle(privateFd, s1Fd, s2Fd, values);

                    hooks.AtBarrier("before-rename", privateName);
                    NativeCall(hooks, "renameat2");
                    OverrideResumeNativeResult rename = native.RenameAt2(
                        rootFd, privateName, rootFd, BundleName,
                        RenameNoReplace);
                    if (rename.ReturnValue != 0)
                    {
                        if (rename.Error == EExist)
                            throw new IOException(
                                "Final bundle already exists and will not be replaced: "
                                + Path.Combine(fixturePath, BundleName));
                        throw Failure("commit bundle with renameat2",
                            BundleName, rename.Error);
                    }
                    hooks.AtBarrier("after-rename", privateName);
                    try
                    {
                        SyncDirectory(rootFd, "fsync-root");
                        return OverrideResumeBundlePublicationResult.Durable;
                    }
                    catch (IOException)
                    {
                        return OverrideResumeBundlePublicationResult
                            .CommittedButDurabilityUnconfirmed;
                    }
                }
                finally
                {
                    NativeClose(s2Fd);
                    NativeClose(s1Fd);
                    NativeClose(privateFd);
                }
            }

            private void RevalidateRoot()
            {
                int currentRepo = -1, currentRoot = -1;
                try
                {
                    currentRepo = OpenTrusted(hooks, slashFd,
                        repositoryPath.TrimStart('/'));
                    currentRoot = OpenTrusted(hooks, currentRepo,
                        "src/test/resources/audio/parity");
                    Identity current = ReadIdentity(hooks, currentRoot, ".",
                        "reopened fixture root");
                    if (!Same(rootIdentity, current, SIfdir))
                        throw new IOException(
                            "Fixture root identity changed before commit.");
                }
                finally
                {
                    NativeClose(currentRoot);
                    NativeClose(currentRepo);
                }
            }

            private void ValidateBundle(int privateFd, int s1Fd, int s2Fd,
                byte[][] expected)
            {
                RequireInventory(privateFd, new[] { "s1", "s2" },
                    "bundle inventory");
                RequireInventory(s1Fd, new[]
                {
                    "s1-override-resume-metadata.v1.json",
                    "s1-override-resume-reference.v1.jsonl.gz"
                }, "S1 bundle inventory");
                RequireInventory(s2Fd, new[]
                {
                    "s2-override-resume-metadata.v1.json",
                    "s2-override-resume-reference.v1.jsonl.gz"
                }, "S2 bundle inventory");
                byte[] s1Reference = ReadMember(s1Fd,
                    "s1-override-resume-reference.v1.jsonl.gz");
                byte[] s1Metadata = ReadMember(s1Fd,
                    "s1-override-resume-metadata.v1.json");
                byte[] s2Reference = ReadMember(s2Fd,
                    "s2-override-resume-reference.v1.jsonl.gz");
                byte[] s2Metadata = ReadMember(s2Fd,
                    "s2-override-resume-metadata.v1.json");
                byte[][] actual = { s1Reference, s1Metadata,
                    s2Reference, s2Metadata };
                for (int index = 0; index < actual.Length; index++)
                    if (!BytesEqual(expected[index], actual[index]))
                        throw new IOException(
                            "Staged bundle member bytes changed before commit.");
                OverrideResumeFirstDivergenceExtractor
                    .ValidatePublishedGameBytes("s1", s1Reference, s1Metadata);
                OverrideResumeFirstDivergenceExtractor
                    .ValidatePublishedGameBytes("s2", s2Reference, s2Metadata);
            }

            private int CreateDirectory(int parentFd, string name)
            {
                NativeCall(hooks, "mkdirat-" + name);
                if (MkdirAt(parentFd, name, DirectoryMode) != 0)
                    throw Failure("create staged directory", name);
                int fd = OpenDirectory(parentFd, name,
                    "staged " + name + " directory");
                try
                {
                    SetMode(fd, DirectoryMode, name);
                    return fd;
                }
                catch
                {
                    NativeClose(fd);
                    throw;
                }
            }

            private void RequireNamedDirectoryIdentity(int parentFd,
                string name, Identity expected)
            {
                Identity actual = ReadIdentity(hooks, parentFd, name,
                    "staged directory name");
                if (!Same(expected, actual, SIfdir))
                    throw new IOException("Staged directory identity changed"
                        + " before commit: " + name + ".");
            }

            private void WriteMember(int directoryFd, string name,
                byte[] value)
            {
                NativeCall(hooks, "openat-create-file");
                int fd = OpenAt(directoryFd, name, OWriteOnly | OCreate
                    | OExclusive | ONoFollow | OCloseExec, FileMode);
                if (fd < 0) throw Failure("create staged member", name);
                try
                {
                    SetMode(fd, FileMode, name);
                    WriteAll(fd, value, name);
                    NativeCall(hooks, "fsync-file");
                    OverrideResumeNativeResult result = native.Fsync(fd,
                        "fsync-file");
                    if (result.ReturnValue != 0)
                        throw Failure("fsync staged member", name,
                            result.Error);
                }
                finally
                {
                    NativeClose(fd);
                }
            }

            private byte[] ReadMember(int directoryFd, string name)
            {
                Identity identity = ReadIdentity(hooks, directoryFd, name,
                    "staged member");
                if ((identity.Mode & SIfmt) != SIfreg)
                    throw new IOException(
                        "Staged bundle member is not a regular file: " + name);
                NativeCall(hooks, "openat-read-file");
                int fd = OpenAt(directoryFd, name,
                    OReadOnly | ONoFollow | OCloseExec, 0);
                if (fd < 0) throw Failure("open staged member", name);
                try
                {
                    using (var output = new MemoryStream())
                    {
                        var buffer = new byte[8192];
                        while (true)
                        {
                            NativeCall(hooks, "read");
                            int count = NativeRead(fd, buffer);
                            if (count == 0) break;
                            output.Write(buffer, 0, count);
                            if (output.Length > 1024 * 1024)
                                throw new IOException(
                                    "Staged bundle member exceeds size bound.");
                        }
                        return output.ToArray();
                    }
                }
                finally { NativeClose(fd); }
            }

            private void RequireInventory(int directoryFd, string[] expected,
                string label)
            {
                string[] actual = Enumerate(directoryFd);
                Array.Sort(actual, StringComparer.Ordinal);
                if (actual.Length != expected.Length)
                    throw new IOException(label + " is not exact: "
                        + string.Join(",", actual) + ".");
                for (int index = 0; index < actual.Length; index++)
                    if (actual[index] != expected[index])
                        throw new IOException(label + " is not exact: "
                            + string.Join(",", actual) + ".");
            }

            private string[] Enumerate(int directoryFd)
            {
                NativeCall(hooks, "openat-enumeration");
                int duplicate = OpenAt(directoryFd, ".",
                    OReadOnly | ODirectory | ONoFollow | OCloseExec, 0);
                if (duplicate < 0)
                    throw Failure("reopen staged directory", ".");
                NativeCall(hooks, "fdopendir");
                IntPtr directory = FdOpenDir(duplicate);
                if (directory == IntPtr.Zero)
                {
                    NativeClose(duplicate);
                    throw Failure("open staged directory stream", ".");
                }
                var names = new List<string>();
                try
                {
                    while (true)
                    {
                        NativeCall(hooks, "readdir");
                        Marshal.WriteInt32(ErrnoLocation(), 0);
                        IntPtr entry = ReadDir(directory);
                        if (entry == IntPtr.Zero)
                        {
                            int error = Marshal.GetLastWin32Error();
                            if (error != 0)
                                throw Failure("enumerate staged directory",
                                    ".", error);
                            break;
                        }
                        string name = Marshal.PtrToStringAnsi(
                            IntPtr.Add(entry, 19));
                        if (name != "." && name != "..") names.Add(name);
                    }
                }
                finally { CloseDir(directory); }
                return names.ToArray();
            }

            private void RequireAbsent(int directoryFd, string name)
            {
                Identity ignored;
                int error;
                if (TryReadIdentity(hooks, directoryFd, name, out ignored,
                        out error))
                    throw new IOException(
                        "Final bundle already exists and will not be replaced: "
                        + Path.Combine(fixturePath, name));
                if (error != ENoEnt)
                    throw Failure("inspect public bundle", name, error);
            }

            private int OpenDirectory(int parentFd, string name, string label)
            {
                NativeCall(hooks, "openat-directory");
                int fd = OpenAt(parentFd, name,
                    OReadOnly | ODirectory | ONoFollow | OCloseExec, 0);
                if (fd < 0) throw Failure("open " + label, name);
                return fd;
            }

            private void SetMode(int fd, uint mode, string label)
            {
                NativeCall(hooks, "fchmod");
                if (Fchmod(fd, mode) != 0)
                    throw Failure("set private mode", label);
            }

            private void SyncDirectory(int fd, string operation)
            {
                try { NativeCall(hooks, operation); }
                catch (OverrideResumeInjectedNativeException exception)
                { throw new IOException(exception.Message, exception); }
                OverrideResumeNativeResult result = native.Fsync(fd,
                    operation);
                if (result.ReturnValue != 0)
                    throw Failure(operation, ".", result.Error);
            }

            private static int OpenTrusted(
                IOverrideResumeBundlePublicationHooks hooks, int parentFd,
                string path)
            {
                if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path))
                    throw new IOException(
                        "Trusted openat2 path must be nonempty and relative.");
                var how = new OpenHow
                {
                    Flags = OReadOnly | ODirectory | ONoFollow | OCloseExec,
                    Resolve = ResolveBeneath | ResolveNoSymlinks
                };
                NativeCall(hooks, "openat2");
                long fd = SyscallOpenAt2(SysOpenAt2, parentFd, path,
                    ref how, new UIntPtr(24));
                if (fd < 0) throw Failure("openat2 trusted directory", path);
                return checked((int)fd);
            }

            private static int NativeOpen(
                IOverrideResumeBundlePublicationHooks hooks, string path,
                int flags)
            {
                NativeCall(hooks, "open-anchor");
                int fd = Open(path, flags);
                if (fd < 0) throw Failure("open trusted anchor", path);
                return fd;
            }

            private static Identity ReadIdentity(
                IOverrideResumeBundlePublicationHooks hooks, int directoryFd,
                string name, string label)
            {
                Identity value;
                int error;
                if (!TryReadIdentity(hooks, directoryFd, name, out value,
                        out error))
                    throw Failure("statx " + label, name, error);
                return value;
            }

            private static bool TryReadIdentity(
                IOverrideResumeBundlePublicationHooks hooks, int directoryFd,
                string name, out Identity identity, out int error)
            {
                IntPtr buffer = Marshal.AllocHGlobal(StatxBufferSize);
                try
                {
                    NativeCall(hooks, "statx");
                    if (Statx(directoryFd, name, AtSymlinkNoFollow,
                            StatxType | StatxIno | StatxMountId, buffer) != 0)
                    {
                        error = Marshal.GetLastWin32Error();
                        identity = new Identity();
                        return false;
                    }
                    uint mask = unchecked((uint)Marshal.ReadInt32(buffer, 0));
                    uint required = StatxType | StatxIno | StatxMountId;
                    if ((mask & required) != required)
                        throw new IOException(
                            "statx did not return type, inode, and mount identity.");
                    identity = new Identity
                    {
                        Mode = unchecked((ushort)Marshal.ReadInt16(buffer, 28)),
                        Inode = unchecked((ulong)Marshal.ReadInt64(buffer, 32)),
                        DeviceMajor = unchecked((uint)Marshal.ReadInt32(buffer, 136)),
                        DeviceMinor = unchecked((uint)Marshal.ReadInt32(buffer, 140)),
                        MountId = unchecked((ulong)Marshal.ReadInt64(buffer, 144))
                    };
                    error = 0;
                    return true;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }

            private static void RequireDirectory(Identity identity,
                string label)
            {
                if ((identity.Mode & SIfmt) != SIfdir)
                    throw new IOException(label + " is not a directory.");
            }

            private static bool Same(Identity first, Identity second,
                int type)
            {
                return (first.Mode & SIfmt) == type
                    && (second.Mode & SIfmt) == type
                    && first.DeviceMajor == second.DeviceMajor
                    && first.DeviceMinor == second.DeviceMinor
                    && first.Inode == second.Inode
                    && first.MountId == second.MountId;
            }

            private void WriteAll(int fd, byte[] value, string name)
            {
                if (value == null) throw new ArgumentNullException("value");
                GCHandle pinned = GCHandle.Alloc(value, GCHandleType.Pinned);
                try
                {
                    int offset = 0;
                    while (offset < value.Length)
                    {
                        NativeCall(hooks, "write");
                        long count = Write(fd,
                            IntPtr.Add(pinned.AddrOfPinnedObject(), offset),
                            new UIntPtr(unchecked((uint)(value.Length - offset))))
                            .ToInt64();
                        if (count > 0) { offset += checked((int)count); continue; }
                        int error = Marshal.GetLastWin32Error();
                        if (count < 0 && error == EInterrupted) continue;
                        throw Failure("write staged member", name, error);
                    }
                }
                finally { pinned.Free(); }
            }

            private static int NativeRead(int fd, byte[] buffer)
            {
                GCHandle pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                try
                {
                    while (true)
                    {
                        long count = Read(fd, pinned.AddrOfPinnedObject(),
                            new UIntPtr(unchecked((uint)buffer.Length))).ToInt64();
                        if (count >= 0) return checked((int)count);
                        int error = Marshal.GetLastWin32Error();
                        if (error == EInterrupted) continue;
                        throw Failure("read staged member", ".", error);
                    }
                }
                finally { pinned.Free(); }
            }

            private static bool BytesEqual(byte[] first, byte[] second)
            {
                if (first == null || second == null
                    || first.Length != second.Length) return false;
                for (int index = 0; index < first.Length; index++)
                    if (first[index] != second[index]) return false;
                return true;
            }

            private static void NativeCall(
                IOverrideResumeBundlePublicationHooks hooks, string name)
            {
                try { hooks.BeforeNativeOperation(name); }
                catch (OverrideResumeInjectedNativeException) { throw; }
            }

            public void Dispose()
            {
                NativeClose(rootFd); rootFd = -1;
                NativeClose(repositoryFd); repositoryFd = -1;
                NativeClose(slashFd); slashFd = -1;
            }

            private static void NativeClose(int fd)
            { if (fd >= 0) Close(fd); }

            private static IOException Failure(string operation, string target)
            { return Failure(operation, target, Marshal.GetLastWin32Error()); }

            private static IOException Failure(string operation, string target,
                int error)
            { return new IOException(operation + " failed for " + target
                + " with errno " + error + "."); }

            private struct Identity
            {
                internal uint DeviceMajor, DeviceMinor;
                internal ulong Inode, MountId;
                internal ushort Mode;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct OpenHow
            {
                internal ulong Flags;
                internal ulong Mode;
                internal ulong Resolve;
            }

            [DllImport("libc", EntryPoint = "syscall", CharSet = CharSet.Ansi,
                SetLastError = true)]
            private static extern long SyscallOpenAt2(long number, int dirfd,
                string path, ref OpenHow how, UIntPtr size);
            [DllImport("libc", EntryPoint = "open", CharSet = CharSet.Ansi,
                SetLastError = true)]
            private static extern int Open(string path, int flags);
            [DllImport("libc", EntryPoint = "openat", CharSet = CharSet.Ansi,
                SetLastError = true)]
            private static extern int OpenAt(int dirfd, string path, int flags,
                uint mode);
            [DllImport("libc", EntryPoint = "mkdirat", CharSet = CharSet.Ansi,
                SetLastError = true)]
            private static extern int MkdirAt(int dirfd, string path, uint mode);
            [DllImport("libc", EntryPoint = "statx", CharSet = CharSet.Ansi,
                SetLastError = true)]
            private static extern int Statx(int dirfd, string path, int flags,
                uint mask, IntPtr buffer);
            [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
            private static extern int Flock(int fd, int operation);
            [DllImport("libc", EntryPoint = "fchmod", SetLastError = true)]
            private static extern int Fchmod(int fd, uint mode);
            [DllImport("libc", EntryPoint = "write", SetLastError = true)]
            private static extern IntPtr Write(int fd, IntPtr buffer,
                UIntPtr count);
            [DllImport("libc", EntryPoint = "read", SetLastError = true)]
            private static extern IntPtr Read(int fd, IntPtr buffer,
                UIntPtr count);
            [DllImport("libc", EntryPoint = "dup", SetLastError = true)]
            private static extern int Duplicate(int fd);
            [DllImport("libc", EntryPoint = "fdopendir", SetLastError = true)]
            private static extern IntPtr FdOpenDir(int fd);
            [DllImport("libc", EntryPoint = "readdir", SetLastError = true)]
            private static extern IntPtr ReadDir(IntPtr directory);
            [DllImport("libc", EntryPoint = "__errno_location")]
            private static extern IntPtr ErrnoLocation();
            [DllImport("libc", EntryPoint = "closedir", SetLastError = true)]
            private static extern int CloseDir(IntPtr directory);
            [DllImport("libc", EntryPoint = "close", SetLastError = true)]
            private static extern int Close(int fd);
        }
    }

    internal sealed class OverrideResumePublisherCommandOptions
    {
        internal const string Mode =
            "--override-resume-first-divergence-publisher";
        private OverrideResumePublisherCommandOptions(string tracechaserRoot,
            string inputRoot, string fixtureRoot,
            OverrideResumeFirstDivergenceExtractor.Inputs inputs)
        { TracechaserRoot = tracechaserRoot; InputRoot = inputRoot;
            FixtureRoot = fixtureRoot; Inputs = inputs; }
        internal string TracechaserRoot { get; private set; }
        internal string InputRoot { get; private set; }
        internal string FixtureRoot { get; private set; }
        internal OverrideResumeFirstDivergenceExtractor.Inputs Inputs
        { get; private set; }

        internal static bool IsRequested(string[] args)
        { return args != null && args.Length != 0 && args[0] == Mode; }

        internal static OverrideResumePublisherCommandOptions Parse(string[] args)
        {
            if (!IsRequested(args)) throw new ArgumentException(
                "Override-resume publisher mode is required.");
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 1; index < args.Length; index += 2)
            {
                string name = args[index];
                if (!Supported(name)) throw new ArgumentException(
                    "Unknown override-resume publisher argument: " + name + ".");
                if (index + 1 >= args.Length || string.IsNullOrEmpty(args[index + 1]))
                    throw new ArgumentException(name + " requires a value.");
                if (values.ContainsKey(name)) throw new ArgumentException(
                    "Duplicate override-resume publisher argument: " + name + ".");
                values.Add(name, args[index + 1]);
            }
            string trace = ExistingDirectory(Required(values,
                "--tracechaser-root"), "TraceChaser root");
            string input = ExistingDirectory(Required(values,
                "--input-repository-root"), "input repository root");
            string fixture = ExistingDirectory(Required(values,
                "--fixture-root"), "fixture root");
            return new OverrideResumePublisherCommandOptions(trace, input,
                fixture, new OverrideResumeFirstDivergenceExtractor.Inputs(
                    ExistingFile(Required(values, "--s1-raw-1"), "S1 raw 1"),
                    ExistingFile(Required(values, "--s1-attestation-1"), "S1 attestation 1"),
                    ExistingFile(Required(values, "--s1-raw-2"), "S1 raw 2"),
                    ExistingFile(Required(values, "--s1-attestation-2"), "S1 attestation 2"),
                    ExistingFile(Required(values, "--s2-raw-1"), "S2 raw 1"),
                    ExistingFile(Required(values, "--s2-attestation-1"), "S2 attestation 1"),
                    ExistingFile(Required(values, "--s2-raw-2"), "S2 raw 2"),
                    ExistingFile(Required(values, "--s2-attestation-2"), "S2 attestation 2")));
        }

        private static bool Supported(string value)
        { switch (value) { case "--tracechaser-root": case "--input-repository-root":
            case "--fixture-root": case "--s1-raw-1": case "--s1-attestation-1":
            case "--s1-raw-2": case "--s1-attestation-2": case "--s2-raw-1":
            case "--s2-attestation-1": case "--s2-raw-2": case "--s2-attestation-2":
                return true; default: return false; } }
        private static string Required(IDictionary<string, string> values,
            string name)
        { string value; if (!values.TryGetValue(name, out value))
            throw new ArgumentException(
                "Required override-resume publisher argument is missing: "
                + name + "."); return value; }
        private static string ExistingFile(string path, string label)
        { string full = Absolute(path, label); if (!File.Exists(full))
            throw new ArgumentException(label
                + " must be an existing absolute file."); return full; }
        private static string ExistingDirectory(string path, string label)
        { string full = Absolute(path, label); if (!Directory.Exists(full))
            throw new ArgumentException(label
                + " must be an existing absolute directory."); return full; }
        private static string Absolute(string path, string label)
        { if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path))
            throw new ArgumentException(label + " must be absolute.");
            return Path.GetFullPath(path); }
    }
}
