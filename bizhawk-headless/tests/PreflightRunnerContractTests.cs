using System;
using System.Collections.Generic;
using System.IO;

namespace OpenGGF.BizHawk.Headless.Tests
{
    internal static class PreflightRunnerContractTests
    {
        internal static void Register(List<TestMain.TestCase> tests)
        {
            tests.Add(new TestMain.TestCase(
                "Preflight runner fail-on-skip rejects a selected skip",
                FailOnSkipRejectsSelectedSkip));
            tests.Add(new TestMain.TestCase(
                "Preflight runner fail-on-skip accepts a selected pass",
                FailOnSkipAcceptsSelectedPass));
            tests.Add(new TestMain.TestCase(
                "Preflight registration emits no result-shaped line",
                RegistrationEmitsNoResultLine,
                serial: true));
        }

        private static void FailOnSkipRejectsSelectedSkip()
        {
            TestOptions options = Parse("--fail-on-skip", "--jobs", "1");
            var selected = new List<TestMain.TestCase> {
                new TestMain.TestCase("synthetic skipped test", () => {
                    throw new TestMain.SkipTestException("missing fixture");
                })
            };
            AssertEx.Equal(1, RunWithoutConsoleOutput(selected, options));
        }

        private static void FailOnSkipAcceptsSelectedPass()
        {
            TestOptions options = Parse("--fail-on-skip", "--jobs", "1");
            var selected = new List<TestMain.TestCase> {
                new TestMain.TestCase("synthetic passing test", () => { })
            };
            AssertEx.Equal(0, RunWithoutConsoleOutput(selected, options));
        }

        private static void RegistrationEmitsNoResultLine()
        {
            string original = Environment.GetEnvironmentVariable("S1_ROM_PATH");
            TextWriter previous = Console.Out;
            var output = new StringWriter();
            try
            {
                Environment.SetEnvironmentVariable("S1_ROM_PATH", null);
                Console.SetOut(output);
                GpgxHostTests.Register(new List<TestMain.TestCase>());
            }
            finally
            {
                Console.SetOut(previous);
                Environment.SetEnvironmentVariable("S1_ROM_PATH", original);
            }
            foreach (string line in output.ToString().Split('\n'))
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("PASS ", StringComparison.Ordinal)
                    || trimmed.StartsWith("FAIL ", StringComparison.Ordinal)
                    || trimmed.StartsWith("SKIP ", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "registration emitted a result-shaped line: " + trimmed);
                }
            }
        }

        private static TestOptions Parse(params string[] args)
        {
            TestOptions options;
            string error;
            if (!TestOptions.TryParse(args, out options, out error))
            {
                throw new InvalidOperationException(error);
            }
            return options;
        }

        private static int RunWithoutConsoleOutput(
            List<TestMain.TestCase> tests,
            TestOptions options)
        {
            TextWriter previousOut = Console.Out;
            TextWriter previousError = Console.Error;
            try
            {
                Console.SetOut(new StringWriter());
                Console.SetError(new StringWriter());
                return TestRunner.Run(tests, options);
            }
            finally
            {
                Console.SetOut(previousOut);
                Console.SetError(previousError);
            }
        }
    }
}
