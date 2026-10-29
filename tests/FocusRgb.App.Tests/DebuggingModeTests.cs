using System.Text.Json;
using System.Text.Json.Nodes;
using FocusRgb.App.Diagnostics;
using FocusRgb.App.Mcp;
using FocusRgb.App.Plugins;
using FocusRgb.App.ViewModels;
using FocusRgb.Core.Commands;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Persistence;
using FocusRgb.Core.Selection;
using FocusRgb.LocalMcp;
using FocusRgb.TestSupport;

namespace FocusRgb.App.Tests
{
    [Collection(nameof(LocalMcpTestGroup))]
    public sealed class DebuggingModeTests
    {
        [Fact]
        public async Task RuntimeToggle_CreatesAndDisposesRecordersWithoutAffectingControlPipe()
        {
            using var directory = new TempDirectory(nameof(DebuggingModeTests));
            var store = new LocalStateStore(directory.Path);
            var loaded = (await store.InitializeAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken)).Snapshot!;
            var state = new ApplicationStateService(loaded.Settings, loaded.State, loaded.Desired.Reason, store);
            IReadOnlyList<InstalledPluginInfo> Plugins() => [new InstalledPluginInfo { Id = "example", DisplayName = "Example", Version = "0.1.0" }];
            var created = new List<McpDiagnostics>();
            using var queries = new McpReadQueryService(state, Plugins, () =>
            {
                var recorder = new McpDiagnostics(state, Plugins);
                created.Add(recorder);
                return recorder;
            });
            var commands = new McpCommandService(state, queries);
            var config = new McpConfigurationStore(directory.Path);
            var enabled = await config.SetEnabledAsync(true, Environment.ProcessPath, TestContext.Current.CancellationToken);
            var configBefore = await File.ReadAllBytesAsync(Path.Combine(directory.Path, McpConfigurationStore.FileName), TestContext.Current.CancellationToken);
            await using var server = new LocalMcpPipeServer(enabled.Configuration!.Credential!, commands.DispatchAsync, directory.Path);
            _ = server.RunAsync();
            var client = new LocalMcpPipeClient(config);
            Assert.Empty(created);
            Assert.Equal(0, ObservationHandlers(state));
            for (var cycle = 0; cycle < 3; cycle++)
            {
                foreach (var method in new[] { "getSelectionTrace", "getPluginRuntime", "getRecentLogs", "getUiState", "getOutputState" })
                    Assert.Equal("disabled", (await client.QueryAsync(method, TestContext.Current.CancellationToken)).GetProperty("status").GetString());
                Assert.NotEmpty((await client.QueryAsync("listProfiles", TestContext.Current.CancellationToken)).EnumerateArray());
                Assert.True((await client.QueryAsync("getStatus", TestContext.Current.CancellationToken)).TryGetProperty("runtimeStatus", out _));
                foreach (var method in new[] { "listApplicationGroups", "listMappings", "listPlugins", "listDevices", "explainActiveSelection" })
                    Assert.NotEqual(JsonValueKind.Undefined, (await client.QueryAsync(method, TestContext.Current.CancellationToken)).ValueKind);

                await SetModeAsync(state, true);
                var recorder = Assert.IsType<McpDiagnostics>(queries.Diagnostics);
                Assert.Equal(cycle + 1, created.Count);
                Assert.Equal(1, ObservationHandlers(state));
                // Other snapshots must not add another observation handler.
                await state.ObserveAsync(new ApplicationObservation(), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
                await state.ObserveAsync(new ApplicationObservation(), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
                var trace = await client.QueryAsync("getSelectionTrace", TestContext.Current.CancellationToken);
                Assert.Equal(2, trace.GetProperty("retained").GetInt32());
                recorder.RecordPluginError("example", "example-error");
                AppLog.Write("Event while debugging", source: "debugging-test");
                Assert.NotEmpty(AppLog.Recent.Read(20, null, "debugging-test"));

                await SetModeAsync(state, false);
                Assert.Null(queries.Diagnostics);
                Assert.Equal(0, ObservationHandlers(state));
                AppLog.Write("Must not retain", source: "debugging-test");
                await state.ObserveAsync(new ApplicationObservation(), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
                Assert.Empty(AppLog.Recent.Read(20, null, null));
                var cleared = await recorder.QueryAsync("getSelectionTrace", null, TestContext.Current.CancellationToken);
                Assert.Equal(0, cleared.GetProperty("retained").GetInt32());
                recorder.RecordPluginError("example", "late-error-must-not-be-retained");
                var runtime = await recorder.QueryAsync("getPluginRuntime", null, TestContext.Current.CancellationToken);
                Assert.Equal(JsonValueKind.Null, runtime.GetProperty("plugins")[0].GetProperty("lastError").ValueKind);
                Assert.Equal(configBefore, await File.ReadAllBytesAsync(Path.Combine(directory.Path, McpConfigurationStore.FileName), TestContext.Current.CancellationToken));
            }
            queries.Dispose();
            await SetModeAsync(state, true);
            Assert.Equal(3, created.Count);
            Assert.Null(queries.Diagnostics);
        }

        [Fact]
        public async Task RuntimeToggle_DiscardsInFlightDiagnosticResultWhenModeTurnsOff()
        {
            using var directory = new TempDirectory(nameof(DebuggingModeTests));
            var store = new LocalStateStore(directory.Path);
            var loaded = (await store.InitializeAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken)).Snapshot!;
            var state = new ApplicationStateService(loaded.Settings, loaded.State, loaded.Desired.Reason, store);
            await SetModeAsync(state, true);
            var ui = new TaskCompletionSource<DiagnosticUiState>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var queries = new McpReadQueryService(state, () => [], () =>
                new McpDiagnostics(state, () => [], ui: () => ui.Task));
            var pending = queries.QueryAsync("getUiState", TestContext.Current.CancellationToken);
            Assert.False(pending.IsCompleted);
            await SetModeAsync(state, false);
            ui.SetResult(new DiagnosticUiState("Settings", false, false, false, true, [], [], []));
            Assert.Equal("disabled", (await pending).GetProperty("status").GetString());
            Assert.Equal(0, ObservationHandlers(state));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Mcp_CannotChangeDebuggingModeInEitherDirection(bool initial)
        {
            using var directory = new TempDirectory(nameof(DebuggingModeTests));
            var store = new LocalStateStore(directory.Path);
            var loaded = (await store.InitializeAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken)).Snapshot!;
            var state = new ApplicationStateService(loaded.Settings, loaded.State, loaded.Desired.Reason, store);
            await SetModeAsync(state, initial);
            using var queries = new McpReadQueryService(state, () => []);
            var commands = new McpCommandService(state, queries);
            var candidate = Candidate(state, !initial);
            var request = new LocalMcpRequest
            {
                Method = "applySettings",
                Parameters = JsonSerializer.SerializeToElement(new { settings = candidate, expectedRevision = state.Current.SettingsRevision }, JsonSerializerOptions.Web)
            };
            var result = await commands.DispatchAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal("debugging-mode-changes-not-allowed", result.GetProperty("error").GetProperty("code").GetString());
            Assert.Null(commands.Pending);
            Assert.Equal(initial, state.Current.Settings.Application!.DebuggingMode);
        }

        [Fact]
        public async Task SettingsSave_OnlySuccessfulPersistenceStartsOrStopsDiagnostics()
        {
            using var directory = new TempDirectory(nameof(DebuggingModeTests));
            var store = new LocalStateStore(directory.Path);
            var loaded = (await store.InitializeAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken)).Snapshot!;
            var state = new ApplicationStateService(loaded.Settings, loaded.State, loaded.Desired.Reason, store);
            using var queries = new McpReadQueryService(state, () => []);
            var model = MainWindowViewModel.CreatePersistent(loaded, store, applicationState: state);
            model.DebuggingMode = true;
            model.VerboseLogging = true;
            Assert.Null(queries.Diagnostics);
            using (var locked = new FileStream(Path.Combine(directory.Path, LocalStateStore.SettingsFileName), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.False(await model.SaveApplicationSettingsAsync(TestContext.Current.CancellationToken));
                Assert.Null(queries.Diagnostics);
                Assert.True(model.IsApplicationSettingsDirty);
            }
            Assert.True(await model.SaveApplicationSettingsAsync(TestContext.Current.CancellationToken));
            Assert.NotNull(queries.Diagnostics);
            model.DebuggingMode = false;
            Assert.NotNull(queries.Diagnostics);
            Assert.True(await model.SaveApplicationSettingsAsync(TestContext.Current.CancellationToken));
            Assert.Null(queries.Diagnostics);
            Assert.True(model.SavedVerboseLogging);
        }

        [Fact]
        public void Logging_OffClearsAndStopsBothMemoryAndFileLogging()
        {
            using var directory = new TempDirectory(nameof(DebuggingModeTests));
            try
            {
                AppLog.Configure(directory.Path, true, true);
                AppLog.Write("Enabled event");
                var path = Path.Combine(directory.Path, "logs", "focusrgb.log");
                var before = File.ReadAllText(path);
                Assert.NotEmpty(AppLog.Recent.Read(20, null, null));
                AppLog.Configure(directory.Path, true, false);
                AppLog.Write("Disabled event", "error");
                Assert.Empty(AppLog.Recent.Read(20, null, null));
                Assert.Equal(before, File.ReadAllText(path));
            }
            finally { AppLog.Configure(directory.Path, false); }
        }

        private static SettingsDocument Candidate(ApplicationStateService state, bool enabled)
        {
            var json = JsonNode.Parse(SettingsJson.Serialize(state.Current.Settings))!;
            json["application"]!["debuggingMode"] = enabled;
            json["application"]!["verboseLogging"] = false;
            return SettingsJson.Load(json.ToJsonString()).Settings!;
        }

        private static int ObservationHandlers(ApplicationStateService state) =>
            ((Delegate?)typeof(ApplicationStateService).GetField("ObservationEvaluated",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(state))
                ?.GetInvocationList().Length ?? 0;

        private static async Task SetModeAsync(ApplicationStateService state, bool enabled)
            => Assert.True((await state.ApplySettingsAsync(Candidate(state, enabled), state.Current.SettingsRevision,
                "ui", TestContext.Current.CancellationToken)).IsSuccess);
    }
}
