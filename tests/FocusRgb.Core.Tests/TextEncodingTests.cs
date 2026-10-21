using System.Diagnostics;
using System.Text;
using FocusRgb.TestSupport;

namespace FocusRgb.Core.Tests
{
    public sealed class TextEncodingTests
    {
        [Fact]
        public void TrackedTextFiles_DoNotContainCommonMojibake()
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = RepositoryPaths.Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in new[] { "ls-files", "-z", "--", "src", "tests", "docs", "sdk" })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, error);

            var markers = new[] { "\u00e2\u20ac", "\u00c3", "\u00c2" };
            var failures = new List<string>();
            foreach (var relativePath in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                var bytes = File.ReadAllBytes(Path.Combine(RepositoryPaths.Root, relativePath));
                if (bytes.Contains((byte)0))
                {
                    continue;
                }
                string text;
                try
                {
                    text = new UTF8Encoding(false, true).GetString(bytes);
                }
                catch (DecoderFallbackException)
                {
                    continue;
                }
                var lines = text.Split('\n');
                for (var index = 0; index < lines.Length; index++)
                {
                    if (markers.Any(lines[index].Contains))
                    {
                        failures.Add($"{relativePath}:{index + 1}");
                    }
                }
            }

            Assert.True(failures.Count == 0,
                "Common mojibake found:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
        }
    }
}
