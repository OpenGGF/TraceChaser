using System;
using System.Collections.Generic;

namespace OpenGGF.BizHawk.Headless.Tests
{
    internal static class FinalPcmWindowRunnerTests
    {
        public static void Register(ICollection<TestMain.TestCase> tests)
        {
            tests.Add(new TestMain.TestCase(
                "FinalPcmWindowRunnerTests accept only explicit absolute inputs and a bounded frame count",
                AcceptsOnlyExplicitBoundedArguments));
            tests.Add(new TestMain.TestCase(
                "FinalPcmWindowRunnerTests reject output inside the harness checkout",
                RejectsOutputInsideHarnessCheckout));
        }

        private static void AcceptsOnlyExplicitBoundedArguments()
        {
            FinalPcmWindowRunner.Options options = FinalPcmWindowRunner.Parse(new[]
            {
                FinalPcmWindowRunner.Flag, "--rom", "/r/rom.gen", "--movie", "/m/movie.bk2",
                "--output", "/o/pcm", "--frames", "600"
            }, "/checkout");
            AssertEx.Equal(600, options.Frames);
            AssertEx.Equal("/o/pcm", options.OutputDirectory);
            ExpectArgumentException(new[] { FinalPcmWindowRunner.Flag, "--rom", "rom.gen", "--movie", "/m.bk2",
                "--output", "/o", "--frames", "1" });
            ExpectArgumentException(new[] { FinalPcmWindowRunner.Flag, "--rom", "/rom.gen", "--movie", "/m.bk2",
                "--output", "/o", "--frames", "0" });
            ExpectArgumentException(new[] { FinalPcmWindowRunner.Flag, "--rom", "/rom.gen", "--movie", "/m.bk2",
                "--output", "/o", "--frames", "5", "--start", "3" });
        }

        private static void RejectsOutputInsideHarnessCheckout()
        {
            bool rejected = false;
            try
            {
                FinalPcmWindowRunner.Parse(new[] { FinalPcmWindowRunner.Flag, "--rom", "/rom.gen", "--movie",
                    "/m.bk2", "--output", "/checkout/out", "--frames", "5" }, "/checkout");
            }
            catch (ArgumentException)
            {
                rejected = true;
            }
            AssertEx.Equal(true, rejected);
        }

        private static void ExpectArgumentException(string[] args)
        {
            bool rejected = false;
            try
            {
                FinalPcmWindowRunner.Parse(args, "/checkout");
            }
            catch (ArgumentException)
            {
                rejected = true;
            }
            AssertEx.Equal(true, rejected);
        }
    }
}
