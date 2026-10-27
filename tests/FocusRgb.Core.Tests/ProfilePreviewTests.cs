using FocusRgb.Core.Commands;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Coordination;
using FocusRgb.Core.Persistence;
using FocusRgb.Core.Protocol;
using FocusRgb.Core.Selection;

namespace FocusRgb.Core.Tests
{
    public sealed class ProfilePreviewTests
    {
        [Fact]
        public async Task PreviewProfileAsync_RequiresJournalBeforeOutputAndRestoresAfterApplyFailure()
        {
            var (service, store, gateway, coordinator) = Create();
            using (coordinator)
            {
                store.FailJournal = true;
                var denied = await service.PreviewProfileAsync("movie", 1, 1, "test", TestContext.Current.CancellationToken);
                Assert.Equal("persistence-failed", denied.ErrorCode);
                Assert.Empty(gateway.Applied);
                store.FailJournal = false;
                gateway.ThrowNext = true;
                await Assert.ThrowsAsync<IOException>(() => service.PreviewProfileAsync("movie", 1, 1, "test", TestContext.Current.CancellationToken));
                Assert.Null(service.Current.Preview);
                Assert.Null(store.Journal);
                Assert.Equal("default", gateway.Applied.Last());
                Assert.Equal(0, store.StateWrites);
            }
        }

        [Fact]
        public async Task PreviewProfileAsync_ExpiresAndRestoresWithoutSavingPreviewIntent()
        {
            var (service, store, gateway, coordinator) = Create();
            using (coordinator)
            {
                var preview = service.PreviewProfileAsync("movie", 1, 1, "test", TestContext.Current.CancellationToken);
                await WaitForPreview(service);
                Assert.Equal("default", service.Current.RuntimeStatus!.Desired.ProfileId);
                Assert.Equal("movie", service.Current.RuntimeStatus.Applied!.ProfileId);
                Assert.Equal("movie", store.Journal);
                Assert.True((await preview).IsSuccess);
                Assert.Null(service.Current.Preview);
                Assert.Null(store.Journal);
                Assert.Equal("default", gateway.Applied.Last());
                Assert.Equal(0, store.StateWrites);
            }
        }

        [Fact]
        public async Task PreviewProfileAsync_DisconnectRestoresLatestAutomaticSelection()
        {
            var (service, store, gateway, coordinator) = Create(automatic: true);
            using (coordinator)
            using (var disconnected = new CancellationTokenSource())
            {
                var preview = service.PreviewProfileAsync("movie", 30, 1, "test", disconnected.Token);
                await WaitForPreview(service);
                var observation = new ApplicationObservation { RunningExecutables = ["Code.exe"], ForegroundExecutable = "Code.exe" };
                var now = DateTimeOffset.UtcNow;
                await service.ObserveAsync(observation, now, TestContext.Current.CancellationToken);
                await service.ObserveAsync(observation, now.AddSeconds(3), TestContext.Current.CancellationToken);
                Assert.Equal("programming", service.Current.SelectionState.LastSelectedProfile);
                Assert.Equal("programming", service.Current.RuntimeStatus!.Desired.ProfileId);
                Assert.Equal("movie", service.Current.RuntimeStatus.Applied!.ProfileId);
                Assert.Single(gateway.Applied);
                disconnected.Cancel();
                await preview;
                Assert.Equal("programming", gateway.Applied.Last());
                Assert.Equal(1, store.StateWrites);
                Assert.Null(store.Journal);
            }
        }

        [Fact]
        public async Task PreviewProfileAsync_RejectsConcurrentPreviewAndSettings_AndManualCommandEndsOverride()
        {
            var (service, store, gateway, coordinator) = Create();
            using (coordinator)
            {
                var preview = service.PreviewProfileAsync("movie", 30, 1, "test", TestContext.Current.CancellationToken);
                await WaitForPreview(service);
                Assert.Equal("busy", (await service.PreviewProfileAsync("music", 1, 1, "test", TestContext.Current.CancellationToken)).ErrorCode);
                Assert.Equal("busy", (await service.ApplySettingsAsync(service.Current.Settings, 1, "ui", TestContext.Current.CancellationToken)).ErrorCode);
                var manual = await service.SelectProfileAsync("music", DateTimeOffset.UtcNow,
                    cancellationToken: TestContext.Current.CancellationToken);
                await preview;
                Assert.True(manual.IsSuccess);
                Assert.Equal("music", service.Current.SelectionState.ManualProfile);
                Assert.Equal("music", gateway.Applied.Last());
                Assert.Equal(1, store.StateWrites);
            }
        }

        [Fact]
        public async Task PreviewProfileAsync_RechecksRevisionAfterWaitingForCommandGate()
        {
            var (service, store, _, coordinator) = Create();
            using (coordinator)
            {
                store.BlockSettings = true;
                var write = service.ApplySettingsAsync(service.Current.Settings, 1, "ui", TestContext.Current.CancellationToken);
                await store.SaveEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
                var preview = service.PreviewProfileAsync("movie", 1, 1, "test", TestContext.Current.CancellationToken);
                Assert.False(preview.IsCompleted);
                store.AllowSave.TrySetResult();
                Assert.True((await write).IsSuccess);
                Assert.Equal("conflict", (await preview).ErrorCode);
                Assert.Null(store.Journal);
            }
        }

        [Fact]
        public async Task PreviewProfileAsync_RecoveryNeverResumesWithoutExistingRecoveryApproval()
        {
            var (service, _, gateway, coordinator) = Create();
            using (coordinator)
            {
                var preview = service.PreviewProfileAsync("movie", 30, 1, "test", TestContext.Current.CancellationToken);
                await WaitForPreview(service);
                await coordinator.ReportAvailabilityAsync("signalrgb", false, "plugin-exited", "Exited", TestContext.Current.CancellationToken);
                await coordinator.ReportAvailabilityAsync("signalrgb", true, TestContext.Current.CancellationToken);
                var applies = gateway.Applied.Count;
                await service.CancelPreviewAsync(TestContext.Current.CancellationToken);
                await preview;
                Assert.Equal(applies, gateway.Applied.Count);
                Assert.NotEmpty(service.Current.RuntimeStatus!.Notifications);
                Assert.Equal("default", service.Current.RuntimeStatus.Desired.ProfileId);
            }
        }

        [Fact]
        public async Task InitializeAsync_DiscardsInterruptedPreviewJournalAndRestoresOnlySavedState()
        {
            using var directory = new FocusRgb.TestSupport.TempDirectory(nameof(ProfilePreviewTests));
            var store = new LocalStateStore(directory.Path);
            var initial = await store.InitializeAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
            var original = await File.ReadAllBytesAsync(Path.Combine(directory.Path, LocalStateStore.StateFileName), TestContext.Current.CancellationToken);
            Assert.True((await store.SavePreviewJournalAsync("movie", TestContext.Current.CancellationToken)).IsSuccess);
            var restarted = await store.InitializeAsync(DateTimeOffset.UtcNow.AddSeconds(1), TestContext.Current.CancellationToken);
            Assert.Equal(initial.Snapshot!.Desired.ProfileId, restarted.Snapshot!.Desired.ProfileId);
            Assert.Contains(restarted.Snapshot.Recoveries, notice => notice.FileName == LocalStateStore.PreviewJournalFileName);
            Assert.False(File.Exists(Path.Combine(directory.Path, LocalStateStore.PreviewJournalFileName)));
            Assert.Single(Directory.GetFiles(directory.Path, "preview.json.interrupted-*"));
            Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(directory.Path, LocalStateStore.StateFileName), TestContext.Current.CancellationToken));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(31)]
        public async Task PreviewProfileAsync_RejectsUnboundedDuration(int duration)
        {
            var (service, store, _, coordinator) = Create();
            using (coordinator)
            {
                Assert.Equal("invalid-duration", (await service.PreviewProfileAsync("movie", duration, 1, "test", TestContext.Current.CancellationToken)).ErrorCode);
                Assert.Null(store.Journal);
            }
        }

        private static async Task WaitForPreview(ApplicationStateService service)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            while (service.Current.RuntimeStatus?.Applied?.ProfileId != "movie") await Task.Delay(10, timeout.Token);
        }

        private static (ApplicationStateService, Store, Gateway, OutputCoordinator) Create(bool automatic = false)
        {
            var document = System.Text.Json.Nodes.JsonNode.Parse(SettingsJson.Serialize(SettingsJson.LoadDefaults().Settings!))!;
            document["selection"]!["debounceMs"] = 0;
            var settings = SettingsJson.Load(document.ToJsonString()).Settings!;
            var state = new SelectionStateDocument { SchemaVersion = 1, Mode = automatic ? "automatic" : "manual",
                ManualProfile = "default", LastSelectedProfile = "default", UpdatedAt = DateTimeOffset.UtcNow };
            var store = new Store();
            var gateway = new Gateway();
            var coordinator = new OutputCoordinator(gateway, NativeSetupJson.CreateDefault());
            return (new ApplicationStateService(settings, state, SelectionReason.Restored(), store, coordinator), store, gateway, coordinator);
        }

        private sealed class Store : IApplicationStateStore
        {
            public int StateWrites { get; private set; }
            public string? Journal { get; private set; }
            public bool BlockSettings { get; set; }
            public bool FailJournal { get; set; }
            public TaskCompletionSource SaveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource AllowSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task<SaveResult> SavePreviewJournalAsync(string? profileId, CancellationToken cancellationToken = default)
            {
                if (FailJournal) return Task.FromResult(SaveResult.Fail(new PersistenceError { FileName = "preview.json", Message = "Denied" }));
                Journal = profileId;
                return Task.FromResult(SaveResult.Ok());
            }
            public async Task<SaveResult> SaveSettingsAsync(SettingsDocument settings, CancellationToken cancellationToken = default)
            {
                SaveEntered.TrySetResult();
                if (BlockSettings) await AllowSave.Task.WaitAsync(cancellationToken);
                return SaveResult.Ok();
            }
            public Task<SaveResult> SaveStateAsync(SelectionStateDocument state, SettingsDocument settings, CancellationToken cancellationToken = default)
            { StateWrites++; return Task.FromResult(SaveResult.Ok()); }
            public Task<SaveResult> SaveGameCatalogAsync(GameCatalogDocument catalog, CancellationToken cancellationToken = default) => Task.FromResult(SaveResult.Ok());
        }

        private sealed class Gateway : IPluginOutputGateway
        {
            private static readonly string[] Effects = ["Aurora", "Screen Ambience", "Logarithmic Visualizer"];
            public List<string> Applied { get; } = [];
            public bool ThrowNext { get; set; }
            public IReadOnlyList<PluginCapability>? GetCapabilities(string pluginId) =>
                [new() { Id = "provider.effect.select", Support = "supported" }, new() { Id = "provider.effect.catalog", Support = "supported" }];
            public Task<PluginGatewayResult<PluginCatalogResult>> ListCatalogAsync(string pluginId, string kind, CancellationToken cancellationToken = default) =>
                Task.FromResult(new PluginGatewayResult<PluginCatalogResult>(new PluginCatalogResult { Support = "supported", Items =
                    Effects.Select(name => new PluginCatalogItem { ResourceId = name, DisplayName = name, Available = true }).ToArray() }));
            public Task<PluginGatewayResult<BindingApplyResult>> ApplyBindingAsync(string pluginId, BindingApplyParameters parameters, CancellationToken cancellationToken = default)
            {
                if (ThrowNext) { ThrowNext = false; throw new IOException("Disconnected during apply"); }
                Applied.Add(parameters.Profile);
                return Task.FromResult(new PluginGatewayResult<BindingApplyResult>(new BindingApplyResult { Outcome = "applied",
                    Actual = new PluginSelection { Effect = parameters.Effect } }));
            }
            public Task<PluginGatewayResult<BindingReleaseResult>> ReleaseBindingAsync(string pluginId, BindingReleaseParameters parameters, CancellationToken cancellationToken = default) =>
                Task.FromResult(new PluginGatewayResult<BindingReleaseResult>(new BindingReleaseResult { Outcome = "released" }));
        }
    }
}
