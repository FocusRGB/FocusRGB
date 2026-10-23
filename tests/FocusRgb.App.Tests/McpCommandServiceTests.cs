using System.Security.Cryptography;
using System.Text.Json;
using FocusRgb.App.Mcp;
using FocusRgb.Core.Commands;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Persistence;
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

            Assert.Contains("mapping order", approval!.Summary, StringComparison.Ordinal);
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

            Assert.Contains("changed profiles: Cinema", approval!.Summary, StringComparison.Ordinal);
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

            Assert.Contains("changed application groups: Cinema apps", approval!.Summary, StringComparison.Ordinal);
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
        public async Task PipeDisconnect_DeclinesPendingApprovalWithoutSideEffects()
        {
            var (state, commands) = await CreateAsync();
            var approvalSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            commands.ApprovalRequested += (_, _) => approvalSeen.TrySetResult();
            var credential = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            await using var server = new LocalMcpPipeServer(credential, commands.DispatchAsync);
            _ = server.RunAsync();
            var client = new LocalMcpPipeClient(credential, Environment.ProcessPath!);
            using var cancellation = new CancellationTokenSource();
            var request = client.RequestAsync(
                "selectProfile", new { profile = "movie" }, cancellation.Token);
            await approvalSeen.Task.WaitAsync(TestContext.Current.CancellationToken);

            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
            await WaitForAsync(() => commands.Pending is null);

            Assert.Equal("default", state.Current.SelectionState.ManualProfile);
        }

        private async Task<(ApplicationStateService State, McpCommandService Commands)> CreateAsync(
            TimeSpan? approvalTimeout = null,
            TimeSpan? declineCooldown = null)
        {
            var store = new LocalStateStore(_directory);
            var startup = await store.InitializeAsync(
                new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero),
                TestContext.Current.CancellationToken);
            var state = new ApplicationStateService(
                startup.Snapshot!.Settings,
                startup.Snapshot.State,
                startup.Snapshot.Desired.Reason,
                store);
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
    }
}
