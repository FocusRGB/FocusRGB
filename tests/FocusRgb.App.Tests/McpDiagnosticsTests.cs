using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FocusRgb.App.Diagnostics;
using FocusRgb.App.Mcp;
using FocusRgb.App.Plugins;
using FocusRgb.Core.Commands;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Coordination;
using FocusRgb.Core.Persistence;
using FocusRgb.Core.Selection;
using FocusRgb.LocalMcp;
using FocusRgb.Mcp;
using FocusRgb.TestSupport;

namespace FocusRgb.App.Tests
{
    [Collection(nameof(LocalMcpTestGroup))]
    public sealed class McpDiagnosticsTests
    {
        [Theory]
        [InlineData("Could not read C:\\Users\\Private Name\\settings.json")]
        [InlineData("Could not read /home/private/settings.json")]
        [InlineData("Could not read \\\\server\\private folder\\settings.json")]
        [InlineData("password=private-value")]
        [InlineData("Authorization: Bearer private-value")]
        [InlineData("api_key=private-value")]
        [InlineData("access_token=private-value")]
        [InlineData("clientSecret=private-value")]
        [InlineData("Connection_String=private-value")]
        [InlineData("pass\u200bword=private-value")]
        [InlineData("private-value https://private.example.test/data")]
        public void DiagnosticText_RedactsSensitiveMessagesBeforeRetention(string value)
        {
            var redacted = DiagnosticText.Clean(value)!;
            Assert.StartsWith("[redacted", redacted, StringComparison.Ordinal);
            Assert.DoesNotContain("private", redacted, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DiagnosticText_BoundsMessagesAndRedactsUnlabelledCredentials()
        {
            var credential = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            Assert.DoesNotContain(credential, DiagnosticText.Clean(credential)!, StringComparison.Ordinal);
            Assert.Equal(256, DiagnosticText.Clean(new string('a', 300))!.Length);
            Assert.Equal("[oversized text omitted]", DiagnosticText.Clean(new string('x', 17000)));
            Assert.Equal("ab", DiagnosticText.Clean("a\n\u202eb"));
            Assert.Equal("device:desk", DiagnosticText.Clean("device:desk"));
            var result = DiagnosticText.Serialize(new { tooMuch = new string('x', DiagnosticText.MaximumResponseBytes) });
            Assert.Equal("truncated", result.GetProperty("status").GetString());
            Assert.True(Encoding.UTF8.GetByteCount(result.GetRawText()) < DiagnosticText.MaximumResponseBytes);
        }

        [Fact]
        public async Task ReadTools_BoundAndRedactAllDiagnosticsWithoutCreatingApproval()
        {
            using var temporary = new TempDirectory(nameof(McpDiagnosticsTests));
            var store = new LocalStateStore(temporary.Path);
            var startup = await store.InitializeAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
            var state = new ApplicationStateService(startup.Snapshot!.Settings, startup.Snapshot.State, startup.Snapshot.Desired.Reason, store);
            var settings = JsonNode.Parse(SettingsJson.Serialize(state.Current.Settings))!;
            settings["application"]!["debuggingMode"] = true;
            Assert.True((await state.ApplySettingsAsync(SettingsJson.Load(settings.ToJsonString()).Settings!,
                state.Current.SettingsRevision, "ui", TestContext.Current.CancellationToken)).IsSuccess);
            var logs = new RecentLogBuffer();
            logs.SetEnabled(true);
            for (var index = 0; index < 230; index++)
            {
                logs.Add($"Event {index}", "warning", "selection");
            }

            logs.Add("password=private-value", "error", "selection");
            logs.Add("unstructured private plugin payload", "info", "fake-output", retainMessage: false);
            using var diagnostics = new McpDiagnostics(state,
                () => Enumerable.Range(0, 80).Select(index => new InstalledPluginInfo
                {
                    Id = $"plugin-{index}", DisplayName = "not projected", Version = "0.1.0",
                    Error = "C:/Private/settings.json", IsOfficial = true
                }).ToArray(), null, null,
                () => Task.FromResult(new DiagnosticUiState("Settings", true, true, true, false,
                    Enumerable.Repeat("Password=private-value", 20).ToArray(), Enumerable.Repeat("C:/Private/toast", 20).ToArray(),
                    Enumerable.Repeat(new DiagnosticUiStatus("settings", "Authorization: Bearer private-value"), 30).ToArray())), logs);
            using var queries = new McpReadQueryService(state, () => [], () => diagnostics);
            var commands = new McpCommandService(state, queries);
            var at = DateTimeOffset.UtcNow;
            for (var index = 0; index < 230; index++)
            {
                await state.ObserveAsync(new ApplicationObservation
                {
                    ForegroundExecutable = "Code.exe", ObservedForegroundExecutable = "explorer.exe",
                    ForegroundWindowClass = "XamlExplorerHostIslandWindow", IgnoredAsTransient = true,
                    RunningExecutables = ["Code.exe"]
                }, at.AddMilliseconds(index), TestContext.Current.CancellationToken);
            }
            await state.ObserveAsync(new ApplicationObservation
            {
                ForegroundExecutable = "C:/Private/private.exe", ObservedForegroundExecutable = "Authorization: Bearer private-value",
                ForegroundWindowClass = "C:/Private/window", IgnoredAsTransient = true
            }, at.AddSeconds(1), TestContext.Current.CancellationToken);
            var configuration = new McpConfigurationStore(temporary.Path);
            var enabled = await configuration.SetEnabledAsync(true, Environment.ProcessPath, TestContext.Current.CancellationToken);
            logs.Add(enabled.Configuration!.Credential!, "warning", "mcp-test");
            await using var server = new LocalMcpPipeServer(enabled.Configuration!.Credential!, commands.DispatchAsync, temporary.Path);
            _ = server.RunAsync();
            var tools = new FocusRgbReadTools(new LocalMcpPipeClient(configuration));
            var trace = await tools.GetSelectionTraceAsync(int.MaxValue, TestContext.Current.CancellationToken);
            Assert.Equal(200, trace.GetProperty("retained").GetInt32());
            Assert.Equal(50, trace.GetProperty("items").GetArrayLength());
            var item = trace.GetProperty("items")[0];
            Assert.Equal("explorer.exe", item.GetProperty("process").GetString());
            Assert.Equal("Code.exe", item.GetProperty("retainedForeground").GetString());
            Assert.Equal("XamlExplorerHostIslandWindow", item.GetProperty("windowClass").GetString());
            Assert.True(item.GetProperty("ignoredAsTransient").GetBoolean());
            Assert.Equal("default", item.GetProperty("profileId").GetString());
            Assert.Equal("manual", item.GetProperty("reason").GetString());
            Assert.Equal(750, item.GetProperty("delayMs").GetInt32());
            Assert.True(trace.GetProperty("truncated").GetBoolean());
            Assert.Equal(1, (await tools.GetSelectionTraceAsync(-1, TestContext.Current.CancellationToken)).GetProperty("items").GetArrayLength());
            var plugins = await tools.GetPluginRuntimeAsync(int.MaxValue, TestContext.Current.CancellationToken);
            Assert.Equal(50, plugins.GetProperty("plugins").GetArrayLength());
            Assert.True(plugins.GetProperty("truncated").GetBoolean());
            var recent = await tools.GetRecentLogsAsync(int.MaxValue, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(50, recent.GetProperty("items").GetArrayLength());
            Assert.Equal(1, (await tools.GetRecentLogsAsync(level: "error", source: "selection",
                cancellationToken: TestContext.Current.CancellationToken)).GetProperty("items").GetArrayLength());
            var ui = await tools.GetUiStateAsync(TestContext.Current.CancellationToken);
            Assert.True(ui.GetProperty("settingsDirty").GetBoolean());
            Assert.Equal(8, ui.GetProperty("dialogs").GetArrayLength());
            Assert.Equal(8, ui.GetProperty("toasts").GetArrayLength());
            Assert.Equal(10, ui.GetProperty("statuses").GetArrayLength());
            var output = await tools.GetOutputStateAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(JsonValueKind.Null, output.GetProperty("writerPlugin").ValueKind);
            Assert.Equal(JsonValueKind.Null, output.GetProperty("lastOutputAt").ValueKind);
            foreach (var result in new[] { trace, plugins, recent, ui, output })
            {
                var json = result.GetRawText();
                Assert.True(Encoding.UTF8.GetByteCount(json) <= DiagnosticText.MaximumResponseBytes);
                Assert.DoesNotContain("private", json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("windowTitle", json, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(enabled.Configuration.Credential!, json, StringComparison.Ordinal);
            }
            Assert.Null(commands.Pending);
            Assert.Equal("manual", state.Current.SelectionState.Mode);
        }

        [Fact]
        public async Task ReadTools_AllReturnDisabledAndCannotEnableMcp()
        {
            using var temporary = new TempDirectory(nameof(McpDiagnosticsTests));
            var store = new McpConfigurationStore(temporary.Path);
            var disabled = await store.SetEnabledAsync(false, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(disabled.IsSuccess);
            var before = await File.ReadAllBytesAsync(Path.Combine(temporary.Path, McpConfigurationStore.FileName), TestContext.Current.CancellationToken);
            var tools = new FocusRgbReadTools(new LocalMcpPipeClient(store));
            foreach (var result in new[]
            {
                await tools.GetSelectionTraceAsync(cancellationToken: TestContext.Current.CancellationToken),
                await tools.GetPluginRuntimeAsync(cancellationToken: TestContext.Current.CancellationToken),
                await tools.GetRecentLogsAsync(cancellationToken: TestContext.Current.CancellationToken),
                await tools.GetUiStateAsync(TestContext.Current.CancellationToken),
                await tools.GetOutputStateAsync(cancellationToken: TestContext.Current.CancellationToken)
            })
            {
                Assert.Equal("disabled", result.GetProperty("status").GetString());
            }
            Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(temporary.Path, McpConfigurationStore.FileName), TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task ReadTools_HideConfigurationErrorsAndAreAnnotatedReadOnly()
        {
            using var temporary = new TempDirectory(nameof(McpDiagnosticsTests));
            await File.WriteAllTextAsync(Path.Combine(temporary.Path, McpConfigurationStore.FileName),
                "invalid secret C:/Private/value", TestContext.Current.CancellationToken);
            var tools = new FocusRgbReadTools(new LocalMcpPipeClient(new McpConfigurationStore(temporary.Path)));
            var result = await tools.GetRecentLogsAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal("unavailable", result.GetProperty("status").GetString());
            Assert.DoesNotContain("private", result.GetRawText(), StringComparison.OrdinalIgnoreCase);
            foreach (var name in new[] { "GetSelectionTraceAsync", "GetPluginRuntimeAsync", "GetRecentLogsAsync", "GetUiStateAsync", "GetOutputStateAsync" })
            {
                var attribute = Assert.Single(typeof(FocusRgbReadTools).GetMethod(name)!
                    .GetCustomAttributes(typeof(ModelContextProtocol.Server.McpServerToolAttribute), false));
                Assert.True(((ModelContextProtocol.Server.McpServerToolAttribute)attribute).ReadOnly);
                var description = Assert.Single(typeof(FocusRgbReadTools).GetMethod(name)!
                    .GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false));
                Assert.Contains("Debugging mode", ((System.ComponentModel.DescriptionAttribute)description).Description, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void AppLog_RetainsRedactedEventsWhenFileLoggingIsOff()
        {
            using var temporary = new TempDirectory(nameof(McpDiagnosticsTests));
            AppLog.Configure(temporary.Path, false, debuggingMode: true);
            AppLog.Write("Password=private-value", "warning", "off-test");
            Assert.Contains("redacted", AppLog.Recent.Read(1, "warning", "off-test").Single().Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(temporary.Path, "logs")));
            AppLog.Configure(temporary.Path, false);
        }

        [Fact]
        public async Task RuntimeAndOutput_ReadActualFakeProcessRestartsAndOwnershipWithoutApprovingRecovery()
        {
            using var temporary = new TempDirectory(nameof(McpDiagnosticsTests));
            var store = new LocalStateStore(temporary.Path);
            var startup = await store.InitializeAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
            var document = JsonNode.Parse(SettingsJson.Serialize(startup.Snapshot!.Settings))!;
            foreach (var profile in document["profiles"]!.AsArray())
            {
                var binding = profile!["bindings"]![0]!;
                binding["plugin"] = "fake-output";
                binding["effect"] = new JsonObject { ["resourceId"] = "fake-aurora", ["displayName"] = "C:/Private/effect" };
                binding["layout"] = null;
            }
            var settings = SettingsJson.Load(document.ToJsonString()).Settings!;
            var packages = Path.Combine(temporary.Path, "plugins");
            await FakePluginPackage.CreateAsync(packages, TestContext.Current.CancellationToken);
            await using var runtime = await PluginRuntime.StartAsync(packages, ["fake-output"], TestContext.Current.CancellationToken);
            var entry = Assert.Single(runtime.Entries);
            Assert.True(entry.IsRunning, entry.ErrorMessage);
            var gateway = new PluginRuntimeGateway(runtime.Entries);
            using var coordinator = new OutputCoordinator(gateway, NativeSetupJson.CreateDefault());
            var applied = await coordinator.ApplyAsync(settings, startup.Snapshot.State, startup.Snapshot.Desired.Reason,
                TestContext.Current.CancellationToken);
            var state = new ApplicationStateService(settings, startup.Snapshot.State, startup.Snapshot.Desired.Reason, store, coordinator, applied);
            using var inventory = new PluginInventory(packages);
            using var diagnostics = new McpDiagnostics(state, () => inventory.List(["fake-output"], runtime.Entries), () => runtime.Entries, coordinator);
            diagnostics.RecordPluginStart(entry);
            diagnostics.RecordCatalog(OfficialPluginCatalogResult.Cached(DateTimeOffset.UtcNow.AddMinutes(-5), [], "Cached catalog"));
            var output = await diagnostics.QueryAsync("getOutputState", null, TestContext.Current.CancellationToken);
            Assert.Equal("fake-output", output.GetProperty("writerPlugin").GetString());
            Assert.Equal("provider", output.GetProperty("renderingAuthority").GetString());
            Assert.Equal("fake-aurora", output.GetProperty("effectResourceId").GetString());
            Assert.DoesNotContain("private", output.GetRawText(), StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(JsonValueKind.Null, output.GetProperty("lastOutputAt").ValueKind);
            Assert.Equal("provider-managed", output.GetProperty("writers")[0].GetProperty("scope").GetString());
            var pid = entry.Client!.ProcessId;
            diagnostics.RecordPluginError(entry.PluginId, "plugin-exited");
            var restarted = await runtime.RestartAsync(packages, "fake-output", TestContext.Current.CancellationToken);
            Assert.True(restarted.IsRunning, restarted.ErrorMessage);
            diagnostics.RecordPluginStart(restarted);
            diagnostics.RecordPluginStart(restarted);
            await coordinator.ReportAvailabilityAsync("fake-output", false, "plugin-exited", "Stopped", TestContext.Current.CancellationToken);
            await coordinator.ReportAvailabilityAsync("fake-output", true, cancellationToken: TestContext.Current.CancellationToken);
            var result = await diagnostics.QueryAsync("getPluginRuntime", null, TestContext.Current.CancellationToken);
            var plugin = result.GetProperty("plugins")[0];
            Assert.NotEqual(pid, plugin.GetProperty("processId").GetInt32());
            Assert.Equal(1, plugin.GetProperty("restartCount").GetInt32());
            Assert.Equal("plugin-exited", plugin.GetProperty("lastError").GetString());
            Assert.Equal("custom", plugin.GetProperty("origin").GetString());
            Assert.NotEqual(JsonValueKind.Null, plugin.GetProperty("startTime").ValueKind);
            Assert.True(plugin.GetProperty("pendingRecoveryApproval").GetBoolean());
            Assert.True(result.GetProperty("catalog").GetProperty("ageSeconds").GetDouble() >= 300);
            output = await diagnostics.QueryAsync("getOutputState", null, TestContext.Current.CancellationToken);
            Assert.Equal(JsonValueKind.Null, output.GetProperty("writerPlugin").ValueKind);
            Assert.NotEqual(JsonValueKind.Null, output.GetProperty("lastOutputAt").ValueKind);
        }
    }
}
