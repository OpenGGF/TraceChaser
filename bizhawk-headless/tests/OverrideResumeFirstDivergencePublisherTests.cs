using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace OpenGGF.BizHawk.Headless.Tests
{
    internal static class OverrideResumeFirstDivergencePublisherTests
    {
        internal static void Register(ICollection<TestMain.TestCase> tests)
        {
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergencePublisherTests publish exactly four files once",
                PublishesExactlyFourFilesOnce));
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergencePublisherTests reject s1 and s2 intermediate symlinks",
                RejectsGameIntermediateSymlinks));
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergencePublisherTests reject a validated game directory swap before link",
                RejectsValidatedGameDirectorySwapBeforeLink));
        }

        private static void PublishesExactlyFourFilesOnce()
        {
            OverrideResumeFirstDivergenceExtractorTests.WithInputs((root, inputs) =>
            {
                string fixtureRoot = Path.Combine(root, "src", "test", "resources",
                    "audio", "parity");
                string tracechaserRoot=Path.Combine(root,"tools","tracechaser");
                Directory.CreateDirectory(tracechaserRoot);
                new OverrideResumeFirstDivergencePublisher(
                    OverrideResumeFirstDivergenceExtractor.ForTesting(),
                    new NoReplacePublisher()).Publish(inputs, tracechaserRoot,
                        root, fixtureRoot);
                string[] files = Directory.GetFiles(fixtureRoot, "*",
                    SearchOption.AllDirectories);
                AssertEx.Equal(4, files.Length);
                byte[] before = File.ReadAllBytes(files[0]);
                AssertEx.Throws<IOException>(() =>
                    new OverrideResumeFirstDivergencePublisher(
                        OverrideResumeFirstDivergenceExtractor.ForTesting(),
                        new NoReplacePublisher()).Publish(inputs, tracechaserRoot,
                            root, fixtureRoot),
                    "already exists");
                AssertEx.Equal(Convert.ToBase64String(before),
                    Convert.ToBase64String(File.ReadAllBytes(files[0])));
            });
        }

        private static void RejectsGameIntermediateSymlinks()
        {
            foreach (string game in new[] { "s1", "s2" })
            {
                OverrideResumeFirstDivergenceExtractorTests.WithInputs(
                    (root, inputs) =>
                    {
                        string fixtureRoot = Path.Combine(root, "src", "test",
                            "resources", "audio", "parity");
                        string tracechaserRoot = Path.Combine(root, "tools",
                            "tracechaser");
                        string target = Path.Combine(root,
                            game + "-symlink-target");
                        Directory.CreateDirectory(fixtureRoot);
                        Directory.CreateDirectory(tracechaserRoot);
                        Directory.CreateDirectory(target);
                        string link = Path.Combine(fixtureRoot, game);
                        if (Symlink(target, link) != 0)
                            throw new IOException("symlink(2) failed with errno "
                                + Marshal.GetLastWin32Error() + ".");
                        try
                        {
                            AssertEx.Throws<IOException>(() =>
                                new OverrideResumeFirstDivergencePublisher(
                                    OverrideResumeFirstDivergenceExtractor
                                        .ForTesting(),
                                    new NoReplacePublisher()).Publish(inputs,
                                        tracechaserRoot, root, fixtureRoot),
                                "symbolic link");
                            AssertEx.Equal(0,
                                Directory.GetFileSystemEntries(target).Length);
                        }
                        finally
                        {
                            if (LinuxPathEntry.Exists(link) && Unlink(link) != 0)
                                throw new IOException(
                                    "unlink(2) failed with errno "
                                    + Marshal.GetLastWin32Error() + ".");
                        }
                    });
            }
        }

        private static void RejectsValidatedGameDirectorySwapBeforeLink()
        {
            OverrideResumeFirstDivergenceExtractorTests.WithInputs(
                (root, inputs) =>
                {
                    string fixtureRoot = Path.Combine(root, "src", "test",
                        "resources", "audio", "parity");
                    string tracechaserRoot = Path.Combine(root, "tools",
                        "tracechaser");
                    string outsideRoot = Path.Combine(root,
                        "outside-fixture-root");
                    Directory.CreateDirectory(tracechaserRoot);
                    var link = new SwapGameDirectoryThenLinkOperation(
                        outsideRoot);
                    try
                    {
                        AssertEx.Throws<IOException>(() =>
                            new OverrideResumeFirstDivergencePublisher(
                                OverrideResumeFirstDivergenceExtractor
                                    .ForTesting(),
                                new NoReplacePublisher(link)).Publish(inputs,
                                    tracechaserRoot, root, fixtureRoot),
                            "directory");
                        AssertEx.Equal(true, link.Swapped);
                        AssertEx.Equal(false,
                            link.ExternallyVisibleFinalObserved);
                        AssertEx.Equal(false, File.Exists(Path.Combine(
                            link.MovedGameDirectory,
                            "s1-override-resume-reference.v1.jsonl.gz")));
                        AssertEx.Equal(false, File.Exists(Path.Combine(
                            link.MovedGameDirectory,
                            "s1-override-resume-metadata.v1.json")));
                    }
                    finally
                    {
                        link.Restore();
                    }
                });
        }

        private sealed class SwapGameDirectoryThenLinkOperation
            : ILinkOperation
        {
            private readonly string outsideRoot;
            private string gameDirectory;

            internal SwapGameDirectoryThenLinkOperation(string outside)
            {
                outsideRoot = outside;
            }

            internal bool Swapped { get; private set; }
            internal bool ExternallyVisibleFinalObserved { get; private set; }
            internal string MovedGameDirectory { get; private set; }

            public void Create(string temporary, string finalPath,
                Action createAnchoredLink)
            {
                if (!Swapped && Path.GetFileName(
                    Path.GetDirectoryName(finalPath)) == "s1")
                {
                    gameDirectory = Path.GetDirectoryName(finalPath);
                    Directory.CreateDirectory(outsideRoot);
                    MovedGameDirectory = Path.Combine(outsideRoot, "s1");
                    Directory.Move(gameDirectory, MovedGameDirectory);
                    if (Symlink(MovedGameDirectory, gameDirectory) != 0)
                        throw new IOException(
                            "symlink(2) failed with errno "
                            + Marshal.GetLastWin32Error() + ".");
                    Swapped = true;
                }
                try
                {
                    createAnchoredLink();
                }
                finally
                {
                    if (Swapped && File.Exists(finalPath))
                        ExternallyVisibleFinalObserved = true;
                }
            }

            internal void Restore()
            {
                if (!Swapped)
                    return;
                if (LinuxPathEntry.Exists(gameDirectory)
                    && Unlink(gameDirectory) != 0)
                    throw new IOException("unlink(2) failed with errno "
                        + Marshal.GetLastWin32Error() + ".");
                Directory.Move(MovedGameDirectory, gameDirectory);
                Swapped = false;
            }
        }

        [DllImport("libc", EntryPoint = "symlink", CharSet = CharSet.Ansi,
            SetLastError = true)]
        private static extern int Symlink(string target, string linkPath);

        [DllImport("libc", EntryPoint = "unlink", CharSet = CharSet.Ansi,
            SetLastError = true)]
        private static extern int Unlink(string path);
    }
}
