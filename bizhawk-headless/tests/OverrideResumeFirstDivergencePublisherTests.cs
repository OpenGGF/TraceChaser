using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace OpenGGF.BizHawk.Headless.Tests
{
    internal static class OverrideResumeFirstDivergencePublisherTests
    {
        private const string Bundle = "override-resume-first-divergence-v1";

        internal static void Register(ICollection<TestMain.TestCase> tests)
        {
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergencePublisherTests atomically publish one exact durable bundle",
                PublishesOneExactDurableBundle));
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergencePublisherTests leave an existing bundle untouched",
                LeavesExistingBundleUntouched));
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergencePublisherTests expose no public name at every precommit fault",
                ExposesNoPublicNameAtEveryPrecommitFault));
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergencePublisherTests preserve a competing rename target",
                PreservesCompetingRenameTarget));
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergencePublisherTests report committed durability uncertainty without rollback",
                ReportsCommittedDurabilityUncertaintyWithoutRollback));
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergencePublisherTests have one rename visibility boundary",
                HasOneRenameVisibilityBoundary));
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergencePublisherTests survive child death on both sides of commit",
                SurviveChildDeathOnBothSidesOfCommit));
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergencePublisherTests serialize cooperating publishers",
                SerializesCooperatingPublishers));
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergencePublisherTests reject symlinked trusted root components",
                RejectsSymlinkedTrustedRootComponents));
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergencePublisherTests reject changed staged inventory",
                RejectsChangedStagedInventory));
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergencePublisherTests reject a staged directory replacement before commit",
                RejectsStagedDirectoryReplacementBeforeCommit));
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergencePublisherTests detect fixture root replacement before commit",
                DetectsFixtureRootReplacementBeforeCommit));
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergencePublisherTests declare the namespace stability precondition",
                DeclaresNamespaceStabilityPrecondition));
        }

        private static void PublishesOneExactDurableBundle()
        {
            WithPublication((root, trace, fixture, inputs) =>
            {
                var hooks = new RecordingHooks();
                OverrideResumeBundlePublicationResult result = Publish(
                    root, trace, fixture, inputs, hooks);
                AssertEx.Equal(OverrideResumeBundlePublicationResult.Durable,
                    result);
                string bundle = Path.Combine(fixture, Bundle);
                AssertEx.Equal(true, Directory.Exists(bundle));
                AssertEx.Equal(
                    "s1/s1-override-resume-metadata.v1.json,"
                    + "s1/s1-override-resume-reference.v1.jsonl.gz,"
                    + "s2/s2-override-resume-metadata.v1.json,"
                    + "s2/s2-override-resume-reference.v1.jsonl.gz",
                    Inventory(bundle));
                AssertEx.Equal(1,
                    hooks.Operations.Count(value => value.Name == "renameat2"));
                AssertEx.Equal(true, hooks.Operations.Any(value =>
                    value.Name == "fsync-root"));
                AssertEx.Equal(448, Mode(bundle));
                AssertEx.Equal(448, Mode(Path.Combine(bundle, "s1")));
                AssertEx.Equal(384, Mode(Path.Combine(bundle, "s1",
                    "s1-override-resume-metadata.v1.json")));
            });
        }

        private static void LeavesExistingBundleUntouched()
        {
            WithPublication((root, trace, fixture, inputs) =>
            {
                string bundle = Path.Combine(fixture, Bundle);
                Directory.CreateDirectory(bundle);
                string marker = Path.Combine(bundle, "competitor.bin");
                byte[] before = Encoding.UTF8.GetBytes("competitor\n");
                File.WriteAllBytes(marker, before);
                AssertEx.Throws<IOException>(() => Publish(root, trace,
                    fixture, inputs, new RecordingHooks()), "already exists");
                AssertEx.Equal(Convert.ToBase64String(before),
                    Convert.ToBase64String(File.ReadAllBytes(marker)));
                AssertEx.Equal("competitor.bin", Inventory(bundle));
            });
        }

        private static void ExposesNoPublicNameAtEveryPrecommitFault()
        {
            var baseline = new RecordingHooks();
            WithPublication((root, trace, fixture, inputs) =>
            {
                Publish(root, trace, fixture, inputs, baseline);
            });
            int renameOrdinal = baseline.Operations.Single(value =>
                value.Name == "renameat2").Ordinal;
            for (int ordinal = 1; ordinal <= renameOrdinal; ordinal++)
            {
                int selected = ordinal;
                WithPublication((root, trace, fixture, inputs) =>
                {
                    var hooks = new RecordingHooks(selected, 5);
                    AssertEx.Throws<IOException>(() => Publish(root, trace,
                        fixture, inputs, hooks), "injected native failure");
                    AssertEx.Equal(false,
                        LinuxPathEntry.Exists(Path.Combine(fixture, Bundle)));
                    foreach (string residue in Directory.GetFileSystemEntries(
                        fixture))
                    {
                        AssertEx.Equal(true, Path.GetFileName(residue)
                            .StartsWith("." + Bundle + ".tmp.",
                                StringComparison.Ordinal));
                        AssertEx.Equal(448, Mode(residue));
                    }
                });
            }
        }

        private static void PreservesCompetingRenameTarget()
        {
            WithPublication((root, trace, fixture, inputs) =>
            {
                byte[] marker = Encoding.UTF8.GetBytes("winner\n");
                var hooks = new RecordingHooks();
                hooks.Barrier = (name, privateName) =>
                {
                    if (name != "before-rename") return;
                    string target = Path.Combine(fixture, Bundle);
                    Directory.CreateDirectory(target);
                    File.WriteAllBytes(Path.Combine(target, "winner.bin"),
                        marker);
                };
                AssertEx.Throws<IOException>(() => Publish(root, trace,
                    fixture, inputs, hooks), "already exists");
                AssertEx.Equal(Convert.ToBase64String(marker),
                    Convert.ToBase64String(File.ReadAllBytes(Path.Combine(
                        fixture, Bundle, "winner.bin"))));
            });
        }

        private static void ReportsCommittedDurabilityUncertaintyWithoutRollback()
        {
            WithPublication((root, trace, fixture, inputs) =>
            {
                var hooks = new RecordingHooks("fsync-root", 5);
                OverrideResumeBundlePublicationResult result = Publish(root,
                    trace, fixture, inputs, hooks);
                AssertEx.Equal(
                    OverrideResumeBundlePublicationResult
                        .CommittedButDurabilityUnconfirmed,
                    result);
                AssertEx.Equal(4, Directory.GetFiles(Path.Combine(fixture,
                    Bundle), "*", SearchOption.AllDirectories).Length);
            });
        }

        private static void HasOneRenameVisibilityBoundary()
        {
            WithPublication((root, trace, fixture, inputs) =>
            {
                var hooks = new RecordingHooks();
                hooks.Barrier = (name, privateName) =>
                {
                    string publicPath = Path.Combine(fixture, Bundle);
                    if (name == "before-rename")
                        AssertEx.Equal(false, LinuxPathEntry.Exists(publicPath));
                    if (name == "after-rename")
                        AssertEx.Equal(4, Directory.GetFiles(publicPath, "*",
                            SearchOption.AllDirectories).Length);
                };
                Publish(root, trace, fixture, inputs, hooks);
            });
        }

        private static void SerializesCooperatingPublishers()
        {
            WithPublication((root, trace, fixture, inputs) =>
            {
                var ready = new ManualResetEvent(false);
                var release = new ManualResetEvent(false);
                var firstHooks = new RecordingHooks();
                firstHooks.Barrier = (name, privateName) =>
                {
                    if (name != "private-created") return;
                    ready.Set();
                    release.WaitOne();
                };
                Exception firstError = null;
                Exception secondError = null;
                OverrideResumeBundlePublicationResult firstResult = default(
                    OverrideResumeBundlePublicationResult);
                var first = new Thread(() =>
                {
                    try { firstResult = Publish(root, trace, fixture, inputs,
                        firstHooks); }
                    catch (Exception exception) { firstError = exception; }
                });
                var second = new Thread(() =>
                {
                    try { Publish(root, trace, fixture, inputs,
                        new RecordingHooks()); }
                    catch (Exception exception) { secondError = exception; }
                });
                first.Start();
                AssertEx.Equal(true, ready.WaitOne(5000));
                second.Start();
                Thread.Sleep(100);
                AssertEx.Equal(true, second.IsAlive);
                release.Set();
                first.Join();
                second.Join();
                AssertEx.Equal(null, firstError);
                AssertEx.Equal(OverrideResumeBundlePublicationResult.Durable,
                    firstResult);
                AssertEx.Equal(true, secondError is IOException);
                AssertEx.Equal(4, Directory.GetFiles(Path.Combine(fixture,
                    Bundle), "*", SearchOption.AllDirectories).Length);
            });
        }

        private static void SurviveChildDeathOnBothSidesOfCommit()
        {
            WithPublication((root, trace, fixture, inputs) =>
            {
                RunKilledChild(root, trace, fixture, inputs, "before-rename");
                AssertEx.Equal(false,
                    LinuxPathEntry.Exists(Path.Combine(fixture, Bundle)));
            });
            WithPublication((root, trace, fixture, inputs) =>
            {
                RunKilledChild(root, trace, fixture, inputs, "after-rename");
                AssertEx.Equal(4, Directory.GetFiles(Path.Combine(fixture,
                    Bundle), "*", SearchOption.AllDirectories).Length);
            });
        }

        private static void RunKilledChild(string root, string trace,
            string fixture,
            OverrideResumeFirstDivergenceExtractor.Inputs inputs,
            string barrier)
        {
            int child = Fork();
            if (child < 0) throw new IOException("fork failed");
            if (child == 0)
            {
                var hooks = new RecordingHooks();
                hooks.Barrier = (name, privateName) =>
                {
                    if (name == barrier) Kill(GetPid(), 9);
                };
                try
                {
                    Publish(root, trace, fixture, inputs, hooks);
                    ImmediateExit(70);
                }
                catch
                {
                    ImmediateExit(71);
                }
            }
            int status;
            if (WaitPid(child, out status, 0) != child)
                throw new IOException("waitpid failed");
            AssertEx.Equal(9, status & 0x7f);
        }

        private static void RejectsSymlinkedTrustedRootComponents()
        {
            WithPublication((root, trace, fixture, inputs) =>
            {
                string link = root + "-link";
                if (Symlink(root, link) != 0)
                    throw new IOException("symlink failed");
                try
                {
                    string linkedTrace = Path.Combine(link, "tools",
                        "tracechaser");
                    string linkedFixture = Path.Combine(link, "src", "test",
                        "resources", "audio", "parity");
                    AssertEx.Throws<IOException>(() => Publish(link,
                        linkedTrace, linkedFixture, inputs,
                        new RecordingHooks()), "openat2");
                    AssertEx.Equal(false,
                        LinuxPathEntry.Exists(Path.Combine(fixture, Bundle)));
                }
                finally
                {
                    if (LinuxPathEntry.Exists(link)) Unlink(link);
                }
            });
        }

        private static void RejectsChangedStagedInventory()
        {
            WithPublication((root, trace, fixture, inputs) =>
            {
                var hooks = new RecordingHooks();
                hooks.Barrier = (name, privateName) =>
                {
                    if (name == "before-staged-validation")
                        File.WriteAllText(Path.Combine(fixture, privateName,
                            "extra"), "unexpected\n");
                };
                AssertEx.Throws<IOException>(() => Publish(root, trace,
                    fixture, inputs, hooks), "inventory");
                AssertEx.Equal(false,
                    LinuxPathEntry.Exists(Path.Combine(fixture, Bundle)));
            });
        }

        private static void DetectsFixtureRootReplacementBeforeCommit()
        {
            WithPublication((root, trace, fixture, inputs) =>
            {
                string moved = fixture + ".moved";
                var hooks = new RecordingHooks();
                hooks.Barrier = (name, privateName) =>
                {
                    if (name != "before-root-revalidation") return;
                    Directory.Move(fixture, moved);
                    Directory.CreateDirectory(fixture);
                };
                try
                {
                    AssertEx.Throws<IOException>(() => Publish(root, trace,
                        fixture, inputs, hooks), "identity changed");
                    AssertEx.Equal(false,
                        LinuxPathEntry.Exists(Path.Combine(fixture, Bundle)));
                }
                finally
                {
                    if (Directory.Exists(fixture))
                        Directory.Delete(fixture, true);
                    if (Directory.Exists(moved)) Directory.Move(moved, fixture);
                }
            });
        }

        private static void RejectsStagedDirectoryReplacementBeforeCommit()
        {
            WithPublication((root, trace, fixture, inputs) =>
            {
                string moved = Path.Combine(root, "moved-s1");
                string link = null;
                var hooks = new RecordingHooks();
                hooks.Barrier = (name, privateName) =>
                {
                    if (name != "before-root-revalidation") return;
                    link = Path.Combine(fixture, privateName, "s1");
                    Directory.Move(link, moved);
                    if (Symlink(moved, link) != 0)
                        throw new IOException("symlink failed");
                };
                try
                {
                    AssertEx.Throws<IOException>(() => Publish(root, trace,
                        fixture, inputs, hooks), "identity changed");
                    AssertEx.Equal(false,
                        LinuxPathEntry.Exists(Path.Combine(fixture, Bundle)));
                }
                finally
                {
                    string publicLink = Path.Combine(fixture, Bundle, "s1");
                    string restore = link;
                    if (LinuxPathEntry.Exists(publicLink))
                    {
                        Unlink(publicLink);
                        restore = publicLink;
                    }
                    else if (link != null && LinuxPathEntry.Exists(link))
                    {
                        Unlink(link);
                    }
                    if (Directory.Exists(moved) && restore != null)
                        Directory.Move(moved, restore);
                }
            });
        }

        private static void DeclaresNamespaceStabilityPrecondition()
        {
            string contract = OverrideResumeFirstDivergencePublisher
                .NamespaceStabilityPrecondition;
            AssertEx.Equal(true, contract.Contains("cooperate"));
            AssertEx.Equal(true, contract.Contains("namespace-stable"));
            AssertEx.Equal(true, contract.Contains("rename and mount"));
            AssertEx.Equal(true, contract.Contains("unsupported"));
        }

        private static OverrideResumeBundlePublicationResult Publish(
            string root, string trace, string fixture,
            OverrideResumeFirstDivergenceExtractor.Inputs inputs,
            IOverrideResumeBundlePublicationHooks hooks)
        {
            return new OverrideResumeFirstDivergencePublisher(
                OverrideResumeFirstDivergenceExtractor.ForTesting(), hooks)
                .Publish(inputs, trace, root, fixture);
        }

        private static void WithPublication(Action<string, string, string,
            OverrideResumeFirstDivergenceExtractor.Inputs> action)
        {
            OverrideResumeFirstDivergenceExtractorTests.WithInputs(
                (root, inputs) =>
                {
                    string trace = Path.Combine(root, "tools", "tracechaser");
                    string fixture = Path.Combine(root, "src", "test",
                        "resources", "audio", "parity");
                    Directory.CreateDirectory(trace);
                    Directory.CreateDirectory(fixture);
                    action(root, trace, fixture, inputs);
                });
        }

        private static string Inventory(string root)
        {
            return string.Join(",", Directory.GetFiles(root, "*",
                SearchOption.AllDirectories).Select(path => path.Substring(
                    root.Length + 1).Replace('\\', '/')).OrderBy(value => value,
                        StringComparer.Ordinal).ToArray());
        }

        private static int Mode(string path)
        {
            Stat value;
            if (LStat(path, out value) != 0)
                throw new IOException("lstat failed");
            return unchecked((int)value.Mode) & 511;
        }

        private sealed class RecordingHooks
            : IOverrideResumeBundlePublicationHooks
        {
            private readonly int faultOrdinal;
            private readonly string faultName;
            private readonly int error;
            private int ordinal;

            internal RecordingHooks() { }
            internal RecordingHooks(int selectedOrdinal, int errno)
            { faultOrdinal = selectedOrdinal; error = errno; }
            internal RecordingHooks(string selectedName, int errno)
            { faultName = selectedName; error = errno; }

            internal readonly List<Operation> Operations =
                new List<Operation>();
            internal Action<string, string> Barrier;

            public void BeforeNativeOperation(string name)
            {
                ordinal++;
                Operations.Add(new Operation(name, ordinal));
                if (ordinal == faultOrdinal || name == faultName)
                    throw new OverrideResumeInjectedNativeException(name,
                        error);
            }

            public void AtBarrier(string name, string privateName)
            {
                if (Barrier != null) Barrier(name, privateName);
            }
        }

        internal sealed class Operation
        {
            internal Operation(string name, int ordinal)
            { Name = name; Ordinal = ordinal; }
            internal string Name { get; private set; }
            internal int Ordinal { get; private set; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Stat
        {
            internal ulong Device;
            internal ulong Inode;
            internal ulong LinkCount;
            internal uint Mode;
            private readonly uint uid, gid, pad;
            private readonly ulong rdev;
            private readonly long size, blockSize, blocks;
            private readonly long atime, atimeNs, mtime, mtimeNs;
            private readonly long ctime, ctimeNs;
            private readonly long unused1, unused2, unused3;
        }

        [DllImport("libc", EntryPoint = "lstat", CharSet = CharSet.Ansi,
            SetLastError = true)]
        private static extern int LStat(string path, out Stat value);

        [DllImport("libc", EntryPoint = "symlink", CharSet = CharSet.Ansi,
            SetLastError = true)]
        private static extern int Symlink(string target, string linkPath);

        [DllImport("libc", EntryPoint = "unlink", CharSet = CharSet.Ansi,
            SetLastError = true)]
        private static extern int Unlink(string path);

        [DllImport("libc", EntryPoint = "fork", SetLastError = true)]
        private static extern int Fork();

        [DllImport("libc", EntryPoint = "getpid")]
        private static extern int GetPid();

        [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
        private static extern int Kill(int process, int signal);

        [DllImport("libc", EntryPoint = "waitpid", SetLastError = true)]
        private static extern int WaitPid(int process, out int status,
            int options);

        [DllImport("libc", EntryPoint = "_exit")]
        private static extern void ImmediateExit(int status);
    }
}
