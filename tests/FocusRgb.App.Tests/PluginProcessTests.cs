using System.Diagnostics;
using System.Text.Json.Nodes;
using FocusRgb.App.Plugins;
using FocusRgb.Core.Protocol;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Coordination;
using FocusRgb.Core.Selection;
using FocusRgb.TestSupport;

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
        public async Task StartAsync_ReturnsContractIncompatible_WhenPluginReportsDifferentMajor()
        {
            var started = await StartAsync("--contract-version", "2.0");

            Assert.False(started.IsSuccess);
            Assert.Equal("contract-incompatible", started.ErrorCode);
        }

        [Fact]
        public async Task StartAsync_RejectsLegacyContract()
        {
            var started = await StartAsync(Manifest("0.1"), "--contract-version", "0.1");

            Assert.False(started.IsSuccess);
            Assert.Equal("contract-incompatible", started.ErrorCode);
        }

        [Fact]
        public async Task StartAsync_AcceptsMissingSdkIdentity()
        {
            var started = await StartAsync();

            Assert.True(started.IsSuccess, started.ErrorMessage);
            Assert.Null(started.Handshake!.Sdk);
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
        public async Task StartAsync_ReturnsPluginExited_WhenOnlyStdoutIsNotJson()
        {
            var started = await StartAsync("--malformed");

            Assert.False(started.IsSuccess);
            Assert.Equal("plugin-exited", started.ErrorCode);
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

        [Fact]
        public async Task StalledStdin_MarksClientUnhealthyAndFailsLaterCallsWithoutThrowing()
        {
            var started = await StartAsync("--stall-after-initialize");
            Assert.True(started.IsSuccess, started.ErrorMessage);
            await using var client = started.Client!;

            var stalled = await client.ListCatalogAsync(
                new string('x', 128 * 1024), TestContext.Current.CancellationToken);
            var startedAt = Stopwatch.GetTimestamp();
            var later = await client.GetStatusAsync(TestContext.Current.CancellationToken);

            Assert.Equal("timeout", stalled.ErrorCode);
            Assert.Equal("timeout", later.ErrorCode);
            Assert.Equal("unhealthy", client.Health);
            Assert.True(Stopwatch.GetElapsedTime(startedAt) < TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task CancellationDuringWrite_DoesNotCorruptLaterRequests()
        {
            var started = await StartAsync("--invalid-response", "delayed-read");
            Assert.True(started.IsSuccess, started.ErrorMessage);
            await using var client = started.Client!;
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ListCatalogAsync(
                new string('x', 128 * 1024), cancellation.Token));
            var status = await client.GetStatusAsync(TestContext.Current.CancellationToken);

            Assert.True(status.IsSuccess, status.ErrorMessage);
            Assert.Equal("healthy", client.Health);
        }

        [Theory]
        [InlineData("method-not-found", "capability-unsupported", -32601)]
        [InlineData("request-cancelled", "request-cancelled", -32800)]
        public async Task JsonRpcError_PreservesNumericCodeAndMapsKnownMeaning(
            string response,
            string expectedCode,
            int expectedJsonRpcCode)
        {
            var started = await StartAsync("--invalid-response", response);
            Assert.True(started.IsSuccess, started.ErrorMessage);
            await using var client = started.Client!;

            var status = await client.GetStatusAsync(TestContext.Current.CancellationToken);

            Assert.Equal(expectedCode, status.ErrorCode);
            Assert.Equal(expectedJsonRpcCode, status.JsonRpcErrorCode);
            Assert.Contains(expectedJsonRpcCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                status.ErrorMessage, StringComparison.Ordinal);
        }

        [Fact]
        public async Task UnknownNotification_IsIgnored()
        {
            var started = await StartAsync("--invalid-response", "unknown-notification");
            Assert.True(started.IsSuccess, started.ErrorMessage);
            await using var client = started.Client!;

            var status = await client.GetStatusAsync(TestContext.Current.CancellationToken);

            Assert.True(status.IsSuccess, status.ErrorMessage);
            Assert.Equal("healthy", client.Health);
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
        public async Task MalformedResponseRaisesStatusChangedForSupervision()
        {
            var started = await StartAsync("--invalid-response", "array");
            Assert.True(started.IsSuccess, started.ErrorMessage);
            await using var client = started.Client!;
            var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            client.StatusChanged += (_, _) => changed.TrySetResult();

            var status = await client.GetStatusAsync(TestContext.Current.CancellationToken);

            Assert.Equal("malformed-response", status.ErrorCode);
            await changed.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.Equal("unhealthy", client.Health);
        }

        [Fact]
        public async Task StrayStdoutLine_DoesNotFailPendingRequest()
        {
            var started = await StartAsync("--invalid-response", "stray");
            Assert.True(started.IsSuccess, started.ErrorMessage);
            await using var client = started.Client!;

            var status = await client.GetStatusAsync(TestContext.Current.CancellationToken);

            Assert.True(status.IsSuccess, status.ErrorMessage);
            Assert.Equal("healthy", client.Health);
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
        public async Task BufferedPluginLogsAreBounded()
        {
            var started = await StartAsync("--stderr-bytes", (600 * 1024).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.True(started.IsSuccess, started.ErrorMessage);
            await using var client = started.Client!;
            var messages = new List<string>();

            client.LogReceived += (_, value) => messages.Add(value);

            Assert.Equal(501, messages.Count);
            Assert.StartsWith("Dropped ", messages[0], StringComparison.Ordinal);
        }

        [Fact]
        public async Task ConcurrentDisposeIsIdempotent()
        {
            var started = await StartAsync();
            Assert.True(started.IsSuccess, started.ErrorMessage);

            await Task.WhenAll(
                started.Client!.DisposeAsync().AsTask(),
                started.Client.DisposeAsync().AsTask());
        }

        [Fact]
        public async Task DisposeTerminatesPluginGrandchildren()
        {
            var pidFile = Path.Combine(Path.GetTempPath(), $"FocusRGB-grandchild-{Guid.NewGuid():N}.pid");
            var started = await StartAsync("--spawn-grandchild", pidFile);
            Assert.True(started.IsSuccess, started.ErrorMessage);
            for (var attempt = 0; attempt < 50 && !File.Exists(pidFile); attempt++)
                await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.True(File.Exists(pidFile));
            var process = Process.GetProcessById(int.Parse(
                await File.ReadAllTextAsync(pidFile, TestContext.Current.CancellationToken),
                System.Globalization.CultureInfo.InvariantCulture));

            await started.Client!.DisposeAsync();

            await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(
                TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(process.HasExited);
            File.Delete(pidFile);
        }

        [Fact]
        public async Task OversizedStderrLine_PublishesTruncationMarker()
        {
            var started = await StartAsync("--stderr-line-bytes", "1100000");
            Assert.True(started.IsSuccess, started.ErrorMessage);
            await using var client = started.Client!;
            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            client.LogReceived += (_, value) => received.TrySetResult(value);

            var message = await received.Task.WaitAsync(TestContext.Current.CancellationToken);

            Assert.Contains("exceeded the 1 MiB limit", message, StringComparison.Ordinal);
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
        public async Task StatusChanged_DuringStatusRead_IsNotLost()
        {
            var started = await StartAsync("--notify-count", "1", "--notify-during-status");
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

            await client.GetStatusAsync(TestContext.Current.CancellationToken);

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
                    await Process.GetProcessById(pid).WaitForExitAsync(TestContext.Current.CancellationToken)
                        .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                }
                catch (ArgumentException)
                {
                }
            }
            finally
            {
                await DeleteWithRetryAsync(pidFile, TestContext.Current.CancellationToken);
            }
        }

        private static async Task DeleteWithRetryAsync(string path, CancellationToken cancellationToken)
        {
            for (var attempt = 0; File.Exists(path); attempt++)
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException) when (attempt < 49)
                {
                    await Task.Delay(20, cancellationToken);
                }
            }
        }

        [Fact]
        public async Task Runtime_ReportsMissingEnabledPackage_WithoutFailingStartup()
        {
            var tempDirectory = new TempDirectory(nameof(PluginProcessTests));
            var directory = tempDirectory.Path;
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
                tempDirectory.Dispose();
            }
        }

        [Fact]
        public async Task Runtime_RestartsOnlyRequestedPlugin_AndKeepsHealthyPeerRunning()
        {
            var tempDirectory = new TempDirectory(nameof(PluginProcessTests));
            var directory = tempDirectory.Path;
            try
            {
                await FakePluginPackage.CreateAsync(directory, TestContext.Current.CancellationToken);
                await using var runtime = await PluginRuntime.StartAsync(
                    directory, ["fake-output", "missing-plugin"], TestContext.Current.CancellationToken);
                var healthy = runtime.Entries.Single(entry => entry.PluginId == "fake-output");
                Assert.True(healthy.IsRunning);

                var restarted = await runtime.RestartAsync(
                    directory, "missing-plugin", TestContext.Current.CancellationToken);

                Assert.False(restarted.IsRunning);
                Assert.Same(healthy, runtime.Entries.Single(entry => entry.PluginId == "fake-output"));
                var status = await healthy.Client!.GetStatusAsync(TestContext.Current.CancellationToken);
                Assert.True(status.IsSuccess, status.ErrorMessage);
            }
            finally
            {
                tempDirectory.Dispose();
            }
        }

        [Fact]
        public async Task Runtime_StopsAndStartsOnlyRequestedPlugin_AndKeepsHealthyPeerRunning()
        {
            var tempDirectory = new TempDirectory(nameof(PluginProcessTests));
            var directory = tempDirectory.Path;
            try
            {
                await FakePluginPackage.CreateAsync(directory, TestContext.Current.CancellationToken);
                await using var runtime = await PluginRuntime.StartAsync(
                    directory, ["fake-output", "missing-plugin"], TestContext.Current.CancellationToken);
                var healthy = runtime.Entries.Single(entry => entry.PluginId == "fake-output");

                Assert.True(await runtime.StopAsync(
                    "missing-plugin", TestContext.Current.CancellationToken));
                Assert.DoesNotContain(runtime.Entries, entry => entry.PluginId == "missing-plugin");
                Assert.Same(healthy, runtime.Entries.Single(entry => entry.PluginId == "fake-output"));

                var started = await runtime.StartPluginAsync(
                    directory, "missing-plugin", TestContext.Current.CancellationToken);

                Assert.False(started.IsRunning);
                Assert.Same(started, runtime.Entries.Single(entry => entry.PluginId == "missing-plugin"));
                Assert.Same(healthy, runtime.Entries.Single(entry => entry.PluginId == "fake-output"));
                var status = await healthy.Client!.GetStatusAsync(TestContext.Current.CancellationToken);
                Assert.True(status.IsSuccess, status.ErrorMessage);
            }
            finally
            {
                tempDirectory.Dispose();
            }
        }

        [Fact]
        public async Task Runtime_StopAsync_StopsRunningPluginAndRemovesItsEntry()
        {
            var tempDirectory = new TempDirectory(nameof(PluginProcessTests));
            var directory = tempDirectory.Path;
            try
            {
                await FakePluginPackage.CreateAsync(directory, TestContext.Current.CancellationToken);
                await using var runtime = await PluginRuntime.StartAsync(
                    directory, ["fake-output"], TestContext.Current.CancellationToken);
                var client = Assert.Single(runtime.Entries).Client!;

                Assert.True(await runtime.StopAsync(
                    "fake-output", TestContext.Current.CancellationToken));

                Assert.Empty(runtime.Entries);
                Assert.Equal("exited", client.Health);
            }
            finally
            {
                tempDirectory.Dispose();
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

        [Fact]
        public async Task Gateway_PreservesPluginExitedAvailabilityReason()
        {
            var started = await StartAsync("--exit-on-status");
            Assert.True(started.IsSuccess, started.ErrorMessage);
            await using var client = started.Client!;
            var gateway = new PluginRuntimeGateway([]);
            gateway.Attach([PluginRuntimeEntry.Ok("fake-output", client, started.Handshake!)]);
            var unavailable = new TaskCompletionSource<PluginAvailabilityChangedEventArgs>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            gateway.AvailabilityChanged += (_, change) =>
            {
                if (!change.IsAvailable) unavailable.TrySetResult(change);
            };

            await client.GetStatusAsync(TestContext.Current.CancellationToken);
            var change = await unavailable.Task.WaitAsync(
                TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.Equal("plugin-exited", change.ReasonCode);
        }

        private static Task<PluginStartResult> StartAsync(params string[] arguments)
            => StartAsync(TestContext.Current.CancellationToken, arguments);

        private static Task<PluginStartResult> StartAsync(
            CancellationToken cancellationToken,
            params string[] arguments)
            => StartAsync(Manifest(), cancellationToken, arguments);

        private static Task<PluginStartResult> StartAsync(
            PluginManifest manifest,
            params string[] arguments)
            => StartAsync(manifest, TestContext.Current.CancellationToken, arguments);

        private static Task<PluginStartResult> StartAsync(
            PluginManifest manifest,
            CancellationToken cancellationToken,
            params string[] arguments)
        {
            var launchArguments = new List<string> { RepositoryPaths.FakePlugin };
            launchArguments.AddRange(arguments);
            return PluginProcessClient.StartAsync(
                new PluginLaunchSpec { FileName = "dotnet", Arguments = launchArguments },
                manifest,
                cancellationToken);
        }

        private static PluginManifest Manifest(string? contractVersion = null) =>
            FakePluginPackage.Manifest(contractVersion: contractVersion ?? ContractVersion.Current);

    }
}
