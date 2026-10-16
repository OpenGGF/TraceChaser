using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

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
                "FinalPcmWindowRunnerTests reject duplicate flags including boundary flags",
                RejectsDuplicateFlags));
            tests.Add(new TestMain.TestCase(
                "FinalPcmWindowRunnerTests apply the producer boundary through resolved symlinks",
                AppliesProducerBoundary));
            tests.Add(new TestMain.TestCase(
                "FinalPcmWindowRunnerTests require an absent or empty real output directory",
                RequiresUsableOutput));
            tests.Add(new TestMain.TestCase(
                "FinalPcmWindowRunnerTests publish the completion marker only after verified closed data",
                PublishesMarkerOnlyAfterVerifiedData));
        }

        private static void AcceptsOnlyExplicitBoundedArguments()
        {
            FinalPcmWindowRunner.Options options = FinalPcmWindowRunner.Parse(new[]
            {
                FinalPcmWindowRunner.Flag, "--rom", "/r/rom.gen", "--movie", "/m/movie.bk2",
                "--output", "/o/pcm", "--frames", "600"
            });
            AssertEx.Equal(600, options.Frames);
            AssertEx.Equal("/o/pcm", options.OutputDirectory);
            ExpectArgumentException(new[] { FinalPcmWindowRunner.Flag, "--rom", "rom.gen", "--movie", "/m.bk2",
                "--output", "/o", "--frames", "1" });
            ExpectArgumentException(new[] { FinalPcmWindowRunner.Flag, "--rom", "/rom.gen", "--movie", "/m.bk2",
                "--output", "/o", "--frames", "0" });
            ExpectArgumentException(new[] { FinalPcmWindowRunner.Flag, "--rom", "/rom.gen", "--movie", "/m.bk2",
                "--output", "/o", "--frames", "216001" });
            ExpectArgumentException(new[] { FinalPcmWindowRunner.Flag, "--rom", "/rom.gen", "--movie", "/m.bk2",
                "--output", "/o", "--frames", "5", "--start", "3" });
            ExpectArgumentException(new[] { FinalPcmWindowRunner.Flag, "--rom", "/rom.gen", "--movie", "/m.bk2",
                "--output", "/o", "--frames" });
        }

        private static void RejectsDuplicateFlags()
        {
            // Last-one-wins would keep Count == 4 and pass the old check.
            ExpectArgumentException(new[] { FinalPcmWindowRunner.Flag, "--rom", "/a.gen", "--rom", "/b.gen",
                "--movie", "/m.bk2", "--output", "/o", "--frames", "5" });
            ExpectThrows<ArgumentException>(() => FinalPcmWindowRunner.RejectDuplicateFlags(new[]
            {
                FinalPcmWindowRunner.Flag, "--fixture-root", "/c/f", "--rom", "/a.gen", "--fixture-root", "/c/g"
            }));
            FinalPcmWindowRunner.RejectDuplicateFlags(new[]
            {
                FinalPcmWindowRunner.Flag, "--tracechaser-root", "/t", "--input-repository-root", "/c",
                "--fixture-root", "/c/f", "--rom", "/a.gen"
            });
        }

        private static void AppliesProducerBoundary()
        {
            string root = TestScratch.CreateRootPath("final-pcm-boundary");
            try
            {
                string tracechaser = Path.Combine(root, "tracechaser");
                string consumer = Path.Combine(root, "consumer");
                string fixtures = Path.Combine(consumer, "fixtures");
                string outside = Path.Combine(root, "outside");
                foreach (string d in new[] { tracechaser, fixtures, outside }) Directory.CreateDirectory(d);
                string aliasToFixtures = Path.Combine(outside, "alias");
                Symlink(fixtures, aliasToFixtures);
                Func<string, string[]> args = output => new[]
                {
                    FinalPcmWindowRunner.Flag, "--rom", "/rom.gen", "--movie", "/m.bk2", "--output", output, "--frames", "5"
                };
                global::BizHawk.Headless.Gpgx.Program.ValidateProducerBoundary(args(Path.Combine(outside, "run")), tracechaser, consumer, fixtures);
                foreach (string bad in new[]
                {
                    Path.Combine(tracechaser, "run"), Path.Combine(consumer, "run"), Path.Combine(fixtures, "run"),
                    Path.Combine(aliasToFixtures, "run")
                })
                    ExpectThrows<ArgumentException>(() => global::BizHawk.Headless.Gpgx.Program.ValidateProducerBoundary(args(bad), tracechaser, consumer, fixtures));
                ExpectThrows<ArgumentException>(() => global::BizHawk.Headless.Gpgx.Program.ValidateProducerBoundary(
                    args(Path.Combine(outside, "run")), tracechaser, consumer, outside));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void RequiresUsableOutput()
        {
            string root = TestScratch.CreateRootPath("final-pcm-output");
            try
            {
                Directory.CreateDirectory(root);
                FinalPcmWindowRunner.RequireUsableOutput(Path.Combine(root, "absent"));
                string empty = Path.Combine(root, "empty");
                Directory.CreateDirectory(empty);
                FinalPcmWindowRunner.RequireUsableOutput(empty);
                string full = Path.Combine(root, "full");
                Directory.CreateDirectory(full);
                File.WriteAllText(Path.Combine(full, "x"), "x");
                ExpectThrows<IOException>(() => FinalPcmWindowRunner.RequireUsableOutput(full));
                string file = Path.Combine(root, "file");
                File.WriteAllText(file, "x");
                ExpectThrows<IOException>(() => FinalPcmWindowRunner.RequireUsableOutput(file));
                string link = Path.Combine(root, "link");
                Symlink(empty, link);
                ExpectThrows<IOException>(() => FinalPcmWindowRunner.RequireUsableOutput(link));
                string dangling = Path.Combine(root, "dangling");
                Symlink(Path.Combine(root, "nowhere"), dangling);
                ExpectThrows<IOException>(() => FinalPcmWindowRunner.RequireUsableOutput(dangling));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void PublishesMarkerOnlyAfterVerifiedData()
        {
            string root = TestScratch.CreateRootPath("final-pcm-marker");
            try
            {
                byte[] pcm = { 1, 0, 2, 0, 3, 0, 4, 0 };
                string ok = Fresh(root, "ok", pcm);
                string sha = Sha(pcm);
                string returned = FinalPcmWindowRunner.CompleteOutputs(ok, sha, 8, frames => "{\"frames_tsv_sha256\": \"" + frames + "\"}\n");
                AssertEx.Equal(sha, returned);
                AssertEx.Equal("{\"frames_tsv_sha256\": \"" + FinalPcmWindowRunner.Sha256File(Path.Combine(ok, "frames.tsv")) + "\"}\n",
                    File.ReadAllText(Path.Combine(ok, "metadata.json")));
                AssertEx.Equal(false, File.Exists(Path.Combine(ok, ".metadata.json.partial")));
                ExpectThrows<IOException>(() => FinalPcmWindowRunner.CompleteOutputs(ok, sha, 8, frames => "{}"));

                string truncated = Fresh(root, "truncated", new byte[] { 1, 0, 2, 0 });
                ExpectThrows<IOException>(() => FinalPcmWindowRunner.CompleteOutputs(truncated, sha, 8, frames => "{}"));
                AssertEx.Equal(false, File.Exists(Path.Combine(truncated, "metadata.json")));

                string corrupt = Fresh(root, "corrupt", new byte[] { 1, 0, 2, 0, 3, 0, 4, 1 });
                ExpectThrows<IOException>(() => FinalPcmWindowRunner.CompleteOutputs(corrupt, sha, 8, frames => "{}"));
                AssertEx.Equal(false, File.Exists(Path.Combine(corrupt, "metadata.json")));

                string failing = Fresh(root, "failing", pcm);
                ExpectThrows<InvalidOperationException>(() => FinalPcmWindowRunner.CompleteOutputs(failing, sha, 8,
                    frames => { throw new InvalidOperationException("metadata failure"); }));
                AssertEx.Equal(false, File.Exists(Path.Combine(failing, "metadata.json")));
                AssertEx.Equal(false, File.Exists(Path.Combine(failing, ".metadata.json.partial")));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static string Fresh(string root, string name, byte[] pcm)
        {
            string dir = Path.Combine(root, name);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "pcm.s16"), pcm);
            File.WriteAllText(Path.Combine(dir, "frames.tsv"), "row\tlag\tstereo_frames\tcumulative_stereo_frames\n0\t0\t2\t2\n");
            return dir;
        }

        private static string Sha(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var builder = new StringBuilder();
                foreach (byte b in sha.ComputeHash(bytes)) builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }

        [DllImport("libc", EntryPoint = "symlink", SetLastError = true)]
        private static extern int SymlinkNative(string target, string linkPath);

        private static void Symlink(string target, string linkPath)
        {
            if (SymlinkNative(target, linkPath) != 0)
                throw new InvalidOperationException("Unable to create test symlink, errno " + Marshal.GetLastWin32Error());
        }

        private static void ExpectArgumentException(string[] args)
        {
            ExpectThrows<ArgumentException>(() => FinalPcmWindowRunner.Parse(args));
        }

        private static void ExpectThrows<T>(Action action) where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                return;
            }
            throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
        }
    }
}
