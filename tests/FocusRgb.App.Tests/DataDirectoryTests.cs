using System.Diagnostics;
using System.Text.Json;
using FocusRgb.LocalMcp;
using FocusRgb.TestSupport;

namespace FocusRgb.App.Tests
{
    public sealed class DataDirectoryTests
    {
        [Fact]
        public void InstanceLock_RefusesACompetingFolderHolderUntilReleased()
        {
            using var temporary = new TempDirectory(nameof(DataDirectoryTests));
            using (ApplicationDataDirectory.AcquireInstanceLock(temporary.Path))
            {
                Assert.Throws<IOException>(() => ApplicationDataDirectory.AcquireInstanceLock(temporary.Path + "/./"));
            }
            using var acquired = ApplicationDataDirectory.AcquireInstanceLock(temporary.Path);
            Assert.True(acquired.CanWrite);
        }
        [Fact]
        public void Resolve_CommandLineWinsAndEquivalentFoldersShareIdentity()
        {
            using var temporary = new TempDirectory(nameof(DataDirectoryTests));
            var path = Path.Combine(temporary.Path, "chosen");
            Assert.Equal(path, ApplicationDataDirectory.Resolve(["--data-dir", path], "C:/ignored"));
            Assert.Equal(path, ApplicationDataDirectory.Resolve([], path));
            Assert.Equal(LocalMcpProtocol.PipeNameFor(path), LocalMcpProtocol.PipeNameFor(path.ToUpperInvariant() + "/./"));
            Assert.NotEqual(LocalMcpProtocol.PipeNameFor(path), LocalMcpProtocol.PipeNameFor(temporary.Path));
            Assert.DoesNotContain(temporary.Path, LocalMcpProtocol.PipeNameFor(path), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Resolve_RejectsMalformedOptions()
        {
            foreach (var args in new string[][]
            {
                ["--data-dir"], ["--data-dir", ""], ["--data-dir", "--minimized"],
                ["--data-dir", "first", "--data-dir", "second"]
            })
            {
                Assert.Throws<ArgumentException>(() => ApplicationDataDirectory.Resolve(args));
            }
        }

        [Fact]
        public async Task IsolatedApps_RunSideBySideAndRefuseDuplicateFolder()
        {
            using var temporary = new TempDirectory(nameof(DataDirectoryTests));
            var configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Name;
            var executable = Path.Combine(RepositoryPaths.Root, "build", "artifacts", "bin", "FocusRgb.App", configuration, "FocusRGB.exe");
            var firstDirectory = Path.Combine(temporary.Path, "first");
            var secondDirectory = Path.Combine(temporary.Path, "second");
            var firstStore = new McpConfigurationStore(firstDirectory);
            var secondStore = new McpConfigurationStore(secondDirectory);
            Assert.True((await firstStore.SetEnabledAsync(true, executable, TestContext.Current.CancellationToken)).IsSuccess);
            Assert.True((await secondStore.SetEnabledAsync(true, executable, TestContext.Current.CancellationToken)).IsSuccess);
            using var first = Start(executable, firstDirectory, useEnvironment: false);
            using var second = Start(executable, secondDirectory, useEnvironment: true);
            try
            {
                await WaitForStatusAsync(new LocalMcpPipeClient(firstStore));
                await WaitForStatusAsync(new LocalMcpPipeClient(secondStore));
                Assert.False(first.HasExited);
                Assert.False(second.HasExited);
                using var duplicate = Start(executable, firstDirectory + "/./", useEnvironment: false);
                try
                {
                    await duplicate.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                    Assert.Equal(0, duplicate.ExitCode);
                    Assert.False(first.HasExited);
                    await WaitForStatusAsync(new LocalMcpPipeClient(firstStore));
                }
                finally
                {
                    await StopAsync(duplicate);
                }
                Assert.All(new[] { firstDirectory, secondDirectory }, directory =>
                {
                    Assert.True(File.Exists(Path.Combine(directory, "settings.json")));
                    Assert.True(File.Exists(Path.Combine(directory, "state.json")));
                });
            }
            finally
            {
                await StopAsync(first);
                await StopAsync(second);
            }
        }

        private static Process Start(string executable, string directory, bool useEnvironment)
        {
            var start = new ProcessStartInfo(executable) { UseShellExecute = false };
            start.ArgumentList.Add("--minimized");
            if (useEnvironment)
            {
                start.Environment["FOCUSRGB_DATA_DIR"] = directory;
            }
            else
            {
                start.ArgumentList.Add("--data-dir");
                start.ArgumentList.Add(directory);
            }
            return Process.Start(start)!;
        }

        private static async Task WaitForStatusAsync(LocalMcpPipeClient client)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            while (true)
            {
                try
                {
                    JsonElement status = await client.QueryAsync("getStatus", deadline.Token);
                    Assert.Equal("manual", status.GetProperty("selectionState").GetProperty("mode").GetString());
                    return;
                }
                catch (InvalidOperationException) when (!deadline.IsCancellationRequested)
                {
                    await Task.Delay(100, deadline.Token);
                }
            }
        }

        private static async Task StopAsync(Process process)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }
}
