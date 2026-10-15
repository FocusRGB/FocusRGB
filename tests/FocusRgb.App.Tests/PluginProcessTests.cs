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

            var status = await client.GetStatusAsync(TestContext.Current.CancellationToken);

            Assert.False(status.IsSuccess);
            Assert.Equal("plugin-exited", status.ErrorCode);
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
            var gateway = new PluginRuntimeGateway(
                [PluginRuntimeEntry.Ok("fake-output", client, started.Handshake!)]);
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
        {
            var launchArguments = new List<string> { FakePluginPath() };
            launchArguments.AddRange(arguments);
            return PluginProcessClient.StartAsync(
                new PluginLaunchSpec { FileName = "dotnet", Arguments = launchArguments },
                Manifest(),
                TestContext.Current.CancellationToken);
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
