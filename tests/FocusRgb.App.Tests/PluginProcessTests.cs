using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using FocusRgb.App.Plugins;
using FocusRgb.Contracts;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Coordination;
using FocusRgb.Core.Selection;

namespace FocusRgb.App.Tests
{
    /// <summary>Exercises the real stdio process boundary against the main-repository fake plugin.</summary>
    public sealed class PluginProcessTests
    {
        [Fact]
        public async Task CatalogAndApply_ReturnTypedResults_WhenFakePluginIsHealthy()
        {
            var started = await StartAsync();
            Assert.True(started.IsSuccess, started.ErrorMessage);
            await using var client = started.Client!;

            var catalog = await client.ListCatalogAsync("effect", TestContext.Current.CancellationToken);
            var applied = await client.ApplyBindingAsync(new BindingApplyParameters
            {
                Profile = "programming",
                BindingId = "main",
                RenderingAuthority = "provider",
                DeviceScope = new PluginDeviceScope { Kind = "provider-managed" },
                Effect = new PluginResourceReference { ResourceId = "fake-aurora", DisplayName = "Aurora" }
            }, TestContext.Current.CancellationToken);
            var status = await client.GetStatusAsync(TestContext.Current.CancellationToken);

            Assert.True(catalog.IsSuccess, catalog.ErrorMessage);
            Assert.Equal("fake-aurora", Assert.Single(catalog.Value!.Items).ResourceId);
            Assert.True(applied.IsSuccess, applied.ErrorMessage);
            Assert.Equal("applied", applied.Value!.Outcome);
            Assert.Equal("fake-aurora", status.Value!.Current!.Effect!.ResourceId);
            Assert.Equal("healthy", client.Health);
        }

        [Fact]
        public async Task StartAsync_ReturnsContractIncompatible_WhenPluginReportsNewMajor()
        {
            var started = await StartAsync("--contract-version", "1.0");

            Assert.False(started.IsSuccess);
            Assert.Equal("contract-incompatible", started.ErrorCode);
        }

        [Fact]
        public async Task StartAsync_AcceptsLowerMinorContractVersion()
        {
            var started = await StartAsync("--contract-version", "0.0");

            Assert.True(started.IsSuccess, started.ErrorMessage);
            await started.Client!.DisposeAsync();
        }

        [Fact]
        public async Task StartAsync_RejectsNullCapabilities()
        {
            var started = await StartAsync("--null-capabilities");

            Assert.False(started.IsSuccess);
            Assert.Equal("contract-incompatible", started.ErrorCode);
        }

        [Fact]
        public async Task StartAsync_ReturnsMalformedResponse_WhenStdoutIsNotJson()
        {
            var started = await StartAsync("--malformed");

            Assert.False(started.IsSuccess);
            Assert.Equal("malformed-response", started.ErrorCode);
        }

        [Fact]
        public async Task GetStatusAsync_ReturnsPluginExited_WhenChildExitsMidRequest()
        {
            var started = await StartAsync("--exit-on-status");
            Assert.True(started.IsSuccess, started.ErrorMessage);
            await using var client = started.Client!;
            var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            client.StatusChanged += (_, _) => exited.TrySetResult();

            var status = await client.GetStatusAsync(TestContext.Current.CancellationToken);
            await exited.Task.WaitAsync(TestContext.Current.CancellationToken);

            Assert.False(status.IsSuccess);
            Assert.Equal("plugin-exited", status.ErrorCode);
            Assert.Equal("exited", client.Health);
        }

        [Fact]
        public async Task GetStatusAsync_ReturnsTimeout_WithoutHangingLateResponse()
        {
            var started = await StartAsync("--delay-ms", "5500");
            Assert.True(started.IsSuccess, started.ErrorMessage);
            await using var client = started.Client!;

            var status = await client.GetStatusAsync(TestContext.Current.CancellationToken);

            Assert.False(status.IsSuccess);
            Assert.Equal("timeout", status.ErrorCode);
        }

        [Theory]
        [InlineData("string-id")]
        [InlineData("null-id")]
        [InlineData("array")]
        [InlineData("error-string")]
        [InlineData("oversize")]
        public async Task MalformedLine_DoesNotStopReaderOrBreakDispose(string kind)
        {
            var started = await StartAsync("--invalid-response", kind);
            Assert.True(started.IsSuccess, started.ErrorMessage);
            await using var client = started.Client!;

            var status = await client.GetStatusAsync(TestContext.Current.CancellationToken);
            var catalog = await client.ListCatalogAsync("effect", TestContext.Current.CancellationToken);

            Assert.Equal("malformed-response", status.ErrorCode);
            Assert.True(catalog.IsSuccess, catalog.ErrorMessage);
        }

        [Fact]
        public async Task StartAsync_DrainsAndBuffersUtf8StderrBeforeHandshake()
        {
            var started = await StartAsync("--stderr-bytes", "200000");
            Assert.True(started.IsSuccess, started.ErrorMessage);
            await using var client = started.Client!;
            var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var length = 0;

            client.LogReceived += (_, value) =>
            {
                if (Interlocked.Add(ref length, value.Length) == 200000) received.TrySetResult();
            };

            await received.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(200000, length);
        }

        [Fact]
        public async Task Catalog_PreservesUtf8Text()
        {
            var started = await StartAsync("--catalog-name", "Café");
            Assert.True(started.IsSuccess, started.ErrorMessage);
            await using var client = started.Client!;

            var catalog = await client.ListCatalogAsync("effect", TestContext.Current.CancellationToken);

            Assert.Equal("Café", Assert.Single(catalog.Value!.Items).DisplayName);
        }

        [Fact]
        public async Task ConcurrentCalls_CorrelateOutOfOrderResponses()
        {
            var started = await StartAsync("--delay-ms", "200");
            Assert.True(started.IsSuccess, started.ErrorMessage);
            await using var client = started.Client!;

            var status = client.GetStatusAsync(TestContext.Current.CancellationToken);
            var catalog = client.ListCatalogAsync("effect", TestContext.Current.CancellationToken);
            await Task.WhenAll(status, catalog);
            var statusResult = await status;
            var catalogResult = await catalog;

            Assert.True(statusResult.IsSuccess, statusResult.ErrorMessage);
            Assert.True(catalogResult.IsSuccess, catalogResult.ErrorMessage);
        }

        [Fact]
        public async Task StatusChanged_IsCoalescedUntilStatusIsRead()
        {
            var started = await StartAsync("--notify-count", "3");
            Assert.True(started.IsSuccess, started.ErrorMessage);
            await using var client = started.Client!;
            var notifications = 0;
            client.StatusChanged += (_, _) => Interlocked.Increment(ref notifications);
            var parameters = new BindingApplyParameters
            {
                Profile = "default",
                BindingId = "main",
                RenderingAuthority = "provider",
                DeviceScope = new PluginDeviceScope { Kind = "provider-managed" }
            };

            await client.ApplyBindingAsync(parameters, TestContext.Current.CancellationToken);
            Assert.Equal(1, notifications);
            await client.GetStatusAsync(TestContext.Current.CancellationToken);
            await client.ApplyBindingAsync(parameters, TestContext.Current.CancellationToken);

            Assert.Equal(2, notifications);
        }

        [Fact]
        public async Task StartAsync_KillsProcess_WhenInitializationIsCancelled()
        {
            var pidFile = Path.Combine(Path.GetTempPath(), $"FocusRgb.{Guid.NewGuid():N}.pid");
            using var cancellation = new CancellationTokenSource();
            try
            {
                var start = StartAsync(cancellation.Token, "--pid-file", pidFile, "--initialize-delay-ms", "5000");
                while (!File.Exists(pidFile))
                    await Task.Delay(20, TestContext.Current.CancellationToken);
                var pid = int.Parse(
                    await File.ReadAllTextAsync(pidFile, TestContext.Current.CancellationToken),
                    System.Globalization.CultureInfo.InvariantCulture);
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);

                try
                {
                    await Process.GetProcessById(pid).WaitForExitAsync(TestContext.Current.CancellationToken);
                }
                catch (ArgumentException)
                {
                }
            }
            finally
            {
                if (File.Exists(pidFile)) File.Delete(pidFile);
            }
        }

        [Fact]
        public async Task Runtime_ReportsMissingEnabledPackage_WithoutFailingStartup()
        {
            var directory = Path.Combine(Path.GetTempPath(), "FocusRgb.PluginTests", Guid.NewGuid().ToString("N"));
            try
            {
                await using var runtime = await PluginRuntime.StartAsync(
                    directory, ["missing-plugin"], TestContext.Current.CancellationToken);

                var entry = Assert.Single(runtime.Entries);
                Assert.False(entry.IsRunning);
                Assert.Equal("plugin-absent", entry.ErrorCode);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public async Task Coordinator_AppliesDesiredProfile_ThroughRealFakeProcess()
        {
            var started = await StartAsync();
            Assert.True(started.IsSuccess, started.ErrorMessage);
            await using var client = started.Client!;
            var gateway = new PluginRuntimeGateway([]);
            gateway.Attach([PluginRuntimeEntry.Ok("fake-output", client, started.Handshake!)]);
            using var coordinator = new OutputCoordinator(gateway, NativeSetupJson.CreateDefault());
            var root = JsonNode.Parse(SettingsJson.Serialize(SettingsJson.LoadDefaults().Settings!))!;
            root["profiles"]![0]!["bindings"]![0]!["plugin"] = "fake-output";
            var settings = SettingsJson.Load(root.ToJsonString()).Settings!;

            var status = await coordinator.ApplyAsync(settings, new SelectionStateDocument
            {
                SchemaVersion = 1,
                Mode = "manual",
                ManualProfile = "default",
                LastSelectedProfile = "default",
                UpdatedAt = DateTimeOffset.UtcNow
            }, SelectionReason.Manual(), TestContext.Current.CancellationToken);

            Assert.Equal("desired", status.Effective.Source);
            Assert.Equal("applied", Assert.Single(status.Applied!.Bindings).Outcome);
            Assert.Empty(status.Pending);
        }

        private static Task<PluginStartResult> StartAsync(params string[] arguments)
            => StartAsync(TestContext.Current.CancellationToken, arguments);

        private static Task<PluginStartResult> StartAsync(
            CancellationToken cancellationToken,
            params string[] arguments)
        {
            var launchArguments = new List<string> { FakePluginPath() };
            launchArguments.AddRange(arguments);
            return PluginProcessClient.StartAsync(
                new PluginLaunchSpec { FileName = "dotnet", Arguments = launchArguments },
                Manifest(),
                cancellationToken);
        }

        private static PluginManifest Manifest() => new()
        {
            SchemaVersion = 1,
            Id = "fake-output",
            Version = "0.1.0",
            ContractVersion = ContractVersion.Current,
            EntryPoint = "FocusRgb.FakePlugin.dll",
            OperatingSystems = ["windows"],
            Architectures = ["x64"],
            Roles = ["output"],
            Resources = [],
            License = "MIT"
        };

        private static string FakePluginPath()
        {
            var configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Name;
            return Path.Combine(
                RepositoryRoot(), "build", "artifacts", "bin", "FocusRgb.FakePlugin",
                configuration, "FocusRgb.FakePlugin.dll");
        }

        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                directory = directory.Parent;
            }
            return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }
}
