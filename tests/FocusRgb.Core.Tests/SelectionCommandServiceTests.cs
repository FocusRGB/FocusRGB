using FocusRgb.Contracts;
using FocusRgb.Core.Commands;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Coordination;
using FocusRgb.Core.Persistence;
using FocusRgb.Core.Selection;

namespace FocusRgb.Core.Tests
{
    public sealed class SelectionCommandServiceTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 14, 14, 0, 0, TimeSpan.Zero);

        [Fact]
        public async Task SelectProfileAsync_PersistsManualIntentBeforeReturningStatus()
        {
            var store = new RecordingStateStore();
            var service = new SelectionCommandService(store);

            var result = await service.SelectProfileAsync(
                Settings(),
                State(),
                "movie",
                Now,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Same(store.SavedState, result.State);
            Assert.Equal("manual", result.State!.Mode);
            Assert.Equal("movie", result.State.ManualProfile);
            Assert.Equal("movie", result.Status!.Desired.ProfileId);
            Assert.Equal("manual", result.Status.Desired.Reason.Kind);
            Assert.Null(result.Status.Applied);
        }

        [Fact]
        public async Task SelectProfileAsync_PersistsBeforeCoordinatorApply()
        {
            var events = new List<string>();
            var store = new RecordingStateStore(events: events);
            using var coordinator = new OutputCoordinator(
                new OrderingGateway(events), NativeSetupJson.CreateDefault());
            var service = new SelectionCommandService(store, coordinator);

            var result = await service.SelectProfileAsync(
                Settings(), State(), "movie", Now, TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Equal(["save", "apply"], events);
        }

        [Fact]
        public async Task SetModeAsync_AutomaticRetainsDesiredProfileUntilObservationsExist()
        {
            var service = new SelectionCommandService(new RecordingStateStore());

            var result = await service.SetModeAsync(
                Settings(),
                State("music"),
                "automatic",
                Now,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Equal("automatic", result.State!.Mode);
            Assert.Equal("music", result.Status!.Desired.ProfileId);
            Assert.Equal("observation-pending", result.Status.Desired.Reason.Kind);
        }

        [Fact]
        public async Task ObserveAsync_PersistsChangedAutomaticSelectionOnlyOnce()
        {
            var store = new RecordingStateStore();
            var settings = Settings();
            var musicRuleIndex = settings.Rules!.FindIndex(rule => rule.Id == "music");
            var musicRule = settings.Rules[musicRuleIndex];
            settings.Rules[musicRuleIndex] = new RuleSettings
            {
                Id = musicRule.Id,
                ApplicationGroup = musicRule.ApplicationGroup,
                Profile = musicRule.Profile,
                Eligibility = "while-running",
                Enabled = musicRule.Enabled,
                AdditionalProperties = musicRule.AdditionalProperties
            };
            var state = new SelectionStateDocument
            {
                SchemaVersion = 1,
                Mode = "automatic",
                ManualProfile = "default",
                LastSelectedProfile = "default",
                UpdatedAt = Now.AddMinutes(-1)
            };
            var service = new SelectionCommandService(store);
            var engine = new SelectionEngine(settings);
            var observation = new ApplicationObservation
            {
                ForegroundExecutable = "Spotify.exe",
                RunningExecutables = ["Spotify.exe"]
            };

            var changed = await service.ObserveAsync(
                settings, state, engine, observation, Now, TestContext.Current.CancellationToken);
            var unchanged = await service.ObserveAsync(
                settings, state, engine, observation, Now.AddSeconds(1), TestContext.Current.CancellationToken);

            Assert.Equal("music", changed.State!.LastSelectedProfile);
            Assert.Equal("rule", changed.Status!.Desired.Reason.Kind);
            Assert.Equal("music", unchanged.State!.LastSelectedProfile);
            Assert.Equal(1, store.SaveCount);
        }

        [Fact]
        public async Task ObserveAsync_DoesNotChangeManualSelection()
        {
            var store = new RecordingStateStore();
            var settings = Settings();

            var result = await new SelectionCommandService(store).ObserveAsync(
                settings,
                State("movie"),
                new SelectionEngine(settings),
                new ApplicationObservation { RunningExecutables = ["Spotify.exe"] },
                Now,
                TestContext.Current.CancellationToken);

            Assert.Equal("movie", result.State!.LastSelectedProfile);
            Assert.Equal(0, store.SaveCount);
        }

        [Fact]
        public async Task SelectProfileAsync_ReturnsFailureWithoutNewState_WhenStorageFails()
        {
            var service = new SelectionCommandService(new RecordingStateStore(fail: true));

            var result = await service.SelectProfileAsync(
                Settings(),
                State(),
                "movie",
                Now,
                TestContext.Current.CancellationToken);

            Assert.False(result.IsSuccess);
            Assert.Equal("persistence-failed", result.ErrorCode);
            Assert.Contains("disk unavailable", result.ErrorMessage, StringComparison.Ordinal);
            Assert.Null(result.State);
        }

        [Fact]
        public async Task SetModeAsync_ManualRestoresLastManualProfile()
        {
            var current = State("music");
            current = new SelectionStateDocument
            {
                SchemaVersion = current.SchemaVersion,
                Mode = "automatic",
                ManualProfile = "music",
                LastSelectedProfile = "movie",
                UpdatedAt = current.UpdatedAt
            };

            var result = await new SelectionCommandService(new RecordingStateStore()).SetModeAsync(
                Settings(), current, "manual", Now, TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Equal("music", result.Status!.Desired.ProfileId);
        }

        [Theory]
        [InlineData("invalid", "invalid-mode")]
        public async Task SetModeAsync_RejectsInvalidInput(string mode, string errorCode)
        {
            var result = await new SelectionCommandService(new RecordingStateStore()).SetModeAsync(
                Settings(), State(), mode, Now, TestContext.Current.CancellationToken);

            Assert.False(result.IsSuccess);
            Assert.Equal(errorCode, result.ErrorCode);
        }

        [Fact]
        public async Task SelectProfileAsync_RejectsUnknownProfile()
        {
            var result = await new SelectionCommandService(new RecordingStateStore()).SelectProfileAsync(
                Settings(), State(), "missing", Now, TestContext.Current.CancellationToken);

            Assert.False(result.IsSuccess);
            Assert.Equal("invalid-profile", result.ErrorCode);
        }

        [Fact]
        public async Task SaveCurrentAsync_PersistsCurrentIntent()
        {
            var store = new RecordingStateStore();
            var result = await new SelectionCommandService(store).SaveCurrentAsync(
                Settings(), State("music"), Now, TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Equal("music", store.SavedState!.ManualProfile);
            Assert.Equal(Now, store.SavedState.UpdatedAt);
        }

        [Fact]
        public async Task ReapplyAsync_DoesNotOverwriteAConcurrentNewerSelection()
        {
            var store = new BlockingStateStore();
            var service = new SelectionCommandService(store);
            var oldState = State();
            var select = service.SelectProfileAsync(
                Settings(), oldState, "movie", Now, TestContext.Current.CancellationToken);
            await store.SaveStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

            var reapply = service.ReapplyAsync(
                Settings(), oldState, FocusRgb.Core.Selection.SelectionReason.Manual(),
                TestContext.Current.CancellationToken);
            store.AllowSave.SetResult();

            await select;
            var result = await reapply;
            Assert.Equal("movie", result.State!.ManualProfile);
            Assert.Equal("movie", result.Status!.Desired.ProfileId);
        }

        [Fact]
        public async Task QueuedModeChangeUsesLatestOwnedState()
        {
            var store = new BlockingStateStore();
            var service = new SelectionCommandService(store);
            var oldState = State();
            var select = service.SelectProfileAsync(
                Settings(), oldState, "movie", Now, TestContext.Current.CancellationToken);
            await store.SaveStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

            var setMode = service.SetModeAsync(
                Settings(), oldState, "automatic", Now.AddSeconds(1), TestContext.Current.CancellationToken);
            store.AllowSave.SetResult();

            await select;
            var result = await setMode;
            Assert.Equal("movie", result.State!.ManualProfile);
            Assert.Equal("movie", result.State.LastSelectedProfile);
        }

        [Fact]
        public async Task QueuedSelectionsApplyOnlyNewestIntent()
        {
            var store = new BlockingStateStore();
            var gateway = new OrderingGateway([]);
            using var coordinator = new OutputCoordinator(gateway, NativeSetupJson.CreateDefault());
            var service = new SelectionCommandService(store, coordinator);
            var current = State();
            var first = service.SelectProfileAsync(
                Settings(), current, "default", Now, TestContext.Current.CancellationToken);
            await store.SaveStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            var latest = service.SelectProfileAsync(
                Settings(), current, "movie", Now.AddSeconds(1), TestContext.Current.CancellationToken);

            store.AllowSave.SetResult();
            await Task.WhenAll(first, latest);

            Assert.Equal(["movie"], gateway.AppliedProfiles);
        }

        [Fact]
        public async Task CancelledNewestSelectionDoesNotSuppressEarlierPersistedIntent()
        {
            var store = new BlockingStateStore();
            var gateway = new OrderingGateway([]);
            using var coordinator = new OutputCoordinator(gateway, NativeSetupJson.CreateDefault());
            var service = new SelectionCommandService(store, coordinator);
            var first = service.SelectProfileAsync(
                Settings(), State(), "movie", Now, TestContext.Current.CancellationToken);
            await store.SaveStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            using var cancellation = new CancellationTokenSource();
            var cancelled = service.SelectProfileAsync(
                Settings(), State(), "music", Now.AddSeconds(1), cancellation.Token);

            cancellation.Cancel();
            store.AllowSave.SetResult();

            await first;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
            Assert.Equal(["movie"], gateway.AppliedProfiles);
        }

        [Fact]
        public async Task NewestSelectionCancelledDuringSaveRestoresEarlierPersistedIntent()
        {
            var store = new CancelSecondSaveStore();
            var gateway = new OrderingGateway([]);
            using var coordinator = new OutputCoordinator(gateway, NativeSetupJson.CreateDefault());
            var service = new SelectionCommandService(store, coordinator);
            var first = service.SelectProfileAsync(
                Settings(), State(), "movie", Now, TestContext.Current.CancellationToken);
            await store.FirstSaveStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            using var cancellation = new CancellationTokenSource();
            var cancelled = service.SelectProfileAsync(
                Settings(), State(), "music", Now.AddSeconds(1), cancellation.Token);
            store.AllowFirstSave.SetResult();
            await store.SecondSaveStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

            cancellation.Cancel();

            await first;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
            Assert.Equal(["movie"], gateway.AppliedProfiles);
        }

        [Fact]
        public async Task CommandAfterCoordinatorDisposeDoesNotPersist()
        {
            var store = new RecordingStateStore();
            var coordinator = new OutputCoordinator(new OrderingGateway([]), NativeSetupJson.CreateDefault());
            coordinator.Dispose();

            var result = await new SelectionCommandService(store, coordinator).SelectProfileAsync(
                Settings(), State(), "movie", Now, TestContext.Current.CancellationToken);

            Assert.False(result.IsSuccess);
            Assert.Equal("output-not-connected", result.ErrorCode);
            Assert.Null(store.SavedState);
        }

        [Fact]
        public async Task ReapplyAndNotificationAfterCoordinatorDisposeReturnFailure()
        {
            var coordinator = new OutputCoordinator(new OrderingGateway([]), NativeSetupJson.CreateDefault());
            coordinator.Dispose();
            var service = new SelectionCommandService(new RecordingStateStore(), coordinator);

            var reapplied = await service.ReapplyAsync(
                Settings(), State(), SelectionReason.Manual(), TestContext.Current.CancellationToken);
            var responded = await service.RespondToNotificationAsync(
                Settings(), State(), "missing", "decline", TestContext.Current.CancellationToken);

            Assert.Equal("output-not-connected", reapplied.ErrorCode);
            Assert.Equal("output-not-connected", responded.ErrorCode);
        }

        [Fact]
        public async Task PersistedTimestampIsUtc()
        {
            var store = new RecordingStateStore();
            var local = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.FromHours(2));

            await new SelectionCommandService(store).SaveCurrentAsync(
                Settings(), State(), local, TestContext.Current.CancellationToken);

            Assert.Equal(TimeSpan.Zero, store.SavedState!.UpdatedAt!.Value.Offset);
            Assert.Equal(local.UtcDateTime, store.SavedState.UpdatedAt.Value.UtcDateTime);
        }

        [Fact]
        public async Task ReapplyAsync_UsesPersistedSelection_WhenItsFirstApplyThrowsAndClockMovesBackward()
        {
            var gateway = new OrderingGateway([]) { ThrowOnApply = true };
            using var coordinator = new OutputCoordinator(gateway, NativeSetupJson.CreateDefault());
            var service = new SelectionCommandService(new RecordingStateStore(), coordinator);
            var settings = Settings();
            var oldState = State();

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SelectProfileAsync(
                settings, oldState, "movie", Now.AddMinutes(-2), TestContext.Current.CancellationToken));
            gateway.ThrowOnApply = false;
            var result = await service.ReapplyAsync(
                settings, oldState, FocusRgb.Core.Selection.SelectionReason.Manual(),
                TestContext.Current.CancellationToken);

            Assert.Equal("movie", result.State!.ManualProfile);
            Assert.Equal("movie", result.Status!.Desired.ProfileId);
        }

        private static SettingsDocument Settings() => SettingsJson.LoadDefaults().Settings!;

        private static SelectionStateDocument State(string profile = "default") => new()
        {
            SchemaVersion = 1,
            Mode = "manual",
            ManualProfile = profile,
            LastSelectedProfile = profile,
            UpdatedAt = Now.AddMinutes(-1)
        };

        private sealed class RecordingStateStore(bool fail = false, List<string>? events = null) : ISelectionStateStore
        {
            public SelectionStateDocument? SavedState { get; private set; }
            public int SaveCount { get; private set; }

            public Task<SaveResult> SaveStateAsync(
                SelectionStateDocument state,
                SettingsDocument settings,
                CancellationToken cancellationToken = default)
            {
                SaveCount++;
                events?.Add("save");
                if (fail)
                {
                    return Task.FromResult(SaveResult.Fail(new PersistenceError
                    {
                        FileName = LocalStateStore.StateFileName,
                        Message = "disk unavailable"
                    }));
                }

                SavedState = state;
                return Task.FromResult(SaveResult.Ok());
            }
        }

        private sealed class OrderingGateway(List<string> events) : IPluginOutputGateway
        {
            public bool ThrowOnApply { get; set; }
            public List<string> AppliedProfiles { get; } = [];

            public IReadOnlyList<PluginCapability>? GetCapabilities(string pluginId) =>
            [
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
                                DisplayName = kind == "effect" ? "Screen Ambient" : "Current provider layout",
                                Available = true
                            }
                        ]
                    }));

            public Task<PluginGatewayResult<BindingApplyResult>> ApplyBindingAsync(
                string pluginId,
                BindingApplyParameters parameters,
                CancellationToken cancellationToken = default)
            {
                events.Add("apply");
                AppliedProfiles.Add(parameters.Profile);
                if (ThrowOnApply) throw new InvalidOperationException("Apply failed.");
                return Task.FromResult(new PluginGatewayResult<BindingApplyResult>(new BindingApplyResult
                {
                    Outcome = "applied"
                }));
            }

            public Task<PluginGatewayResult<BindingReleaseResult>> ReleaseBindingAsync(
                string pluginId,
                BindingReleaseParameters parameters,
                CancellationToken cancellationToken = default) => Task.FromResult(
                    new PluginGatewayResult<BindingReleaseResult>(new BindingReleaseResult
                    {
                        Outcome = "nothing-to-release"
                    }));
        }

        private sealed class BlockingStateStore : ISelectionStateStore
        {
            public TaskCompletionSource SaveStarted { get; } = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource AllowSave { get; } = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            public async Task<SaveResult> SaveStateAsync(
                SelectionStateDocument state,
                SettingsDocument settings,
                CancellationToken cancellationToken = default)
            {
                SaveStarted.TrySetResult();
                await AllowSave.Task.WaitAsync(cancellationToken);
                return SaveResult.Ok();
            }
        }

        private sealed class CancelSecondSaveStore : ISelectionStateStore
        {
            private int _calls;
            public TaskCompletionSource FirstSaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource AllowFirstSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource SecondSaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public async Task<SaveResult> SaveStateAsync(
                SelectionStateDocument state,
                SettingsDocument settings,
                CancellationToken cancellationToken = default)
            {
                if (Interlocked.Increment(ref _calls) == 1)
                {
                    FirstSaveStarted.TrySetResult();
                    await AllowFirstSave.Task.WaitAsync(cancellationToken);
                    return SaveResult.Ok();
                }

                SecondSaveStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return SaveResult.Ok();
            }
        }
    }
}
