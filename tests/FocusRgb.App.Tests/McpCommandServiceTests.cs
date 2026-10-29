using System.Security.Cryptography;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using FocusRgb.App.Mcp;
using FocusRgb.Core.Commands;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Coordination;
using FocusRgb.Core.Persistence;
using FocusRgb.Core.Protocol;
using FocusRgb.LocalMcp;

namespace FocusRgb.App.Tests
{
    [Collection(nameof(LocalMcpTestGroup))]
    public sealed class McpCommandServiceTests : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(), "FocusRgb.Tests", Guid.NewGuid().ToString("N"));

        [Fact]
        public async Task PreviewApproval_DisconnectAfterApprovalEndsOverrideAndPreservesSavedIntent()
        {
            using var coordinator = new OutputCoordinator(new RecoveryGateway(), NativeSetupJson.CreateDefault());
            var (state, commands) = await CreateAsync(coordinator: coordinator);
            McpApprovalRequest? approval = null;
            commands.ApprovalRequested += (_, value) => approval = value;
            var saved = await File.ReadAllBytesAsync(Path.Combine(_directory, LocalStateStore.StateFileName), TestContext.Current.CancellationToken);
            using var disconnected = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var request = commands.DispatchAsync(Request("previewProfile", new { profile = "movie", durationSeconds = 30 }), disconnected.Token);
            await WaitForAsync(() => approval is not null);
            commands.Approve(approval!.Id);
            await WaitForAsync(() => state.Current.Preview is not null);
            var status = await new McpReadQueryService(state, () => []).QueryAsync("getStatus", TestContext.Current.CancellationToken);
            Assert.Equal("movie", status.GetProperty("preview").GetProperty("profileId").GetString());
            disconnected.Cancel();
            Assert.True((await request).GetProperty("ok").GetBoolean());
            Assert.Null(state.Current.Preview);
            Assert.Equal("default", state.Current.SelectionState.ManualProfile);
            Assert.Equal(saved, await File.ReadAllBytesAsync(Path.Combine(_directory, LocalStateStore.StateFileName), TestContext.Current.CancellationToken));
            Assert.False(File.Exists(Path.Combine(_directory, LocalStateStore.PreviewJournalFileName)));
        }

        [Fact]
        public async Task PreviewApproval_DeclineDoesNotChangeOutputOrSavedState()
        {
            var (state, commands) = await CreateAsync();
            McpApprovalRequest? approval = null;
            commands.ApprovalRequested += (_, value) => approval = value;
            var request = commands.DispatchAsync(Request("previewProfile", new { profile = "movie", durationSeconds = 10 }),
                TestContext.Current.CancellationToken);
            await WaitForAsync(() => approval is not null);
            Assert.Contains("real output", approval!.Summary, StringComparison.Ordinal);
            Assert.Contains("10 seconds", approval.Summary, StringComparison.Ordinal);
            commands.Decline(approval.Id);
            var result = await request;
            Assert.Equal("approval-declined", result.GetProperty("error").GetProperty("code").GetString());
            Assert.Null(state.Current.Preview);
            Assert.Equal("default", state.Current.SelectionState.ManualProfile);
            Assert.False(File.Exists(Path.Combine(_directory, LocalStateStore.PreviewJournalFileName)));
        }

        [Fact]
        public async Task PreviewApproval_RechecksRevisionAndNeverApprovesThroughMcp()
        {
            var (state, commands) = await CreateAsync();
            McpApprovalRequest? approval = null;
            commands.ApprovalRequested += (_, value) => approval = value;
            var request = commands.DispatchAsync(Request("previewProfile", new { profile = "movie", durationSeconds = 1 }),
                TestContext.Current.CancellationToken);
            await WaitForAsync(() => approval is not null);
            var unknown = await commands.DispatchAsync(Request("approve", new { id = approval!.Id }), TestContext.Current.CancellationToken);
            Assert.Equal("method-not-found", unknown.GetProperty("error").GetProperty("code").GetString());
            await state.ApplySettingsAsync(state.Current.Settings, state.Current.SettingsRevision, "ui", TestContext.Current.CancellationToken);
            commands.Approve(approval.Id);
            var result = await request;
            Assert.Equal("conflict", result.GetProperty("error").GetProperty("code").GetString());
            Assert.Null(state.Current.Preview);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(31)]
        public async Task PreviewApproval_RejectsInvalidDurationBeforePrompt(int durationSeconds)
        {
            var (_, commands) = await CreateAsync();
            var result = await commands.DispatchAsync(Request("previewProfile", new { profile = "movie", durationSeconds }),
                TestContext.Current.CancellationToken);
            Assert.Equal("invalid-duration", result.GetProperty("error").GetProperty("code").GetString());
            Assert.Null(commands.Pending);
        }

        [Fact]
        public async Task PreviewApproval_RejectsNonNumericDurationWithoutPrompt()
        {
            var (_, commands) = await CreateAsync();
            var result = await commands.DispatchAsync(Request("previewProfile", new { profile = "movie", durationSeconds = "30" }),
                TestContext.Current.CancellationToken);
            Assert.Equal("invalid-duration", result.GetProperty("error").GetProperty("code").GetString());
            Assert.Null(commands.Pending);
        }

        [Fact]
        public async Task Approval_DoesNotHoldCommandGate_AndRechecksRevisionBeforeExecution()
        {
            var (state, commands) = await CreateAsync();
            McpApprovalRequest? approval = null;
            commands.ApprovalRequested += (_, request) => approval = request;
            var request = commands.DispatchAsync(
                Request("selectProfile", new { profile = "movie" }),
                TestContext.Current.CancellationToken);
            await WaitForAsync(() => approval is not null);

            var changed = WithTheme(state.Current.Settings, "dark");
            var uiWrite = await state.ApplySettingsAsync(
                changed, state.Current.SettingsRevision, "ui", TestContext.Current.CancellationToken);
            commands.Approve(approval!.Id);
            var result = await request;

            Assert.True(uiWrite.IsSuccess);
            Assert.False(result.GetProperty("ok").GetBoolean());
            Assert.Equal("conflict", result.GetProperty("error").GetProperty("code").GetString());
            Assert.Equal("default", state.Current.SelectionState.ManualProfile);
        }

        [Fact]
        public async Task Approval_AllowsOnePendingRequest_AndAppliesDeclineCooldown()
        {
            var (_, commands) = await CreateAsync(declineCooldown: TimeSpan.FromSeconds(1));
            McpApprovalRequest? approval = null;
            commands.ApprovalRequested += (_, request) => approval = request;
            var first = commands.DispatchAsync(
                Request("setMode", new { mode = "automatic" }),
                TestContext.Current.CancellationToken);
            await WaitForAsync(() => approval is not null);

            var concurrent = await commands.DispatchAsync(
                Request("selectProfile", new { profile = "movie" }),
                TestContext.Current.CancellationToken);
            commands.Decline(approval!.Id);
            var declined = await first;
            var cooldown = await commands.DispatchAsync(
                Request("selectProfile", new { profile = "movie" }),
                TestContext.Current.CancellationToken);

            Assert.Equal("busy", concurrent.GetProperty("error").GetProperty("code").GetString());
            Assert.Equal("approval-declined", declined.GetProperty("error").GetProperty("code").GetString());
            Assert.Equal("busy", cooldown.GetProperty("error").GetProperty("code").GetString());
        }

        [Fact]
        public async Task Pipe_ServesReadAndBusyWriteWhileApprovalIsPending()
        {
            var (_, commands) = await CreateAsync();
            McpApprovalRequest? approval = null;
            commands.ApprovalRequested += (_, request) => approval = request;
            var credential = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            await using var server = new LocalMcpPipeServer(credential, commands.DispatchAsync, _directory);
            _ = server.RunAsync();
            var client = new LocalMcpPipeClient(credential, Environment.ProcessPath!, _directory);
            var pending = client.RequestAsync(
                "selectProfile", new { profile = "movie" }, TestContext.Current.CancellationToken);
            await WaitForAsync(() => approval is not null);

            var status = await client.QueryAsync("getStatus", TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            var concurrent = await client.RequestAsync(
                    "setMode", new { mode = "automatic" }, TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            commands.Decline(approval!.Id);
            var declined = await pending;

            Assert.Equal("manual", status.GetProperty("selectionState").GetProperty("mode").GetString());
            Assert.Equal("busy", concurrent.GetProperty("error").GetProperty("code").GetString());
            Assert.Equal("approval-declined", declined.GetProperty("error").GetProperty("code").GetString());
        }

        [Fact]
        public async Task ApplySettings_ShowsReadableBoundSummary_AndReturnsSharedStatus()
        {
            var (state, commands) = await CreateAsync();
            McpApprovalRequest? approval = null;
            commands.ApprovalRequested += (_, request) => approval = request;
            var revision = state.Current.SettingsRevision;
            var candidate = WithTheme(state.Current.Settings, "dark");
            var task = commands.DispatchAsync(
                Request("applySettings", new { settings = candidate, expectedRevision = revision }),
                TestContext.Current.CancellationToken);
            await WaitForAsync(() => approval is not null);

            Assert.Contains("application preferences changed", approval!.Summary, StringComparison.Ordinal);
            Assert.Equal(64, approval.PayloadHash.Length);
            Assert.Equal(revision, approval.ExpectedRevision);
            commands.Approve(approval.Id);
            var result = await task;

            Assert.True(result.GetProperty("ok").GetBoolean());
            Assert.Equal(2, result.GetProperty("status").GetProperty("settingsRevision").GetInt64());
            Assert.Equal("dark", state.Current.Settings.Application!.Theme);
        }

        [Fact]
        public async Task ApplySettings_SummarizesRuleEditsWithoutReordering()
        {
            var (state, commands) = await CreateAsync();
            McpApprovalRequest? approval = null;
            commands.ApprovalRequested += (_, request) => approval = request;
            var candidate = Clone(state.Current.Settings);
            var rule = candidate.Rules![0];
            candidate.Rules[0] = new RuleSettings
            {
                Id = rule.Id,
                ApplicationGroup = "music",
                Profile = "music",
                Match = rule.Match,
                Eligibility = rule.Eligibility == "while-running" ? "while-focused" : "while-running",
                Priority = rule.Priority,
                Enabled = rule.Enabled != true,
                AdditionalProperties = rule.AdditionalProperties
            };
            var task = commands.DispatchAsync(
                Request("applySettings", new
                {
                    settings = candidate,
                    expectedRevision = state.Current.SettingsRevision
                }),
                TestContext.Current.CancellationToken);
            await WaitForAsync(() => approval is not null);

            Assert.Contains($"changed Focuses: {rule.Id}", approval!.Summary, StringComparison.Ordinal);
            commands.Decline(approval.Id);
            await task;
        }

        [Fact]
        public async Task ApplySettings_RejectsPluginEnablementChanges()
        {
            var (state, commands) = await CreateAsync();
            var originalEnabled = state.Current.Settings.Plugins!.Enabled!.ToArray();
            var candidate = Clone(state.Current.Settings);
            candidate = new SettingsDocument
            {
                SchemaVersion = candidate.SchemaVersion,
                Application = candidate.Application,
                Plugins = new PluginSettings { Enabled = [.. candidate.Plugins!.Enabled!, "other-plugin"] },
                Selection = candidate.Selection,
                Profiles = candidate.Profiles,
                ApplicationGroups = candidate.ApplicationGroups,
                Rules = candidate.Rules,
                AdditionalProperties = candidate.AdditionalProperties
            };

            var result = await commands.DispatchAsync(
                Request("applySettings", new
                {
                    settings = candidate,
                    expectedRevision = state.Current.SettingsRevision
                }),
                TestContext.Current.CancellationToken);

            Assert.Equal("plugin-changes-not-allowed", result.GetProperty("error").GetProperty("code").GetString());
            Assert.Equal(originalEnabled, state.Current.Settings.Plugins!.Enabled);
            Assert.Null(commands.Pending);
        }

        [Fact]
        public async Task ApplySettings_RejectsApprovalSummaryControlCharacters()
        {
            var (state, commands) = await CreateAsync();
            var candidate = Clone(state.Current.Settings);
            var profile = candidate.Profiles![0];
            candidate.Profiles[0] = new ProfileSettings
            {
                Id = profile.Id,
                DisplayName = "Trusted\nForged approval line",
                Bindings = profile.Bindings,
                Fallback = profile.Fallback,
                AdditionalProperties = profile.AdditionalProperties
            };

            var result = await commands.DispatchAsync(Request("applySettings", new
            {
                settings = candidate,
                expectedRevision = state.Current.SettingsRevision
            }), TestContext.Current.CancellationToken);

            Assert.Equal("invalid-settings", result.GetProperty("error").GetProperty("code").GetString());
            Assert.Null(commands.Pending);
        }

        [Fact]
        public async Task ReorderMappings_RejectsNonStringIdsAsInvalidParameters()
        {
            var (state, commands) = await CreateAsync();

            var result = await commands.DispatchAsync(Request("reorderMappings", new
            {
                mappingIds = new object[] { 1, "movie", "music" },
                expectedRevision = state.Current.SettingsRevision
            }), TestContext.Current.CancellationToken);

            Assert.Equal("invalid-parameters", result.GetProperty("error").GetProperty("code").GetString());
        }

        [Fact]
        public async Task Approval_SanitizesClientName_AndRejectsDuplicateOrStaleDecisions()
        {
            var (_, commands) = await CreateAsync();
            McpApprovalRequest? approval = null;
            commands.ApprovalRequested += (_, request) => approval = request;
            var request = Request("selectProfile", new { profile = "movie" });
            request = new LocalMcpRequest
            {
                ProtocolVersion = request.ProtocolVersion,
                Id = request.Id,
                ClientName = "Trusted\nForged approval line",
                Method = request.Method,
                Parameters = request.Parameters
            };
            var pending = commands.DispatchAsync(request, TestContext.Current.CancellationToken);
            await WaitForAsync(() => approval is not null);

            Assert.DoesNotContain('\n', approval!.ClientName);
            Assert.False(commands.Approve("stale"));
            Assert.True(commands.Decline(approval.Id));
            Assert.False(commands.Decline(approval.Id));
            Assert.Equal("approval-declined",
                (await pending).GetProperty("error").GetProperty("code").GetString());
        }

        [Fact]
        public async Task DeclinePending_ClearsRequestWithoutApplyingIt()
        {
            var (state, commands) = await CreateAsync();
            var pending = commands.DispatchAsync(
                Request("selectProfile", new { profile = "movie" }),
                TestContext.Current.CancellationToken);
            await WaitForAsync(() => commands.Pending is not null);

            commands.DeclinePending();
            var result = await pending;

            Assert.Equal("approval-declined", result.GetProperty("error").GetProperty("code").GetString());
            Assert.Null(commands.Pending);
            Assert.Equal("default", state.Current.SelectionState.ManualProfile);
        }

        [Fact]
        public async Task RespondToNotification_UsesTheSameApprovalPathOverMcp()
        {
            var store = new LocalStateStore(_directory);
            var startup = await store.InitializeAsync(
                new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero),
                TestContext.Current.CancellationToken);
            using var coordinator = new OutputCoordinator(new RecoveryGateway(), NativeSetupJson.CreateDefault());
            var state = new ApplicationStateService(
                startup.Snapshot!.Settings,
                startup.Snapshot.State,
                startup.Snapshot.Desired.Reason,
                store,
                coordinator);
            var commands = new McpCommandService(state, new McpReadQueryService(state, () => []));
            await state.ReapplyAsync(cancellationToken: TestContext.Current.CancellationToken);
            await coordinator.ReportAvailabilityAsync(
                "signalrgb", false, TestContext.Current.CancellationToken);
            await coordinator.ReportAvailabilityAsync(
                "signalrgb", true, TestContext.Current.CancellationToken);
            var notification = Assert.Single(state.Current.RuntimeStatus!.Notifications);
            var pending = commands.DispatchAsync(Request("respondToNotification", new
            {
                id = notification.Id,
                response = "decline"
            }), TestContext.Current.CancellationToken);
            await WaitForAsync(() => commands.Pending is not null);

            commands.Approve(commands.Pending!.Id);
            var result = await pending;

            Assert.True(result.GetProperty("ok").GetBoolean());
            Assert.Empty(state.Current.RuntimeStatus!.Notifications);
        }

        [Fact]
        public async Task ReorderMappings_UsesCompleteCandidateAndPreservesRequestedOrder()
        {
            var (state, commands) = await CreateAsync();
            McpApprovalRequest? approval = null;
            commands.ApprovalRequested += (_, request) => approval = request;
            var original = state.Current.Settings.Rules!.Select(rule => rule.Id!).ToArray();
            var requested = original.Reverse().ToArray();
            var task = commands.DispatchAsync(
                Request("reorderMappings", new
                {
                    mappingIds = requested,
                    expectedRevision = state.Current.SettingsRevision
                }),
                TestContext.Current.CancellationToken);
            await WaitForAsync(() => approval is not null);

            Assert.Contains("Focus order", approval!.Summary, StringComparison.Ordinal);
            commands.Approve(approval.Id);
            var result = await task;

            Assert.True(result.GetProperty("ok").GetBoolean());
            Assert.Equal(requested, state.Current.Settings.Rules!.Select(rule => rule.Id));
        }

        [Fact]
        public async Task TargetedProfileAndGroupEdits_UseApplySettingsAndReadableDiffs()
        {
            var (state, commands) = await CreateAsync();
            McpApprovalRequest? approval = null;
            commands.ApprovalRequested += (_, request) => approval = request;
            var sourceProfile = state.Current.Settings.Profiles!.Single(profile => profile.Id == "movie");
            var sourceBinding = Assert.Single(sourceProfile.Bindings!);
            var updatedProfile = new ProfileSettings
            {
                Id = sourceProfile.Id,
                DisplayName = "Cinema",
                Bindings =
                [
                    new BindingSettings
                    {
                        Id = sourceBinding.Id,
                        Plugin = sourceBinding.Plugin,
                        RenderingAuthority = sourceBinding.RenderingAuthority,
                        DeviceScope = sourceBinding.DeviceScope,
                        Effect = new ResourceReference { ResourceId = "cinema", DisplayName = "Cinema" },
                        Layout = sourceBinding.Layout,
                        AdditionalProperties = sourceBinding.AdditionalProperties
                    }
                ],
                Fallback = sourceProfile.Fallback,
                AdditionalProperties = sourceProfile.AdditionalProperties
            };
            var profileTask = commands.DispatchAsync(
                Request("upsertProfile", new
                {
                    profile = updatedProfile,
                    expectedRevision = state.Current.SettingsRevision
                }),
                TestContext.Current.CancellationToken);
            await WaitForAsync(() => approval is not null);

            Assert.Contains("changed profiles: \"Cinema\"", approval!.Summary, StringComparison.Ordinal);
            Assert.Contains("effect/layout", approval.Summary, StringComparison.Ordinal);
            commands.Approve(approval.Id);
            Assert.True((await profileTask).GetProperty("ok").GetBoolean());

            approval = null;
            var sourceGroup = state.Current.Settings.ApplicationGroups!.Single(group => group.Id == "movie");
            var updatedGroup = new ApplicationGroupSettings
            {
                Id = sourceGroup.Id,
                DisplayName = "Cinema apps",
                Executables = sourceGroup.Executables,
                AdditionalProperties = sourceGroup.AdditionalProperties
            };
            var groupTask = commands.DispatchAsync(
                Request("upsertApplicationGroup", new
                {
                    applicationGroup = updatedGroup,
                    expectedRevision = state.Current.SettingsRevision
                }),
                TestContext.Current.CancellationToken);
            await WaitForAsync(() => approval is not null);

            Assert.Contains("changed application groups: \"Cinema apps\"", approval!.Summary, StringComparison.Ordinal);
            commands.Approve(approval.Id);
            Assert.True((await groupTask).GetProperty("ok").GetBoolean());
            Assert.Equal("Cinema", state.Current.Settings.Profiles!.Single(profile => profile.Id == "movie").DisplayName);
            Assert.Equal("Cinema apps", state.Current.Settings.ApplicationGroups!.Single(group => group.Id == "movie").DisplayName);
        }

        [Fact]
        public async Task Approval_ExpiryIsSideEffectFree()
        {
            var (state, commands) = await CreateAsync(approvalTimeout: TimeSpan.FromMilliseconds(50));

            var result = await commands.DispatchAsync(
                Request("selectProfile", new { profile = "movie" }),
                TestContext.Current.CancellationToken);

            Assert.Equal("approval-expired", result.GetProperty("error").GetProperty("code").GetString());
            Assert.Null(commands.Pending);
            Assert.Equal("default", state.Current.SelectionState.ManualProfile);
        }

        [Fact]
        public async Task Approval_AppliesAcceptedDecisionWhenTimeoutWinsTheAwaitRace()
        {
            var (state, _) = await CreateAsync();
            var clock = new RacingTimeoutProvider();
            var commands = new McpCommandService(state, new McpReadQueryService(state, () => []), clock);
            McpApprovalRequest? approval = null;
            commands.ApprovalRequested += (_, request) => approval = request;
            clock.AfterTimeout = () => Assert.True(commands.Approve(approval!.Id));

            var result = await commands.DispatchAsync(Request("selectProfile", new { profile = "movie" }),
                TestContext.Current.CancellationToken);

            Assert.True(result.GetProperty("ok").GetBoolean());
            Assert.Equal("movie", state.Current.SelectionState.ManualProfile);
            Assert.False(commands.Approve(approval!.Id));
        }

        [Fact]
        public async Task PipeDisconnect_DeclinesPendingApprovalWithoutSideEffects()
        {
            var (state, commands) = await CreateAsync();
            var approvalSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            commands.ApprovalRequested += (_, _) => approvalSeen.TrySetResult();
            var credential = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            await using var server = new LocalMcpPipeServer(credential, commands.DispatchAsync, _directory);
            _ = server.RunAsync();
            var client = new LocalMcpPipeClient(credential, Environment.ProcessPath!, _directory);
            using var cancellation = new CancellationTokenSource();
            var request = client.RequestAsync(
                "selectProfile", new { profile = "movie" }, cancellation.Token);
            await approvalSeen.Task.WaitAsync(TestContext.Current.CancellationToken);

            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
            await WaitForAsync(() => commands.Pending is null);

            Assert.Equal("default", state.Current.SelectionState.ManualProfile);
        }

        [Fact]
        public async Task PipeDisconnect_AfterExtraClientByteStillDeclinesPendingApproval()
        {
            var (state, commands) = await CreateAsync();
            var approvalSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            commands.ApprovalRequested += (_, _) => approvalSeen.TrySetResult();
            var credential = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            await using var server = new LocalMcpPipeServer(credential, commands.DispatchAsync, _directory);
            _ = server.RunAsync();
            using (var pipe = new NamedPipeClientStream(
                ".", LocalMcpProtocol.PipeNameFor(_directory), PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                await pipe.ConnectAsync(TestContext.Current.CancellationToken);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
                {
                    AutoFlush = true
                };
                var request = Request("selectProfile", new { profile = "movie" });
                request = new LocalMcpRequest
                {
                    ProtocolVersion = request.ProtocolVersion,
                    Id = request.Id,
                    Credential = credential,
                    ClientName = request.ClientName,
                    Method = request.Method,
                    Parameters = request.Parameters
                };
                await writer.WriteLineAsync(JsonSerializer.Serialize(request));
                await writer.WriteAsync("x");
                await writer.FlushAsync(TestContext.Current.CancellationToken);
                await approvalSeen.Task.WaitAsync(TestContext.Current.CancellationToken);
            }
            await WaitForAsync(() => commands.Pending is null);

            Assert.Equal("default", state.Current.SelectionState.ManualProfile);
        }

        private async Task<(ApplicationStateService State, McpCommandService Commands)> CreateAsync(
            TimeSpan? approvalTimeout = null,
            TimeSpan? declineCooldown = null,
            OutputCoordinator? coordinator = null)
        {
            var store = new LocalStateStore(_directory);
            var startup = await store.InitializeAsync(
                new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero),
                TestContext.Current.CancellationToken);
            var state = new ApplicationStateService(
                startup.Snapshot!.Settings,
                startup.Snapshot.State,
                startup.Snapshot.Desired.Reason,
                store, coordinator);
            var queries = new McpReadQueryService(state, () => []);
            return (state, new McpCommandService(
                state,
                queries,
                approvalTimeout: approvalTimeout,
                declineCooldown: declineCooldown));
        }

        private static LocalMcpRequest Request(string method, object parameters) => new()
        {
            ProtocolVersion = LocalMcpProtocol.Version,
            Id = Guid.NewGuid().ToString("N"),
            ClientName = "Test client",
            Method = method,
            Parameters = JsonSerializer.SerializeToElement(parameters, JsonOptions)
        };

        private static SettingsDocument WithTheme(SettingsDocument settings, string theme) => new()
        {
            SchemaVersion = settings.SchemaVersion,
            Application = new ApplicationSettings
            {
                CloseButtonBehavior = settings.Application!.CloseButtonBehavior,
                StartMinimized = settings.Application.StartMinimized,
                Theme = theme,
                VerboseLogging = settings.Application.VerboseLogging,
                AdditionalProperties = settings.Application.AdditionalProperties
            },
            Plugins = settings.Plugins,
            Selection = settings.Selection,
            Profiles = settings.Profiles,
            ApplicationGroups = settings.ApplicationGroups,
            Rules = settings.Rules,
            AdditionalProperties = settings.AdditionalProperties
        };

        private static SettingsDocument Clone(SettingsDocument settings) =>
            SettingsJson.Load(SettingsJson.Serialize(settings)).Settings!;

        private static async Task WaitForAsync(Func<bool> condition)
        {
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
            while (!condition() && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.True(condition());
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        private sealed class RacingTimeoutProvider : TimeProvider
        {
            public Action? AfterTimeout { get; set; }

            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                callback(state);
                AfterTimeout!();
                return new CompletedTimer();
            }

            private sealed class CompletedTimer : ITimer
            {
                public bool Change(TimeSpan dueTime, TimeSpan period) => false;
                public void Dispose() { }
                public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            }
        }

        private sealed class RecoveryGateway : IPluginOutputGateway
        {
            public IReadOnlyList<PluginCapability>? GetCapabilities(string pluginId) =>
            [
                new PluginCapability { Id = "provider.effect.catalog", Support = "supported" },
                new PluginCapability { Id = "provider.effect.select", Support = "supported" },
                new PluginCapability { Id = "provider.layout.select", Support = "supported" }
            ];

            public Task<PluginGatewayResult<PluginCatalogResult>> ListCatalogAsync(
                string pluginId,
                string kind,
                CancellationToken cancellationToken = default) => Task.FromResult(
                    new PluginGatewayResult<PluginCatalogResult>(new PluginCatalogResult
                    {
                        Support = "supported",
                        Items =
                        [
                            new PluginCatalogItem
                            {
                                ResourceId = "resolved",
                                DisplayName = "Resolved",
                                Available = true
                            }
                        ]
                    }));

            public Task<PluginGatewayResult<BindingApplyResult>> ApplyBindingAsync(
                string pluginId,
                BindingApplyParameters parameters,
                CancellationToken cancellationToken = default) => Task.FromResult(
                    new PluginGatewayResult<BindingApplyResult>(new BindingApplyResult { Outcome = "applied" }));

            public Task<PluginGatewayResult<BindingReleaseResult>> ReleaseBindingAsync(
                string pluginId,
                BindingReleaseParameters parameters,
                CancellationToken cancellationToken = default) => Task.FromResult(
                    new PluginGatewayResult<BindingReleaseResult>(new BindingReleaseResult
                    {
                        Outcome = "nothing-to-release"
                    }));
        }
    }
}
