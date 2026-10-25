using FocusRgb.Core.Commands;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Coordination;
using FocusRgb.Core.Persistence;
using FocusRgb.Core.Protocol;
using FocusRgb.Core.Selection;

namespace FocusRgb.Core.Tests
{
    public sealed class ApplicationStateServiceTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Current_PopulatesRuntimeStatusWithoutAnOutputCoordinator()
        {
            var service = CreateService(new RecordingStore());

            var current = service.Current;

            Assert.NotNull(current.RuntimeStatus);
            Assert.Equal("default", current.RuntimeStatus.Desired.ProfileId);
        }

        [Fact]
        public async Task ApplySettingsAsync_RejectsAStaleRevision()
        {
            var store = new RecordingStore();
            var service = CreateService(store);
            var changed = WithTheme(service.Current.Settings, "dark");
            var first = await service.ApplySettingsAsync(
                changed, service.Current.SettingsRevision, "mcp", TestContext.Current.CancellationToken);

            var stale = await service.ApplySettingsAsync(
                WithTheme(first.Snapshot.Settings, "light"),
                expectedRevision: 1,
                "mcp",
                TestContext.Current.CancellationToken);

            Assert.False(stale.IsSuccess);
            Assert.Equal("conflict", stale.ErrorCode);
            Assert.Equal(1, store.SettingsSaveCount);
            Assert.Equal("dark", service.Current.Settings.Application!.Theme);
        }

        [Fact]
        public async Task ApplySettingsAsync_PublishesAnIndependentRevisionedSnapshot()
        {
            var store = new RecordingStore();
            var service = CreateService(store);
            ApplicationStateSnapshot? published = null;
            service.SnapshotChanged += (_, snapshot) => published = snapshot;
            var candidate = WithTheme(service.Current.Settings, "dark");

            var result = await service.ApplySettingsAsync(
                candidate, service.Current.SettingsRevision, "mcp", TestContext.Current.CancellationToken);
            candidate.Profiles!.Clear();

            Assert.True(result.IsSuccess);
            Assert.Equal(2, result.Snapshot.SettingsRevision);
            Assert.Same(result.Snapshot, published);
            Assert.Equal("mcp", published!.Source);
            Assert.NotEmpty(service.Current.Settings.Profiles!);
        }

        [Fact]
        public async Task ApplySettingsAsync_SerializesConcurrentWritesAndRejectsTheStaleWriter()
        {
            var store = new BlockingSettingsStore();
            var service = CreateService(store);
            var revision = service.Current.SettingsRevision;
            var first = service.ApplySettingsAsync(
                WithTheme(service.Current.Settings, "dark"), revision, "ui", TestContext.Current.CancellationToken);
            await store.SaveStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

            var second = service.ApplySettingsAsync(
                WithTheme(service.Current.Settings, "light"), revision, "mcp", TestContext.Current.CancellationToken);
            Assert.False(second.IsCompleted);
            store.AllowSave.TrySetResult();

            Assert.True((await first).IsSuccess);
            Assert.Equal("conflict", (await second).ErrorCode);
            Assert.Equal(1, store.SettingsSaveCount);
            Assert.Equal("dark", service.Current.Settings.Application!.Theme);
        }

        [Fact]
        public async Task ApplySettingsAsync_RepairsManualSelectionAfterProfileRemoval()
        {
            var service = CreateService(
                new RecordingStore(),
                state: State("manual", "movie", "movie"),
                reason: SelectionReason.Manual());

            var result = await service.ApplySettingsAsync(
                WithoutProfile(service.Current.Settings, "movie"),
                service.Current.SettingsRevision,
                "ui",
                TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Equal("default", result.Snapshot.SelectionState.ManualProfile);
            Assert.Equal("default", result.Snapshot.SelectionState.LastSelectedProfile);
            Assert.Equal("manual", result.Snapshot.SelectionReason.Kind);
            Assert.Equal("default", result.Snapshot.RuntimeStatus!.Desired.ProfileId);
        }

        [Fact]
        public async Task ApplySettingsAsync_PreservesAutomaticReasonWhenOnlyInactiveManualProfileIsRepaired()
        {
            var service = CreateService(
                new RecordingStore(),
                state: State("automatic", "movie", "programming"),
                reason: SelectionReason.Rule("programming"));

            var result = await service.ApplySettingsAsync(
                WithoutProfile(service.Current.Settings, "movie"),
                service.Current.SettingsRevision,
                "mcp",
                TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Equal("default", result.Snapshot.SelectionState.ManualProfile);
            Assert.Equal("programming", result.Snapshot.SelectionState.LastSelectedProfile);
            Assert.Equal("rule", result.Snapshot.SelectionReason.Kind);
            Assert.Equal("programming", result.Snapshot.SelectionReason.RuleId);
        }

        [Fact]
        public async Task ApplySettingsAsync_UsesObservationPendingWhenAutomaticDesiredProfileIsRepaired()
        {
            var service = CreateService(
                new RecordingStore(),
                state: State("automatic", "default", "movie"),
                reason: SelectionReason.Rule("movie"));

            var result = await service.ApplySettingsAsync(
                WithoutProfile(service.Current.Settings, "movie"),
                service.Current.SettingsRevision,
                "mcp",
                TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Equal("default", result.Snapshot.SelectionState.LastSelectedProfile);
            Assert.Equal("observation-pending", result.Snapshot.SelectionReason.Kind);
            Assert.Equal("default", result.Snapshot.RuntimeStatus!.Desired.ProfileId);
        }

        [Fact]
        public async Task ApplySettingsAsync_LeavesStateUntouchedWhenRepairCannotBePersisted()
        {
            var store = new RecordingStore(failState: true);
            var service = CreateService(
                store,
                state: State("manual", "movie", "movie"),
                reason: SelectionReason.Manual());

            var result = await service.ApplySettingsAsync(
                WithoutProfile(service.Current.Settings, "movie"),
                service.Current.SettingsRevision,
                "ui",
                TestContext.Current.CancellationToken);

            Assert.Equal("persistence-failed", result.ErrorCode);
            Assert.Equal(1, result.Snapshot.SettingsRevision);
            Assert.Equal("movie", result.Snapshot.SelectionState.ManualProfile);
            Assert.Contains(result.Snapshot.Settings.Profiles!, profile => profile.Id == "movie");
        }

        [Fact]
        public async Task ApplySettingsAsync_RollsBackStateWhenSettingsFailAfterStateRepair()
        {
            var state = State("manual", "movie", "movie");
            var reason = SelectionReason.Manual();
            var store = new RecordingStore(failSettings: true);
            var service = CreateService(
                store,
                state: state,
                reason: reason,
                runtimeStatus: SelectionCommandService.GetStatus(state, reason));

            var result = await service.ApplySettingsAsync(
                WithoutProfile(service.Current.Settings, "movie"),
                service.Current.SettingsRevision,
                "ui",
                TestContext.Current.CancellationToken);

            Assert.Equal("persistence-failed", result.ErrorCode);
            Assert.Equal(1, result.Snapshot.SettingsRevision);
            Assert.Equal("movie", result.Snapshot.SelectionState.ManualProfile);
            Assert.Equal("movie", result.Snapshot.RuntimeStatus!.Desired.ProfileId);
            Assert.Contains(result.Snapshot.Settings.Profiles!, profile => profile.Id == "movie");
            Assert.Equal(2, store.StateSaveCount);
        }

        [Fact]
        public async Task ApplySettingsAsync_CancellationLeavesSettingsAndRevisionUnchanged()
        {
            var store = new CancelingSettingsStore();
            var service = CreateService(store);
            using var cancellation = new CancellationTokenSource();
            var apply = service.ApplySettingsAsync(
                WithTheme(service.Current.Settings, "dark"),
                service.Current.SettingsRevision,
                "mcp",
                cancellation.Token);
            await store.SaveStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

            await cancellation.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => apply);
            Assert.Equal(1, service.Current.SettingsRevision);
            Assert.Equal("system", service.Current.Settings.Application!.Theme);
        }

        [Fact]
        public async Task ApplySettingsAsync_CancellationRollsBackPersistedProfileRepair()
        {
            var store = new CancelingSettingsStore();
            var service = CreateService(store, state: State("manual", "movie", "movie"));
            using var cancellation = new CancellationTokenSource();
            var apply = service.ApplySettingsAsync(WithoutProfile(service.Current.Settings, "movie"),
                service.Current.SettingsRevision, "mcp", cancellation.Token);
            await store.SaveStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => apply);

            Assert.Equal(["default", "movie"], store.SavedStates.Select(state => state.ManualProfile));
            Assert.Equal("movie", service.Current.SelectionState.ManualProfile);
            Assert.Equal(1, service.Current.SettingsRevision);
        }

        [Fact]
        public async Task ApplySettingsAsync_UsesOutputSourceWhenOnlyCoordinatorChanged()
        {
            var store = new BlockingFailSettingsStore();
            using var coordinator = new OutputCoordinator(new FakeGateway(), NativeSetupJson.CreateDefault());
            var service = CreateService(store, coordinator);
            await service.ReapplyAsync(cancellationToken: TestContext.Current.CancellationToken);
            var snapshots = new List<ApplicationStateSnapshot>();
            service.SnapshotChanged += (_, snapshot) => snapshots.Add(snapshot);
            var apply = service.ApplySettingsAsync(
                WithTheme(service.Current.Settings, "dark"),
                service.Current.SettingsRevision,
                "ui",
                TestContext.Current.CancellationToken);
            await store.SaveStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

            await coordinator.ReportAvailabilityAsync(
                "signalrgb", false, TestContext.Current.CancellationToken);
            store.AllowSave.TrySetResult();

            Assert.Equal("persistence-failed", (await apply).ErrorCode);
            Assert.Equal("output", Assert.Single(snapshots).Source);
        }

        [Fact]
        public async Task ApplyGameCatalogAsync_UsesSharedRevisionAndRejectsStaleWriter()
        {
            var store = new RecordingStore();
            var service = CreateService(store);
            var revision = service.Current.SettingsRevision;
            var candidate = ManualCatalog("first-game", "First.exe");

            var saved = await service.ApplyGameCatalogAsync(
                candidate, revision, "ui", TestContext.Current.CancellationToken);
            candidate.Games!.Clear();
            var stale = await service.ApplyGameCatalogAsync(
                ManualCatalog("second-game", "Second.exe"), revision, "ui",
                TestContext.Current.CancellationToken);

            Assert.True(saved.IsSuccess);
            Assert.Equal(2, saved.Snapshot.SettingsRevision);
            Assert.Equal("conflict", stale.ErrorCode);
            Assert.Equal(1, store.GameCatalogSaveCount);
            Assert.Equal("first-game", Assert.Single(service.Current.GameCatalog.Games!).Id);
        }

        [Fact]
        public async Task ApplyGameCatalogAsync_PersistenceFailureLeavesCatalogAndRevisionUntouched()
        {
            var service = CreateService(new RecordingStore(failGameCatalog: true));

            var result = await service.ApplyGameCatalogAsync(
                ManualCatalog("local-game", "Local.exe"),
                service.Current.SettingsRevision,
                "ui",
                TestContext.Current.CancellationToken);

            Assert.Equal("persistence-failed", result.ErrorCode);
            Assert.Equal(1, result.Snapshot.SettingsRevision);
            Assert.Empty(result.Snapshot.GameCatalog.Games!);
        }

        [Fact]
        public async Task OutputCoordinatorAvailabilityAndRecoveryPublishSnapshots()
        {
            var store = new RecordingStore();
            using var coordinator = new OutputCoordinator(new FakeGateway(), NativeSetupJson.CreateDefault());
            var service = CreateService(store, coordinator);
            await service.ReapplyAsync(cancellationToken: TestContext.Current.CancellationToken);
            var snapshots = new List<ApplicationStateSnapshot>();
            service.SnapshotChanged += (_, snapshot) => snapshots.Add(snapshot);

            await coordinator.ReportAvailabilityAsync(
                "signalrgb", false, TestContext.Current.CancellationToken);
            await coordinator.ReportAvailabilityAsync(
                "signalrgb", true, TestContext.Current.CancellationToken);

            Assert.Equal(2, snapshots.Count);
            Assert.All(snapshots, snapshot => Assert.Equal("output", snapshot.Source));
            Assert.Equal("fallback", snapshots[0].RuntimeStatus!.Effective.Source);
            Assert.Equal("plugin-recovered", Assert.Single(snapshots[1].RuntimeStatus!.Notifications).Kind);
        }

        private static ApplicationStateService CreateService(
            IApplicationStateStore store,
            OutputCoordinator? coordinator = null,
            SelectionStateDocument? state = null,
            SelectionReason? reason = null,
            SelectionRuntimeStatus? runtimeStatus = null)
        {
            var settings = SettingsJson.LoadDefaults().Settings!;
            return new ApplicationStateService(
                settings,
                state ?? State("manual", "default", "default"),
                reason ?? SelectionReason.Manual(),
                store,
                coordinator,
                runtimeStatus);
        }

        private static SelectionStateDocument State(
            string mode,
            string manualProfile,
            string lastSelectedProfile) => new()
            {
                SchemaVersion = 1,
                Mode = mode,
                ManualProfile = manualProfile,
                LastSelectedProfile = lastSelectedProfile,
                UpdatedAt = Now
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

        private static SettingsDocument WithoutProfile(SettingsDocument settings, string profileId) => new()
        {
            SchemaVersion = settings.SchemaVersion,
            Application = settings.Application,
            Plugins = settings.Plugins,
            Selection = settings.Selection,
            Profiles = settings.Profiles!.Where(profile => profile.Id != profileId).ToList(),
            ApplicationGroups = settings.ApplicationGroups,
            Rules = settings.Rules!.Where(rule => rule.Profile != profileId).ToList(),
            AdditionalProperties = settings.AdditionalProperties
        };

        private static GameCatalogDocument ManualCatalog(string id, string executable) => new()
        {
            SchemaVersion = GameCatalogJson.CurrentSchemaVersion,
            Games =
            [
                new GameCatalogEntry
                {
                    Launcher = GameCatalogJson.ManualLauncher,
                    Id = id,
                    DisplayName = id,
                    Executables = [executable],
                    Available = true
                }
            ]
        };

        private sealed class RecordingStore(
            bool failState = false,
            bool failSettings = false,
            bool failGameCatalog = false) : IApplicationStateStore
        {
            public int SettingsSaveCount { get; private set; }
            public int GameCatalogSaveCount { get; private set; }
            public int StateSaveCount { get; private set; }

            public Task<SaveResult> SaveSettingsAsync(
                SettingsDocument settings,
                CancellationToken cancellationToken = default)
            {
                SettingsSaveCount++;
                return Task.FromResult(failSettings ? Failure("settings.json") : SaveResult.Ok());
            }

            public Task<SaveResult> SaveStateAsync(
                SelectionStateDocument state,
                SettingsDocument settings,
                CancellationToken cancellationToken = default)
            {
                StateSaveCount++;
                return Task.FromResult(failState ? Failure("state.json") : SaveResult.Ok());
            }

            public Task<SaveResult> SaveGameCatalogAsync(
                GameCatalogDocument catalog,
                CancellationToken cancellationToken = default)
            {
                GameCatalogSaveCount++;
                return Task.FromResult(failGameCatalog ? Failure("games.json") : SaveResult.Ok());
            }
        }

        private sealed class BlockingSettingsStore : IApplicationStateStore
        {
            public TaskCompletionSource SaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource AllowSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public int SettingsSaveCount { get; private set; }

            public async Task<SaveResult> SaveSettingsAsync(
                SettingsDocument settings,
                CancellationToken cancellationToken = default)
            {
                SettingsSaveCount++;
                SaveStarted.TrySetResult();
                await AllowSave.Task.WaitAsync(cancellationToken);
                return SaveResult.Ok();
            }

            public Task<SaveResult> SaveStateAsync(
                SelectionStateDocument state,
                SettingsDocument settings,
                CancellationToken cancellationToken = default) => Task.FromResult(SaveResult.Ok());

            public Task<SaveResult> SaveGameCatalogAsync(
                GameCatalogDocument catalog,
                CancellationToken cancellationToken = default) => Task.FromResult(SaveResult.Ok());
        }

        private sealed class BlockingFailSettingsStore : IApplicationStateStore
        {
            public TaskCompletionSource SaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource AllowSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public async Task<SaveResult> SaveSettingsAsync(
                SettingsDocument settings,
                CancellationToken cancellationToken = default)
            {
                SaveStarted.TrySetResult();
                await AllowSave.Task.WaitAsync(cancellationToken);
                return Failure("settings.json");
            }

            public Task<SaveResult> SaveStateAsync(
                SelectionStateDocument state,
                SettingsDocument settings,
                CancellationToken cancellationToken = default) => Task.FromResult(SaveResult.Ok());

            public Task<SaveResult> SaveGameCatalogAsync(
                GameCatalogDocument catalog,
                CancellationToken cancellationToken = default) => Task.FromResult(SaveResult.Ok());
        }

        private sealed class CancelingSettingsStore : IApplicationStateStore
        {
            public TaskCompletionSource SaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public List<SelectionStateDocument> SavedStates { get; } = [];

            public async Task<SaveResult> SaveSettingsAsync(
                SettingsDocument settings,
                CancellationToken cancellationToken = default)
            {
                SaveStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return SaveResult.Ok();
            }

            public Task<SaveResult> SaveStateAsync(
                SelectionStateDocument state,
                SettingsDocument settings,
                CancellationToken cancellationToken = default)
            {
                SavedStates.Add(state);
                return Task.FromResult(SaveResult.Ok());
            }

            public Task<SaveResult> SaveGameCatalogAsync(
                GameCatalogDocument catalog,
                CancellationToken cancellationToken = default) => Task.FromResult(SaveResult.Ok());
        }

        private static SaveResult Failure(string fileName) => SaveResult.Fail(new PersistenceError
        {
            FileName = fileName,
            Message = "disk unavailable"
        });

        private sealed class FakeGateway : IPluginOutputGateway
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
                                DisplayName = kind == "effect" ? "Aurora" : "Current provider layout",
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
