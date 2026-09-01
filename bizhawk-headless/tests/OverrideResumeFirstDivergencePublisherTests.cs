using System;
using System.Collections.Generic;
using System.IO;

namespace OpenGGF.BizHawk.Headless.Tests
{
    internal static class OverrideResumeFirstDivergencePublisherTests
    {
        internal static void Register(ICollection<TestMain.TestCase> tests)
        {
            tests.Add(new TestMain.TestCase(
                "OverrideResumeFirstDivergencePublisherTests publish exactly four files once",
                PublishesExactlyFourFilesOnce));
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
    }
}
