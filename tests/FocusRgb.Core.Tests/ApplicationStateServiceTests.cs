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
            OutputCoordinator? coordinator = null)
        {
            var settings = SettingsJson.LoadDefaults().Settings!;
            return new ApplicationStateService(
                settings,
                new SelectionStateDocument
                {
                    SchemaVersion = 1,
                    Mode = "manual",
                    ManualProfile = "default",
                    LastSelectedProfile = "default",
                    UpdatedAt = Now
                },
                SelectionReason.Manual(),
                store,
                coordinator);
        }

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

        private sealed class RecordingStore : IApplicationStateStore
        {
            public int SettingsSaveCount { get; private set; }

            public Task<SaveResult> SaveSettingsAsync(
                SettingsDocument settings,
                CancellationToken cancellationToken = default)
            {
                SettingsSaveCount++;
                return Task.FromResult(SaveResult.Ok());
            }

            public Task<SaveResult> SaveStateAsync(
                SelectionStateDocument state,
                SettingsDocument settings,
                CancellationToken cancellationToken = default) => Task.FromResult(SaveResult.Ok());
        }

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
