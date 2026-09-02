using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

[assembly: InternalsVisibleTo("BizHawk.Headless.Gpgx.Tests")]

namespace OpenGGF.BizHawk.Headless
{
    internal interface ILinkOperation
    {
        void Create(string temporary, string finalPath,
            Action createAnchoredLink);
    }

    internal sealed class LibcLinkOperation : ILinkOperation
    {
        public static readonly LibcLinkOperation Instance =
            new LibcLinkOperation();

        private LibcLinkOperation()
        {
        }

        public void Create(string temporary, string finalPath,
            Action createAnchoredLink)
        {
            if(createAnchoredLink==null)
                throw new ArgumentNullException("createAnchoredLink");
            createAnchoredLink();
        }
    }

    /// <summary>
    /// Retains an O_NOFOLLOW directory handle and the staged file's statx
    /// identity while a multi-file publication is in flight. Rollback moves
    /// the final name to a private no-replace quarantine. Quarantined entries
    /// remain as failure evidence because Linux has no atomic
    /// unlink-if-still-owned operation. Unsupported native identity
    /// operations fail before the link; rollback never falls back to a
    /// pathname-only delete.
    /// </summary>
    internal sealed class LinuxOwnedFinalRollback : IDisposable
    {
        private const int ENoEnt = 2;
        private const int EExist = 17;
        private const int AtFdcwd = -100;
        private const int ODirectory = 0x10000;
        private const int ONoFollow = 0x20000;
        private const int OCloseExec = 0x80000;
        private const int AtSymlinkNoFollow = 0x100;
        private const uint RenameNoReplace = 1;
        private const uint StatxType = 0x0001;
        private const uint StatxIno = 0x0100;
        private const int StatxBufferSize = 256;
        private const int SIfmt = 0xF000;
        private const int SIfreg = 0x8000;
        private const int SIfdir = 0x4000;

        private readonly string finalName;
        private readonly string temporaryName;
        private readonly string directoryPath;
        private readonly Identity stagedIdentity;
        private readonly Identity directoryIdentity;
        private readonly string[] anchoredDirectoryComponents;
        private readonly Action<string> afterIdentityVerified;
        private int directoryFd;
        private int anchoredRootFd;

        private struct Identity
        {
            internal uint DeviceMajor;
            internal uint DeviceMinor;
            internal ulong Inode;
            internal ushort Mode;
        }

        private LinuxOwnedFinalRollback(int fd,string path,string temporary,
            string final,Identity identity,Identity directory,
            int rootFd,string[] directoryComponents,
            Action<string> afterVerified)
        {
            directoryFd=fd;
            directoryPath=path;
            temporaryName=temporary;
            finalName=final;
            stagedIdentity=identity;
            directoryIdentity=directory;
            anchoredRootFd=rootFd;
            anchoredDirectoryComponents=directoryComponents;
            afterIdentityVerified=afterVerified;
        }

        internal static LinuxOwnedFinalRollback Prepare(
            string temporaryPath,string finalPath,
            Action<string> afterIdentityVerified)
        {
            string temporaryDirectory=Path.GetFullPath(
                Path.GetDirectoryName(temporaryPath));
            string finalDirectory=Path.GetFullPath(
                Path.GetDirectoryName(finalPath));
            if(temporaryDirectory!=finalDirectory)
                throw new IOException(
                    "Identity-checked rollback requires one publication directory.");
            int fd=Open(finalDirectory,
                ODirectory|ONoFollow|OCloseExec);
            if(fd<0)
                throw NativeFailure("open rollback directory",finalDirectory);
            try
            {
                ProbeRollbackOperations(fd);
                Identity staged=ReadRequiredIdentity(fd,
                    Path.GetFileName(temporaryPath),"staged output");
                if((staged.Mode&SIfmt)!=SIfreg)
                    throw new IOException(
                        "Staged output is not a regular file; publication refused.");
                Identity directory=ReadRequiredIdentity(fd,".",
                    "publication directory");
                if((directory.Mode&SIfmt)!=SIfdir)
                    throw new IOException(
                        "Publication directory is not a directory.");
                return new LinuxOwnedFinalRollback(fd,finalDirectory,
                    Path.GetFileName(temporaryPath),
                    Path.GetFileName(finalPath),staged,directory,
                    -1,null,
                    afterIdentityVerified);
            }
            catch
            {
                Close(fd);
                throw;
            }
        }

        internal void LinkNoReplace()
        {
            EnsureOpen();
            Identity current=ReadCurrentDirectoryIdentity();
            if(!SameDirectory(directoryIdentity,current))
                throw new IOException(
                    "Publication directory identity changed before link.");
            if(LinkAt(directoryFd,temporaryName,directoryFd,finalName,0)==0)
                return;
            int error=Marshal.GetLastWin32Error();
            if(error==EExist)
                throw new IOException(
                    "Final output already exists and will not be replaced: "
                    +Path.Combine(directoryPath,finalName));
            throw NativeFailure("publish anchored final",finalName,error);
        }

        internal void RemoveTemporary()
        {
            EnsureOpen();
            if(UnlinkAt(directoryFd,temporaryName,0)==0)return;
            int error=Marshal.GetLastWin32Error();
            if(error==ENoEnt)return;
            throw NativeFailure("remove anchored staged output",
                temporaryName,error);
        }

        internal static LinuxOwnedFinalRollback FromAnchoredStaging(
            int rootFd,int finalDirectoryFd,string rootPath,
            string[] directoryComponents,string temporaryName,
            string finalName,Action<string> afterIdentityVerified)
        {
            if(rootFd<0||finalDirectoryFd<0)
                throw new ArgumentException(
                    "Anchored staging descriptors are required.");
            string directoryPath=rootPath;
            foreach(string component in directoryComponents)
                directoryPath=Path.Combine(directoryPath,component);
            Identity staged=ReadRequiredIdentity(finalDirectoryFd,
                temporaryName,"anchored staged output");
            if((staged.Mode&SIfmt)!=SIfreg)
                throw new IOException(
                    "Anchored staged output is not a regular file.");
            Identity directory=ReadRequiredIdentity(finalDirectoryFd,".",
                "anchored publication directory");
            if((directory.Mode&SIfmt)!=SIfdir)
                throw new IOException(
                    "Anchored publication path is not a directory.");
            ProbeRollbackOperations(finalDirectoryFd);
            return new LinuxOwnedFinalRollback(finalDirectoryFd,
                directoryPath,temporaryName,finalName,staged,directory,
                rootFd,(string[])directoryComponents.Clone(),
                afterIdentityVerified);
        }

        private Identity ReadCurrentDirectoryIdentity()
        {
            if(anchoredRootFd<0)
                return ReadRequiredIdentity(AtFdcwd,directoryPath,
                    "current publication directory");
            int current=Duplicate(anchoredRootFd);
            if(current<0)
                throw NativeFailure("duplicate anchored root",directoryPath);
            try
            {
                foreach(string component in anchoredDirectoryComponents)
                {
                    int next=OpenAt(current,component,
                        ODirectory|ONoFollow|OCloseExec);
                    if(next<0)
                        throw NativeFailure(
                            "open current anchored publication directory",
                            component);
                    Close(current);
                    current=next;
                }
                return ReadRequiredIdentity(current,".",
                    "current anchored publication directory");
            }
            finally
            {
                Close(current);
            }
        }

        private static void ProbeRollbackOperations(int fd)
        {
            if(RenameAt2(fd,"",fd,"",RenameNoReplace)==0)
                throw new IOException(
                    "renameat2 rollback capability probe unexpectedly mutated a path.");
            int error=Marshal.GetLastWin32Error();
            if(error!=ENoEnt)
                throw NativeFailure("probe renameat2 rollback capability",
                    "empty relative path",error);
            if(UnlinkAt(fd,"",0)==0)
                throw new IOException(
                    "unlinkat rollback capability probe unexpectedly mutated a path.");
            error=Marshal.GetLastWin32Error();
            if(error!=ENoEnt)
                throw NativeFailure("probe unlinkat rollback capability",
                    "empty relative path",error);
        }

        internal void RevokeIfOwned(LinuxRollbackQuarantine quarantineRoot)
        {
            EnsureOpen();
            if(quarantineRoot==null)return;
            string quarantine=finalName+".rollback."
                +Guid.NewGuid().ToString("N");
            if(RenameAt2(directoryFd,finalName,
                quarantineRoot.DirectoryFd,quarantine,RenameNoReplace)!=0)
            {
                int error=Marshal.GetLastWin32Error();
                if(error==ENoEnt)return;
                throw NativeFailure("quarantine rollback final",finalName,error);
            }
            Identity candidate=ReadRequiredIdentity(
                quarantineRoot.DirectoryFd,quarantine,
                "quarantined rollback final");
            if(afterIdentityVerified!=null)
                afterIdentityVerified(quarantineRoot.PathFor(quarantine));
            if(!SameRegularFile(stagedIdentity,candidate))
            {
                RestoreQuarantine(quarantineRoot,quarantine);
            }
            // An owned final and every uncertain/replaced quarantine remain
            // under the sibling evidence directory. Linux has no atomic
            // unlink-if-inode-is-still-X primitive; checking and then
            // unlinking would recreate the race this transaction prevents.
        }

        private void RestoreQuarantine(LinuxRollbackQuarantine root,
            string quarantine)
        {
            if(RenameAt2(root.DirectoryFd,quarantine,directoryFd,finalName,
                RenameNoReplace)!=0)
                throw NativeFailure("restore competing rollback final",
                    finalName);
        }

        public void Dispose()
        {
            int fd;
            if(directoryFd>=0)
            {
                fd=directoryFd;
                directoryFd=-1;
                Close(fd);
            }
            if(anchoredRootFd>=0)
            {
                fd=anchoredRootFd;
                anchoredRootFd=-1;
                Close(fd);
            }
        }

        private void EnsureOpen()
        {
            if(directoryFd<0)throw new ObjectDisposedException(
                "LinuxOwnedFinalRollback");
        }

        private static Identity ReadRequiredIdentity(int fd,string name,
            string label)
        {
            Identity value;int error;
            if(!TryReadIdentity(fd,name,out value,out error))
                throw NativeFailure("stat "+label,name,error);
            return value;
        }

        private static bool TryReadIdentity(int fd,string name,
            out Identity identity,out int error)
        {
            IntPtr buffer=Marshal.AllocHGlobal(StatxBufferSize);
            try
            {
                if(Statx(fd,name,AtSymlinkNoFollow,StatxType|StatxIno,
                    buffer)!=0)
                {
                    error=Marshal.GetLastWin32Error();
                    identity=new Identity();
                    return false;
                }
                uint mask=unchecked((uint)Marshal.ReadInt32(buffer,0));
                if((mask&(StatxType|StatxIno))!=(StatxType|StatxIno))
                    throw new IOException(
                        "statx did not return file type and inode identity.");
                identity=new Identity
                {
                    Mode=unchecked((ushort)Marshal.ReadInt16(buffer,28)),
                    Inode=unchecked((ulong)Marshal.ReadInt64(buffer,32)),
                    DeviceMajor=unchecked((uint)Marshal.ReadInt32(buffer,136)),
                    DeviceMinor=unchecked((uint)Marshal.ReadInt32(buffer,140))
                };
                error=0;
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static bool SameRegularFile(Identity expected,Identity actual)
        {
            return (expected.Mode&SIfmt)==SIfreg
                &&(actual.Mode&SIfmt)==SIfreg
                &&expected.DeviceMajor==actual.DeviceMajor
                &&expected.DeviceMinor==actual.DeviceMinor
                &&expected.Inode==actual.Inode;
        }

        private static bool SameDirectory(Identity expected,Identity actual)
        {
            return (expected.Mode&SIfmt)==SIfdir
                &&(actual.Mode&SIfmt)==SIfdir
                &&expected.DeviceMajor==actual.DeviceMajor
                &&expected.DeviceMinor==actual.DeviceMinor
                &&expected.Inode==actual.Inode;
        }

        private static IOException NativeFailure(string operation,string path)
        {return NativeFailure(operation,path,Marshal.GetLastWin32Error());}

        private static IOException NativeFailure(string operation,string path,
            int error)
        {return new IOException(operation+" failed for "+path
            +" with errno "+error+".");}

        [DllImport("libc",EntryPoint="open",CharSet=CharSet.Ansi,
            SetLastError=true)]
        private static extern int Open(string path,int flags);

        [DllImport("libc",EntryPoint="close",SetLastError=true)]
        private static extern int Close(int fd);

        [DllImport("libc",EntryPoint="dup",SetLastError=true)]
        private static extern int Duplicate(int fd);

        [DllImport("libc",EntryPoint="openat",CharSet=CharSet.Ansi,
            SetLastError=true)]
        private static extern int OpenAt(int directoryFd,string path,
            int flags);

        [DllImport("libc",EntryPoint="statx",CharSet=CharSet.Ansi,
            SetLastError=true)]
        private static extern int Statx(int directoryFd,string path,int flags,
            uint mask,IntPtr buffer);

        [DllImport("libc",EntryPoint="renameat2",CharSet=CharSet.Ansi,
            SetLastError=true)]
        private static extern int RenameAt2(int oldDirectoryFd,string oldPath,
            int newDirectoryFd,string newPath,uint flags);

        [DllImport("libc",EntryPoint="linkat",CharSet=CharSet.Ansi,
            SetLastError=true)]
        private static extern int LinkAt(int oldDirectoryFd,string oldPath,
            int newDirectoryFd,string newPath,int flags);

        [DllImport("libc",EntryPoint="unlinkat",CharSet=CharSet.Ansi,
            SetLastError=true)]
        private static extern int UnlinkAt(int directoryFd,string path,
            int flags);
    }

    /// <summary>
    /// Holds the fixture root open while a closed byte set is staged. Every
    /// child directory and temporary is created relative to that descriptor;
    /// a pathname swap can neither redirect a write nor change the directory
    /// used later by linkat(2).
    /// </summary>
    internal sealed class LinuxAnchoredStagingRoot : IDisposable
    {
        private const int EExist = 17;
        private const int EInterrupted = 4;
        private const int OWriteOnly = 0x1;
        private const int OCreate = 0x40;
        private const int OExclusive = 0x80;
        private const int ODirectory = 0x10000;
        private const int ONoFollow = 0x20000;
        private const int OCloseExec = 0x80000;
        private const uint DirectoryMode = 0x1FF;
        private const uint FileMode = 0x180;

        private readonly string rootPath;
        private int rootFd;

        private LinuxAnchoredStagingRoot(int fd,string path)
        {
            rootFd=fd;
            rootPath=path;
        }

        internal static LinuxAnchoredStagingRoot OpenRoot(string path)
        {
            string fullPath=Path.GetFullPath(path);
            Directory.CreateDirectory(fullPath);
            int fd=Open(fullPath,ODirectory|ONoFollow|OCloseExec);
            if(fd<0)throw Failure("open anchored staging root",fullPath);
            return new LinuxAnchoredStagingRoot(fd,fullPath);
        }

        internal LinuxOwnedFinalRollback StageBytes(string relativePath,
            string temporaryName,byte[] content,
            Action<string> afterRollbackIdentityVerified)
        {
            EnsureOpen();
            string[] parts=RelativeParts(relativePath);
            string finalName=parts[parts.Length-1];
            var directoryParts=new string[parts.Length-1];
            Array.Copy(parts,directoryParts,directoryParts.Length);
            int ownedRoot=Duplicate(rootFd);
            if(ownedRoot<0)throw Failure("duplicate anchored staging root",
                rootPath);
            int current=Duplicate(rootFd);
            if(current<0)
            {
                Close(ownedRoot);
                throw Failure("duplicate anchored staging directory",
                    rootPath);
            }
            bool temporaryCreated=false;
            try
            {
                foreach(string component in directoryParts)
                {
                    if(MkdirAt(current,component,DirectoryMode)!=0)
                    {
                        int error=Marshal.GetLastWin32Error();
                        if(error!=EExist)
                            throw Failure("create anchored staging directory",
                                component,error);
                    }
                    int next=OpenAt(current,component,
                        ODirectory|ONoFollow|OCloseExec,0);
                    if(next<0)throw Failure(
                        "open anchored staging directory",component);
                    Close(current);
                    current=next;
                }

                int fileFd=OpenAt(current,temporaryName,
                    OWriteOnly|OCreate|OExclusive|ONoFollow|OCloseExec,
                    FileMode);
                if(fileFd<0)throw Failure("create anchored staged output",
                    temporaryName);
                temporaryCreated=true;
                try
                {
                    WriteAll(fileFd,content,temporaryName);
                    if(Fsync(fileFd)!=0)
                        throw Failure("flush anchored staged output",
                            temporaryName);
                }
                finally
                {
                    Close(fileFd);
                }

                LinuxOwnedFinalRollback authority=
                    LinuxOwnedFinalRollback.FromAnchoredStaging(ownedRoot,
                        current,rootPath,directoryParts,temporaryName,
                        finalName,afterRollbackIdentityVerified);
                ownedRoot=-1;
                current=-1;
                return authority;
            }
            catch
            {
                if(temporaryCreated&&current>=0)
                    UnlinkAt(current,temporaryName,0);
                throw;
            }
            finally
            {
                if(current>=0)Close(current);
                if(ownedRoot>=0)Close(ownedRoot);
            }
        }

        public void Dispose()
        {
            if(rootFd<0)return;
            int fd=rootFd;
            rootFd=-1;
            Close(fd);
        }

        private void EnsureOpen()
        {
            if(rootFd<0)throw new ObjectDisposedException(
                "LinuxAnchoredStagingRoot");
        }

        private static string[] RelativeParts(string path)
        {
            if(string.IsNullOrEmpty(path)||Path.IsPathRooted(path))
                throw new ArgumentException(
                    "Anchored publication file name must be relative.",
                    "path");
            string[] parts=path.Replace('\\','/').Split(
                new[]{'/'},StringSplitOptions.None);
            foreach(string part in parts)
                if(string.IsNullOrEmpty(part)||part=="."||part=="..")
                    throw new ArgumentException(
                        "Anchored publication file name has an unsafe component.",
                        "path");
            return parts;
        }

        private static IOException Failure(string operation,string target)
        {return Failure(operation,target,Marshal.GetLastWin32Error());}

        private static IOException Failure(string operation,string target,
            int error)
        {return new IOException(operation+" failed for "+target
            +" with errno "+error+".");}

        private static void WriteAll(int fd,byte[] content,string target)
        {
            if(content==null)throw new ArgumentNullException("content");
            GCHandle pinned=GCHandle.Alloc(content,GCHandleType.Pinned);
            try
            {
                int offset=0;
                while(offset<content.Length)
                {
                    long written=Write(fd,
                        IntPtr.Add(pinned.AddrOfPinnedObject(),offset),
                        new UIntPtr(unchecked((uint)(content.Length-offset))))
                        .ToInt64();
                    if(written>0)
                    {
                        offset+=checked((int)written);
                        continue;
                    }
                    int error=Marshal.GetLastWin32Error();
                    if(written<0&&error==EInterrupted)continue;
                    throw Failure("write anchored staged output",target,error);
                }
            }
            finally
            {
                pinned.Free();
            }
        }

        [DllImport("libc",EntryPoint="open",CharSet=CharSet.Ansi,
            SetLastError=true)]
        private static extern int Open(string path,int flags);

        [DllImport("libc",EntryPoint="openat",CharSet=CharSet.Ansi,
            SetLastError=true)]
        private static extern int OpenAt(int directoryFd,string path,
            int flags,uint mode);

        [DllImport("libc",EntryPoint="mkdirat",CharSet=CharSet.Ansi,
            SetLastError=true)]
        private static extern int MkdirAt(int directoryFd,string path,
            uint mode);

        [DllImport("libc",EntryPoint="unlinkat",CharSet=CharSet.Ansi,
            SetLastError=true)]
        private static extern int UnlinkAt(int directoryFd,string path,
            int flags);

        [DllImport("libc",EntryPoint="write",SetLastError=true)]
        private static extern IntPtr Write(int fd,IntPtr buffer,
            UIntPtr count);

        [DllImport("libc",EntryPoint="fsync",SetLastError=true)]
        private static extern int Fsync(int fd);

        [DllImport("libc",EntryPoint="dup",SetLastError=true)]
        private static extern int Duplicate(int fd);

        [DllImport("libc",EntryPoint="close",SetLastError=true)]
        private static extern int Close(int fd);
    }

    /// <summary>
    /// A failure-only sibling directory that retains revoked publisher bytes
    /// and any entry whose ownership becomes uncertain. It is intentionally
    /// not removed by the publication transaction: deleting a pathname after
    /// an identity check would allow another writer to substitute its bytes.
    /// </summary>
    internal sealed class LinuxRollbackQuarantine : IDisposable
    {
        private const int ODirectory = 0x10000;
        private const int ONoFollow = 0x20000;
        private const int OCloseExec = 0x80000;
        private const uint OwnerOnlyDirectoryMode = 0x1C0;

        private readonly string path;
        private int directoryFd;

        private LinuxRollbackQuarantine(int fd,string value)
        {
            directoryFd=fd;
            path=value;
        }

        internal int DirectoryFd
        {
            get
            {
                if(directoryFd<0)throw new ObjectDisposedException(
                    "LinuxRollbackQuarantine");
                return directoryFd;
            }
        }

        internal static LinuxRollbackQuarantine Create(
            string outputDirectory)
        {
            string output=Path.GetFullPath(outputDirectory);
            string parent=Path.GetDirectoryName(output);
            if(string.IsNullOrEmpty(parent))
                throw new IOException(
                    "Publication output has no quarantine parent directory.");
            int parentFd=Open(parent,ODirectory|ONoFollow|OCloseExec);
            if(parentFd<0)throw Failure("open quarantine parent",parent);
            string outputName=Path.GetFileName(output);
            if(string.IsNullOrEmpty(outputName))outputName="publication";
            string name="."+outputName+".rollback."
                +Guid.NewGuid().ToString("N");
            try
            {
                if(MkdirAt(parentFd,name,OwnerOnlyDirectoryMode)!=0)
                    throw Failure("create rollback quarantine",name);
                int fd=OpenAt(parentFd,name,
                    ODirectory|ONoFollow|OCloseExec);
                if(fd<0)throw Failure("open rollback quarantine",name);
                return new LinuxRollbackQuarantine(fd,
                    Path.Combine(parent,name));
            }
            finally
            {
                Close(parentFd);
            }
        }

        internal string PathFor(string name)
        {
            return Path.Combine(path,name);
        }

        public void Dispose()
        {
            if(directoryFd<0)return;
            int fd=directoryFd;
            directoryFd=-1;
            Close(fd);
        }

        private static IOException Failure(string operation,string target)
        {
            return new IOException(operation+" failed for "+target
                +" with errno "+Marshal.GetLastWin32Error()+".");
        }

        [DllImport("libc",EntryPoint="open",CharSet=CharSet.Ansi,
            SetLastError=true)]
        private static extern int Open(string value,int flags);

        [DllImport("libc",EntryPoint="openat",CharSet=CharSet.Ansi,
            SetLastError=true)]
        private static extern int OpenAt(int directoryFd,string value,
            int flags);

        [DllImport("libc",EntryPoint="mkdirat",CharSet=CharSet.Ansi,
            SetLastError=true)]
        private static extern int MkdirAt(int directoryFd,string value,
            uint mode);

        [DllImport("libc",EntryPoint="close",SetLastError=true)]
        private static extern int Close(int fd);
    }

    public sealed class NoReplacePublisher
    {
        private const string SmokeFileName = "smoke.csv";

        private readonly ILinkOperation linkOperation;
        private readonly Action<string> deleteFile;
        private readonly TracePayloadCompressor compressor;
        private readonly Action<string> afterRollbackIdentityVerified;

        public NoReplacePublisher()
            : this(LibcLinkOperation.Instance, File.Delete, null, null)
        {
        }

        /// <summary>
        /// Publishes with trace payload compression folded into the
        /// publication step (null = no compression, the default). See
        /// <see cref="TracePayloadCompressor"/>.
        /// </summary>
        public NoReplacePublisher(TracePayloadCompressor compressor)
            : this(LibcLinkOperation.Instance, File.Delete, compressor, null)
        {
        }

        internal NoReplacePublisher(ILinkOperation linkOperation)
            : this(linkOperation, File.Delete, null, null)
        {
        }

        internal NoReplacePublisher(
            ILinkOperation linkOperation,
            Action<string> deleteFile)
            : this(linkOperation, deleteFile, null, null)
        {
        }

        internal NoReplacePublisher(
            ILinkOperation linkOperation,
            Action<string> deleteFile,
            TracePayloadCompressor compressor)
            : this(linkOperation, deleteFile, compressor, null)
        {
        }

        internal NoReplacePublisher(
            ILinkOperation linkOperation,
            Action<string> deleteFile,
            TracePayloadCompressor compressor,
            Action<string> afterRollbackIdentityVerified)
        {
            if (linkOperation == null)
            {
                throw new ArgumentNullException("linkOperation");
            }
            if (deleteFile == null)
            {
                throw new ArgumentNullException("deleteFile");
            }
            this.linkOperation = linkOperation;
            this.deleteFile = deleteFile;
            this.compressor = compressor;
            this.afterRollbackIdentityVerified =
                afterRollbackIdentityVerified;
        }

        /// <summary>
        /// The payload compressor this publisher applies at publication
        /// time, or null when compression is off. The CLI reads its report
        /// after publication commits.
        /// </summary>
        internal TracePayloadCompressor Compressor
        {
            get { return compressor; }
        }

        public void Publish(
            string outputDirectory,
            Action<TextWriter> write)
        {
            using (StagedPublication staged = Stage(
                outputDirectory,
                write))
            {
                staged.Publish();
            }
        }

        internal StagedPublication Stage(
            string outputDirectory,
            Action<TextWriter> write)
        {
            if (write == null)
            {
                throw new ArgumentNullException("write");
            }
            return StageAll(
                outputDirectory,
                new[] { SmokeFileName },
                writers => write(writers[0])).DetachSingle();
        }

        /// <summary>
        /// Stages one temporary file per name in
        /// <paramref name="fileNames"/> (all writers open at once, in the
        /// same order), then returns a set whose Publish() links every file
        /// into place with the same no-replace link(2) semantics as the
        /// single-file path. Names may carry relative subdirectory paths
        /// (e.g. "seg1_ehz1/physics.csv"); each parent directory is created
        /// at staging time and every temporary lives next to its final so
        /// link(2) never crosses a filesystem boundary. On any staging
        /// failure all temporaries are removed and no final path is
        /// touched; on a partial multi-file publication failure the
        /// already-linked finals are revoked (no partial finals).
        /// </summary>
        internal StagedPublicationSet StageAll(
            string outputDirectory,
            string[] fileNames,
            Action<TextWriter[]> write)
        {
            return StageAll(outputDirectory, fileNames, null, write);
        }

        /// <summary>
        /// Binary counterpart to StageAll for already-normalized fixture
        /// payloads such as deterministic gzip members. All bytes stage
        /// before the returned set may publish any final path.
        /// </summary>
        internal StagedPublicationSet StageAllBytes(
            string outputDirectory, string[] fileNames, byte[][] contents)
        {
            if(fileNames==null||contents==null||fileNames.Length==0
                ||fileNames.Length!=contents.Length)
                throw new ArgumentException(
                    "Binary publication names and contents must have equal nonzero length.");
            if(compressor!=null)throw new InvalidOperationException(
                "Closed binary fixture publication does not support payload compression.");
            string fullOutputDirectory=Path.GetFullPath(outputDirectory);
            var staged=new StagedPublication[fileNames.Length];
            try
            {
                using(LinuxAnchoredStagingRoot root=
                    LinuxAnchoredStagingRoot.OpenRoot(fullOutputDirectory))
                {
                    for(int index=0;index<fileNames.Length;index++)
                    {
                        string finalPath=Path.Combine(fullOutputDirectory,
                            fileNames[index]);
                        string temporaryName=CreateTemporaryName(
                            Path.GetFileName(fileNames[index]));
                        string temporaryPath=Path.Combine(
                            Path.GetDirectoryName(finalPath),temporaryName);
                        LinuxOwnedFinalRollback authority=root.StageBytes(
                            fileNames[index],temporaryName,contents[index],
                            afterRollbackIdentityVerified);
                        staged[index]=new StagedPublication(temporaryPath,
                            finalPath,linkOperation,deleteFile,
                            afterRollbackIdentityVerified,authority);
                    }
                }
                return new StagedPublicationSet(staged,null,
                    fullOutputDirectory);
            }
            catch
            {
                foreach(StagedPublication file in staged)
                    if(file!=null)file.Dispose();
                throw;
            }
        }

        /// <summary>
        /// As <see cref="StageAll(string,string[],Action{TextWriter[]})"/>,
        /// with a tail of CONDITIONAL file names. Their writers arrive
        /// after the unconditional ones in the same array, but each opens
        /// its temporary only on the first character written, and a
        /// conditional file nothing was written to is neither staged nor
        /// published — no 0-byte file joins the output inventory.
        ///
        /// That is what lets a capture carry an optional sidecar (the S1
        /// PLC hardware-timing stream) while every capture that produces
        /// no such record keeps its exact existing inventory.
        /// </summary>
        internal StagedPublicationSet StageAll(
            string outputDirectory,
            string[] fileNames,
            string[] conditionalFileNames,
            Action<TextWriter[]> write)
        {
            if (string.IsNullOrEmpty(outputDirectory))
            {
                throw new ArgumentException(
                    "An output directory is required.",
                    "outputDirectory");
            }
            if (fileNames == null || fileNames.Length == 0)
            {
                throw new ArgumentException(
                    "At least one output file name is required.",
                    "fileNames");
            }
            if (write == null)
            {
                throw new ArgumentNullException("write");
            }

            string[] conditional = conditionalFileNames
                ?? new string[0];
            string fullOutputDirectory =
                Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(fullOutputDirectory);
            int total = fileNames.Length + conditional.Length;
            var temporaryPaths = new string[total];
            var staged = new StagedPublication[total];
            for (var index = 0; index < total; index++)
            {
                string name = index < fileNames.Length
                    ? fileNames[index]
                    : conditional[index - fileNames.Length];
                string finalPath = Path.Combine(
                    fullOutputDirectory,
                    name);
                string finalDirectory = Path.GetDirectoryName(finalPath);
                Directory.CreateDirectory(finalDirectory);
                temporaryPaths[index] = Path.Combine(
                    finalDirectory,
                    CreateTemporaryName(Path.GetFileName(name)));
                staged[index] = new StagedPublication(
                    temporaryPaths[index],
                    finalPath,
                    linkOperation,
                    deleteFile,
                    afterRollbackIdentityVerified);
            }

            try
            {
                bool[] opened = WriteTemporaryFiles(
                    temporaryPaths, fileNames.Length, write);
                return new StagedPublicationSet(
                    SelectStaged(staged, opened), compressor,
                    fullOutputDirectory);
            }
            catch
            {
                foreach (string temporaryPath in temporaryPaths)
                {
                    try
                    {
                        deleteFile(temporaryPath);
                    }
                    catch (Exception)
                    {
                        // Preserve the staging failure. A leftover temporary
                        // file is safer than obscuring why capture failed.
                    }
                }
                throw;
            }
        }

        /// <summary>
        /// Opens an incremental staging session for a publication whose
        /// file set is discovered while capture runs (the multi-segment
        /// complete-run layout: each finalized segment stages its files as
        /// the capture streams them, so a 19-segment pass never buffers
        /// more than one segment's contents). Files staged through the
        /// session carry the same guarantees as <see cref="StageAll"/>:
        /// temporaries live next to their finals, nothing lands under a
        /// final name before the returned set's Publish(), and a partial
        /// publication failure revokes every already-linked final.
        /// </summary>
        internal IncrementalStagingSession OpenSession(
            string outputDirectory)
        {
            if (string.IsNullOrEmpty(outputDirectory))
            {
                throw new ArgumentException(
                    "An output directory is required.",
                    "outputDirectory");
            }
            string fullOutputDirectory =
                Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(fullOutputDirectory);
            return new IncrementalStagingSession(
                fullOutputDirectory,
                linkOperation,
                deleteFile,
                compressor,
                afterRollbackIdentityVerified);
        }

        internal sealed class IncrementalStagingSession : IDisposable
        {
            private readonly string outputDirectory;
            private readonly ILinkOperation linkOperation;
            private readonly Action<string> deleteFile;
            private readonly TracePayloadCompressor compressor;
            private readonly Action<string> afterRollbackIdentityVerified;
            private readonly List<StagedPublication> staged =
                new List<StagedPublication>();
            private readonly List<StagedStream> open =
                new List<StagedStream>();
            private bool finished;

            internal IncrementalStagingSession(
                string outputDirectory,
                ILinkOperation linkOperation,
                Action<string> deleteFile,
                TracePayloadCompressor compressor,
                Action<string> afterRollbackIdentityVerified)
            {
                this.outputDirectory = outputDirectory;
                this.linkOperation = linkOperation;
                this.deleteFile = deleteFile;
                this.compressor = compressor;
                this.afterRollbackIdentityVerified =
                    afterRollbackIdentityVerified;
            }

            /// <summary>
            /// Stages <paramref name="content"/> under the relative
            /// <paramref name="fileName"/> (which may carry subdirectory
            /// components, e.g. "ghz1/physics.csv"). The bytes are written
            /// verbatim as BOM-free UTF-8 — any line-ending policy is the
            /// caller's, applied to the content before staging. On a write
            /// failure the file's temporary is removed and the exception
            /// propagates; earlier staged files stay staged so the owner's
            /// Dispose() can discard them together.
            /// </summary>
            public void StageFile(string fileName, string content)
            {
                if (finished)
                {
                    throw new InvalidOperationException(
                        "The staging session is already finalized.");
                }
                if (string.IsNullOrEmpty(fileName))
                {
                    throw new ArgumentException(
                        "An output file name is required.",
                        "fileName");
                }
                if (content == null)
                {
                    throw new ArgumentNullException("content");
                }

                string finalPath = Path.Combine(outputDirectory, fileName);
                string finalDirectory = Path.GetDirectoryName(finalPath);
                Directory.CreateDirectory(finalDirectory);
                string temporaryPath = Path.Combine(
                    finalDirectory,
                    CreateTemporaryName(Path.GetFileName(fileName)));
                try
                {
                    using (var stream = new FileStream(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None))
                    {
                        using (var writer = new StreamWriter(
                            stream,
                            new UTF8Encoding(false),
                            1024,
                            true))
                        {
                            writer.NewLine = "\n";
                            writer.Write(content);
                            writer.Flush();
                        }
                        stream.Flush(true);
                    }
                }
                catch
                {
                    try
                    {
                        deleteFile(temporaryPath);
                    }
                    catch (Exception)
                    {
                        // Preserve the staging failure. A leftover
                        // temporary file is safer than obscuring why
                        // capture failed.
                    }
                    throw;
                }
                staged.Add(new StagedPublication(
                    temporaryPath,
                    finalPath,
                    linkOperation,
                    deleteFile,
                    afterRollbackIdentityVerified));
            }

            internal void StageBytes(string fileName,byte[] content)
            {
                if(finished)throw new InvalidOperationException(
                    "The staging session is already finalized.");
                if(string.IsNullOrEmpty(fileName))throw new ArgumentException(
                    "An output file name is required.","fileName");
                if(content==null)throw new ArgumentNullException("content");
                string finalPath=Path.Combine(outputDirectory,fileName);
                string finalDirectory=Path.GetDirectoryName(finalPath);
                Directory.CreateDirectory(finalDirectory);
                string temporaryPath=Path.Combine(finalDirectory,
                    CreateTemporaryName(Path.GetFileName(fileName)));
                try
                {
                    using(var stream=new FileStream(temporaryPath,FileMode.CreateNew,
                        FileAccess.Write,FileShare.None))
                    {stream.Write(content,0,content.Length);stream.Flush(true);}
                }
                catch
                {
                    try{deleteFile(temporaryPath);}catch(Exception){}
                    throw;
                }
                staged.Add(new StagedPublication(temporaryPath,finalPath,
                    linkOperation,deleteFile,
                    afterRollbackIdentityVerified));
            }

            /// <summary>
            /// Opens a staged file for INCREMENTAL writing, for output too
            /// large to hold as a string: the caller writes through the
            /// returned stream's <see cref="StagedStream.Writer"/> as the
            /// capture produces it and calls
            /// <see cref="StagedStream.Complete"/> when the file is done.
            /// Only then does the file join the session's publication set.
            ///
            /// This exists because the S3K complete-run pass produces
            /// ~1.4 GB of aux_state.jsonl across its segments, with a
            /// single 266 MB segment; routing that through
            /// <see cref="StageFile(string,string)"/> would hold the whole
            /// segment as a .NET string (two bytes per ASCII char) and then
            /// copy it. Streaming keeps peak footprint at the OS write
            /// buffer.
            ///
            /// Guarantees match <see cref="StageFile(string,string)"/>: the
            /// temporary lives next to its final so link(2) never crosses a
            /// filesystem, nothing lands under a final name before the
            /// completed set's Publish(), and an abandoned stream (Dispose
            /// without Complete) removes its temporary and never publishes.
            /// Bytes are written verbatim as BOM-free UTF-8 with LF
            /// newlines; any line-ending policy is the caller's.
            ///
            /// When the session carries a compressor, a payload streamed
            /// this way is gzipped ON THE WAY to its temporary rather than
            /// afterwards, so the staged bytes never include the
            /// uncompressed form — see <see cref="StagedStream"/>.
            /// </summary>
            public StagedStream OpenFile(string fileName)
            {
                if (finished)
                {
                    throw new InvalidOperationException(
                        "The staging session is already finalized.");
                }
                if (string.IsNullOrEmpty(fileName))
                {
                    throw new ArgumentException(
                        "An output file name is required.",
                        "fileName");
                }

                string finalPath = Path.Combine(outputDirectory, fileName);
                string finalDirectory = Path.GetDirectoryName(finalPath);
                Directory.CreateDirectory(finalDirectory);
                string temporaryPath = Path.Combine(
                    finalDirectory,
                    CreateTemporaryName(Path.GetFileName(fileName)));
                var stream = new StagedStream(
                    this, temporaryPath, finalPath, compressor);
                open.Add(stream);
                return stream;
            }

            internal void CompleteStream(
                StagedStream stream, string temporaryPath, string finalPath)
            {
                open.Remove(stream);
                staged.Add(new StagedPublication(
                    temporaryPath,
                    finalPath,
                    linkOperation,
                    deleteFile,
                    afterRollbackIdentityVerified));
            }

            internal void AbandonStream(StagedStream stream)
            {
                open.Remove(stream);
            }

            internal void DeleteTemporary(string temporaryPath)
            {
                try
                {
                    deleteFile(temporaryPath);
                }
                catch (Exception)
                {
                    // Preserve the staging failure. A leftover temporary
                    // file is safer than obscuring why capture failed.
                }
            }

            /// <summary>
            /// Finishes staging and hands the accumulated files over as one
            /// all-or-nothing publication set (possibly empty, whose
            /// Publish() is then a no-op). After Complete() the session
            /// itself owns nothing — dispose the returned set instead. Any
            /// stream still open is a caller bug: it is discarded rather
            /// than half-published.
            /// </summary>
            public StagedPublicationSet Complete()
            {
                if (finished)
                {
                    throw new InvalidOperationException(
                        "The staging session is already finalized.");
                }
                if (open.Count != 0)
                {
                    int unfinished = open.Count;
                    DiscardOpenStreams();
                    throw new InvalidOperationException(
                        "The staging session still has "
                        + unfinished.ToString(
                            System.Globalization.CultureInfo.InvariantCulture)
                        + " unfinished streamed file(s).");
                }
                finished = true;
                return new StagedPublicationSet(
                    staged.ToArray(),
                    compressor,
                    outputDirectory);
            }

            public void Dispose()
            {
                if (finished)
                {
                    return;
                }
                finished = true;
                DiscardOpenStreams();
                foreach (StagedPublication file in staged)
                {
                    file.Dispose();
                }
            }

            private void DiscardOpenStreams()
            {
                var pending = open.ToArray();
                foreach (StagedStream stream in pending)
                {
                    stream.Dispose();
                }
                open.Clear();
            }
        }

        /// <summary>
        /// One incrementally written staged file (see
        /// <see cref="IncrementalStagingSession.OpenFile"/>). Write through
        /// <see cref="Writer"/>, then call <see cref="Complete"/> exactly
        /// once; disposing without completing removes the temporary and
        /// publishes nothing.
        ///
        /// When the session carries a compressor and this file is a trace
        /// payload, the bytes are written THROUGH a gzip stream into a .gz
        /// temporary and the uncompressed form is never materialised at all:
        /// a complete-run capture stages ~a tenth of the bytes it used to.
        /// The verify-before-destroy guarantee is preserved exactly, not
        /// weakened — the plaintext is SHA-256'd and counted on its way into
        /// the compressor, and at Complete() the finished gzip is
        /// decompressed and compared against those values before the file
        /// joins the publication set (<see cref="ResolveCompressedFile"/>).
        /// A payload that turns out to be below the compressor's threshold
        /// is expanded back to its plain name by that same verifying
        /// decompression, so the threshold semantics are unchanged.
        /// </summary>
        internal sealed class StagedStream : IDisposable
        {
            private readonly IncrementalStagingSession owner;
            private readonly TracePayloadCompressor compressor;
            private readonly string finalPath;
            private readonly string stagingPath;
            private readonly FileStream stream;
            private readonly StreamWriter writer;
            private readonly TracePayloadCompressor.StreamingPayload payload;
            private string temporaryPath;
            private string publishedFinalPath;
            private bool finished;

            internal StagedStream(
                IncrementalStagingSession owner,
                string temporaryPath,
                string finalPath,
                TracePayloadCompressor compressor)
            {
                this.owner = owner;
                this.temporaryPath = temporaryPath;
                this.finalPath = finalPath;
                publishedFinalPath = finalPath;
                this.compressor = compressor != null
                    && TracePayloadCompressor.IsTracePayloadName(
                        Path.GetFileName(finalPath))
                    ? compressor
                    : null;
                stagingPath = this.compressor == null
                    ? temporaryPath
                    : temporaryPath + TracePayloadCompressor.GzipExtension;
                try
                {
                    stream = new FileStream(
                        stagingPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None);
                    Stream sink = stream;
                    if (this.compressor != null)
                    {
                        payload = this.compressor.BeginStreaming(stream);
                        sink = payload.PlaintextStream;
                    }
                    writer = new StreamWriter(
                        sink,
                        new UTF8Encoding(false),
                        64 * 1024,
                        true);
                    writer.NewLine = "\n";
                }
                catch
                {
                    TryDispose(writer);
                    TryDispose(payload);
                    TryDispose(stream);
                    owner.DeleteTemporary(stagingPath);
                    throw;
                }
            }

            public TextWriter Writer
            {
                get
                {
                    if (finished)
                    {
                        throw new InvalidOperationException(
                            "The staged stream is already finalized.");
                    }
                    return writer;
                }
            }

            /// <summary>
            /// Flushes and closes the file, then registers it with the
            /// session so the session's own Complete() includes it in the
            /// all-or-nothing publication set.
            /// </summary>
            public void Complete()
            {
                if (finished)
                {
                    throw new InvalidOperationException(
                        "The staged stream is already finalized.");
                }
                finished = true;
                try
                {
                    writer.Flush();
                    if (payload != null)
                    {
                        // Closes the deflate stream into the file; nothing
                        // is flushed through the compressor before this, so
                        // the container bytes do not depend on the caller's
                        // write pattern.
                        payload.Finish();
                    }
                    stream.Flush(true);
                }
                finally
                {
                    TryDispose(writer);
                    TryDispose(stream);
                }
                if (payload != null)
                {
                    try
                    {
                        ResolveCompressedFile();
                    }
                    catch
                    {
                        owner.AbandonStream(this);
                        owner.DeleteTemporary(stagingPath);
                        throw;
                    }
                }
                owner.CompleteStream(
                    this, temporaryPath, publishedFinalPath);
            }

            public void Dispose()
            {
                if (finished)
                {
                    return;
                }
                finished = true;
                TryDispose(writer);
                TryDispose(payload);
                TryDispose(stream);
                owner.AbandonStream(this);
                owner.DeleteTemporary(stagingPath);
            }

            /// <summary>
            /// Verifies the finished gzip against the plaintext digest taken
            /// while writing, then decides which file publishes: the gzip
            /// itself at or above the compressor's threshold (final name
            /// gaining ".gz"), or the payload expanded back to its plain
            /// name below it — the same threshold rule the bulk path
            /// applies, evaluated on the plaintext length the digest
            /// carries. Either way exactly one temporary survives.
            /// </summary>
            private void ResolveCompressedFile()
            {
                if (payload.PlaintextLength >= compressor.ThresholdBytes)
                {
                    compressor.VerifyStreamedGzip(
                        stagingPath,
                        null,
                        payload.PlaintextLength,
                        payload.PlaintextHash);
                    compressor.RecordCompressed(
                        finalPath,
                        payload.PlaintextLength,
                        new FileInfo(stagingPath).Length);
                    temporaryPath = stagingPath;
                    publishedFinalPath = finalPath
                        + TracePayloadCompressor.GzipExtension;
                    return;
                }

                compressor.VerifyStreamedGzip(
                    stagingPath,
                    temporaryPath,
                    payload.PlaintextLength,
                    payload.PlaintextHash);
                publishedFinalPath = finalPath;
                owner.DeleteTemporary(stagingPath);
            }
        }

        /// <summary>
        /// Keeps every unconditional staged file plus the conditional ones
        /// that were actually written to. An unwritten conditional file has
        /// no temporary on disk either, so dropping it here is the whole of
        /// "publishes no file at all".
        /// </summary>
        private static StagedPublication[] SelectStaged(
            StagedPublication[] staged,
            bool[] opened)
        {
            var kept = new List<StagedPublication>(staged.Length);
            for (var index = 0; index < staged.Length; index++)
            {
                if (opened[index])
                {
                    kept.Add(staged[index]);
                }
            }
            return kept.ToArray();
        }

        /// <summary>
        /// Opens the first <paramref name="unconditionalCount"/> temporary
        /// files eagerly and the remainder lazily, runs the capture, and
        /// reports which temporaries exist. Only a lazy file can report
        /// false, and only when nothing was written to it.
        /// </summary>
        private static bool[] WriteTemporaryFiles(
            string[] temporaryPaths,
            int unconditionalCount,
            Action<TextWriter[]> write)
        {
            var streams = new FileStream[temporaryPaths.Length];
            var writers = new StreamWriter[temporaryPaths.Length];
            var handed = new TextWriter[temporaryPaths.Length];
            var lazy = new LazyOpenTextWriter[temporaryPaths.Length];
            var opened = new bool[temporaryPaths.Length];
            try
            {
                for (var index = 0; index < temporaryPaths.Length; index++)
                {
                    if (index < unconditionalCount)
                    {
                        writers[index] = OpenTemporaryWriter(
                            temporaryPaths, streams, index);
                        handed[index] = writers[index];
                        opened[index] = true;
                        continue;
                    }
                    int slot = index;
                    lazy[index] = new LazyOpenTextWriter(() =>
                    {
                        writers[slot] = OpenTemporaryWriter(
                            temporaryPaths, streams, slot);
                        opened[slot] = true;
                        return writers[slot];
                    });
                    handed[index] = lazy[index];
                }

                write(handed);

                for (var index = 0; index < temporaryPaths.Length; index++)
                {
                    if (!opened[index])
                    {
                        continue;
                    }
                    writers[index].Flush();
                    streams[index].Flush(true);
                }
                return opened;
            }
            finally
            {
                for (var index = 0; index < temporaryPaths.Length; index++)
                {
                    TryDispose(writers[index]);
                    TryDispose(streams[index]);
                }
            }
        }

        private static StreamWriter OpenTemporaryWriter(
            string[] temporaryPaths,
            FileStream[] streams,
            int index)
        {
            streams[index] = new FileStream(
                temporaryPaths[index],
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            var writer = new StreamWriter(
                streams[index],
                new UTF8Encoding(false),
                1024,
                true);
            writer.NewLine = "\n";
            return writer;
        }

        private static void TryDispose(IDisposable disposable)
        {
            if (disposable == null)
            {
                return;
            }
            try
            {
                disposable.Dispose();
            }
            catch (Exception)
            {
                // Content durability is established by the explicit flushes
                // on the success path; a disposal failure must not mask the
                // original staging exception on the failure path.
            }
        }

        internal sealed class StagedPublication : IDisposable
        {
            private readonly ILinkOperation linkOperation;
            private readonly Action<string> deleteFile;
            private readonly Action<string> afterRollbackIdentityVerified;
            private string temporaryPath;
            private string finalPath;
            private LinuxOwnedFinalRollback retainedRollback;
            private bool linked;
            private bool finished;

            internal StagedPublication(
                string temporaryPath,
                string finalPath,
                ILinkOperation linkOperation,
                Action<string> deleteFile,
                Action<string> afterRollbackIdentityVerified)
            {
                this.temporaryPath = temporaryPath;
                this.finalPath = finalPath;
                this.linkOperation = linkOperation;
                this.deleteFile = deleteFile;
                this.afterRollbackIdentityVerified =
                    afterRollbackIdentityVerified;
            }

            internal StagedPublication(
                string temporaryPath,
                string finalPath,
                ILinkOperation linkOperation,
                Action<string> deleteFile,
                Action<string> afterRollbackIdentityVerified,
                LinuxOwnedFinalRollback anchoredRollback)
                : this(temporaryPath,finalPath,linkOperation,deleteFile,
                    afterRollbackIdentityVerified)
            {
                if(anchoredRollback==null)
                    throw new ArgumentNullException("anchoredRollback");
                retainedRollback=anchoredRollback;
            }

            /// <summary>
            /// Replaces this staged file with its gzip before publication,
            /// when it is a trace payload at or above the compressor's
            /// threshold: the gzip is written next to the staged temporary,
            /// verified by decompress-and-hash against it, and only then
            /// adopted (final path gaining ".gz") and the uncompressed
            /// temporary discarded. A verification failure throws with the
            /// uncompressed temporary still staged, so the caller's rollback
            /// publishes nothing.
            /// </summary>
            internal void CompressPayload(TracePayloadCompressor compressor)
            {
                if (finished)
                {
                    throw new InvalidOperationException(
                        "The staged output is already finalized.");
                }

                string fileName = Path.GetFileName(finalPath);
                if (!TracePayloadCompressor.IsTracePayloadName(fileName))
                {
                    return;
                }

                long sourceLength = new FileInfo(temporaryPath).Length;
                if (!compressor.ShouldCompress(fileName, sourceLength))
                {
                    return;
                }

                string compressedTemporary =
                    temporaryPath + TracePayloadCompressor.GzipExtension;
                compressor.CompressAndVerify(
                    temporaryPath,
                    compressedTemporary);

                string uncompressedTemporary = temporaryPath;
                temporaryPath = compressedTemporary;
                compressor.RecordCompressed(
                    finalPath,
                    sourceLength,
                    new FileInfo(compressedTemporary).Length);
                finalPath += TracePayloadCompressor.GzipExtension;
                try
                {
                    deleteFile(uncompressedTemporary);
                }
                catch (Exception)
                {
                    // The verified gzip is the file that publishes; failing
                    // to remove its source must not fail the capture.
                }
            }

            public void Publish()
            {
                if (finished)
                {
                    throw new InvalidOperationException(
                        "The staged output is already finalized.");
                }

                LinuxOwnedFinalRollback authority=
                    LinuxOwnedFinalRollback.Prepare(temporaryPath,finalPath,
                        afterRollbackIdentityVerified);
                try
                {
                    linkOperation.Create(temporaryPath,finalPath,
                        authority.LinkNoReplace);
                    linked=true;
                    finished=true;
                    TryDeleteTemporary();
                }
                finally
                {
                    authority.Dispose();
                }
            }

            internal void PublishRetainingRollbackIdentity()
            {
                if(finished||linked)
                    throw new InvalidOperationException(
                        "The staged output is already finalized.");
                if(retainedRollback==null)
                    retainedRollback=LinuxOwnedFinalRollback.Prepare(
                        temporaryPath,finalPath,
                        afterRollbackIdentityVerified);
                linkOperation.Create(temporaryPath,finalPath,
                    retainedRollback.LinkNoReplace);
                linked=true;
            }

            internal void CommitRetainedPublication()
            {
                if(finished||!linked||retainedRollback==null)
                    throw new InvalidOperationException(
                        "The retained publication is not ready to commit.");
                finished=true;
                try
                {
                    TryDeleteTemporary();
                }
                finally
                {
                    retainedRollback.Dispose();
                    retainedRollback=null;
                }
            }

            public void Dispose()
            {
                if (finished)
                {
                    return;
                }
                finished = true;
                try
                {
                    TryDeleteTemporary();
                }
                finally
                {
                    if(retainedRollback!=null)
                        retainedRollback.Dispose();
                    retainedRollback=null;
                }
            }

            internal void RevokeFinal(
                LinuxRollbackQuarantine quarantineRoot)
            {
                if(finished)return;
                finished=true;
                try
                {
                    if(linked&&retainedRollback!=null)
                        retainedRollback.RevokeIfOwned(quarantineRoot);
                }
                catch (Exception)
                {
                    // Fail closed: loss of inode authority must never fall
                    // back to deleting a pathname that another writer may
                    // now own. The original publication failure is reported.
                }
                finally
                {
                    TryDeleteTemporary();
                    if(retainedRollback!=null)retainedRollback.Dispose();
                    retainedRollback=null;
                }
            }

            private void TryDeleteTemporary()
            {
                try
                {
                    if(retainedRollback!=null)
                        retainedRollback.RemoveTemporary();
                    else
                        deleteFile(temporaryPath);
                }
                catch (Exception)
                {
                    // Publication is the final commit point. Cleanup cannot
                    // turn a committed result into a reported failure.
                }
            }
        }

        internal sealed class StagedPublicationSet : IDisposable
        {
            private readonly StagedPublication[] staged;
            private readonly TracePayloadCompressor compressor;
            private readonly string outputDirectory;
            private bool finished;

            internal StagedPublicationSet(
                StagedPublication[] staged,
                TracePayloadCompressor compressor,
                string outputDirectory)
            {
                this.staged = staged;
                this.compressor = compressor;
                this.outputDirectory = outputDirectory;
            }

            /// <summary>
            /// Hands the single staged file over on the smoke path, which
            /// publishes without the set and therefore without compression —
            /// smoke.csv is not a trace payload name in any case.
            /// </summary>
            internal StagedPublication DetachSingle()
            {
                if (staged.Length != 1)
                {
                    throw new InvalidOperationException(
                        "DetachSingle requires exactly one staged file.");
                }
                finished = true;
                return staged[0];
            }

            public void Publish()
            {
                if (finished)
                {
                    throw new InvalidOperationException(
                        "The staged outputs are already finalized.");
                }
                finished = true;

                var publishedCount = 0;
                try
                {
                    // Compression is part of the publication, not a step
                    // bolted on after it: every payload is compressed and
                    // verified before the first link(2), so a verification
                    // failure lands in the rollback below with no final
                    // touched at all.
                    if (compressor != null)
                    {
                        foreach (StagedPublication file in staged)
                        {
                            file.CompressPayload(compressor);
                        }
                    }

                    while (publishedCount < staged.Length)
                    {
                        staged[publishedCount]
                            .PublishRetainingRollbackIdentity();
                        publishedCount++;
                    }
                    foreach(StagedPublication file in staged)
                        file.CommitRetainedPublication();
                }
                catch
                {
                    // No partial finals: revoke the files that already
                    // linked and discard the unpublished temporaries so a
                    // failed multi-file publication leaves behind only a
                    // competing writer's own files.
                    LinuxRollbackQuarantine quarantineRoot=null;
                    try
                    {
                        if(publishedCount>0)
                        {
                            try
                            {
                                quarantineRoot=
                                    LinuxRollbackQuarantine.Create(
                                        outputDirectory);
                            }
                            catch(Exception)
                            {
                                // Without a private anchored destination,
                                // rollback leaves finals in place rather
                                // than risking another writer's bytes.
                            }
                        }
                        for (var index = 0; index < staged.Length; index++)
                        {
                            if (index < publishedCount)
                            {
                                staged[index].RevokeFinal(quarantineRoot);
                            }
                            else
                            {
                                staged[index].Dispose();
                            }
                        }
                    }
                    finally
                    {
                        if(quarantineRoot!=null)quarantineRoot.Dispose();
                    }
                    throw;
                }
            }

            public void Dispose()
            {
                if (finished)
                {
                    return;
                }
                finished = true;
                foreach (StagedPublication file in staged)
                {
                    file.Dispose();
                }
            }
        }

        private static string CreateTemporaryName(string fileName)
        {
            var nonce = new byte[16];
            using (RandomNumberGenerator random =
                RandomNumberGenerator.Create())
            {
                random.GetBytes(nonce);
            }
            return fileName
                + ".tmp."
                + Process.GetCurrentProcess().Id
                + "."
                + BitConverter.ToString(nonce).Replace("-", "");
        }
    }
}
