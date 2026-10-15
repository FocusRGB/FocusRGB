using System.Text.Json.Nodes;
using FocusRgb.Contracts;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Coordination;
using FocusRgb.Core.Selection;

namespace FocusRgb.Core.Tests
{
    public sealed class OutputCoordinatorTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public async Task ApplyAsync_ResolvesCatalogAndAppliesDesiredProfile()
        {
            var gateway = new FakeGateway("fake-output");
            using var coordinator = new OutputCoordinator(gateway, NativeSetupJson.CreateDefault());

            var status = await coordinator.ApplyAsync(
                Settings(("default", "fake-output", "provider-managed", null)),
                State("default"),
                SelectionReason.Manual(),
                TestContext.Current.CancellationToken);

            var applied = Assert.Single(status.Applied!.Bindings);
            Assert.Equal("default", status.Desired.ProfileId);
            Assert.Equal("desired", status.Effective.Source);
            Assert.Equal("applied", applied.Outcome);
            Assert.Equal("fake-aurora", Assert.Single(gateway.Applied).Effect!.ResourceId);
        }

        [Fact]
        public async Task ApplyAsync_RetainsDesiredAndReportsPendingNativeFallback_WhenPluginIsAbsent()
        {
            using var coordinator = new OutputCoordinator(new FakeGateway(), NativeSetupJson.CreateDefault());

            var status = await coordinator.ApplyAsync(
                Settings(("default", "missing", "provider-managed", null)),
                State("default"),
                SelectionReason.Manual(),
                TestContext.Current.CancellationToken);

            Assert.Equal("default", status.Desired.ProfileId);
            Assert.Equal("fallback", status.Effective.Source);
            Assert.Null(status.Applied);
            Assert.Equal("plugin-absent", Assert.Single(status.Effective.Skipped).ReasonCode);
            Assert.Equal("native-output-unavailable", Assert.Single(status.Pending).ReasonCode);
        }

        [Fact]
        public async Task ApplyAsync_DoesNotStartCompetingWriter_WhenReleaseIsNotConfirmed()
        {
            var gateway = new FakeGateway("plugin-a", "plugin-b") { ReleaseOutcome = "unsupported" };
            using var coordinator = new OutputCoordinator(gateway, NativeSetupJson.CreateDefault());
            var settings = Settings(
                ("default", "plugin-a", "devices", "desk"),
                ("movie", "plugin-b", "devices", "desk"));
            await coordinator.ApplyAsync(settings, State("default"), SelectionReason.Manual(),
                TestContext.Current.CancellationToken);

            var status = await coordinator.ApplyAsync(settings, State("movie"), SelectionReason.Manual(),
                TestContext.Current.CancellationToken);

            Assert.Equal("movie", status.Desired.ProfileId);
            Assert.Equal("default", status.Applied!.ProfileId);
            Assert.Equal("applied", Assert.Single(status.Applied.Bindings).Outcome);
            Assert.Equal("device-owned-elsewhere", Assert.Single(status.Effective.Skipped).ReasonCode);
            Assert.DoesNotContain(gateway.Applied, request => request.Profile == "movie");
        }

        [Fact]
        public async Task Recovery_CoalescesAndWaitsForAccept_ThenUsesCurrentIntent()
        {
            var gateway = new FakeGateway();
            using var coordinator = new OutputCoordinator(gateway, NativeSetupJson.CreateDefault());
            var settings = Settings(
                ("default", "fake-output", "provider-managed", null),
                ("movie", "fake-output", "provider-managed", null));
            await coordinator.ApplyAsync(settings, State("default"), SelectionReason.Manual(),
                TestContext.Current.CancellationToken);

            gateway.Enable("fake-output");
            await coordinator.ReportAvailabilityAsync("fake-output", available: true,
                TestContext.Current.CancellationToken);
            var recovered = await coordinator.ReportAvailabilityAsync("fake-output", available: true,
                TestContext.Current.CancellationToken);

            Assert.Single(recovered!.Notifications);
            Assert.Empty(gateway.Applied);

            var newerIntent = await coordinator.ApplyAsync(
                settings, State("movie"), SelectionReason.Manual(), TestContext.Current.CancellationToken);
            Assert.Equal("movie", newerIntent.Desired.ProfileId);
            Assert.Empty(gateway.Applied);

            var accepted = await coordinator.RespondToNotificationAsync(
                recovered.Notifications[0].Id, "accept", TestContext.Current.CancellationToken);

            Assert.Empty(accepted!.Notifications);
            Assert.Equal("movie", Assert.Single(gateway.Applied).Profile);
        }

        [Fact]
        public async Task AvailabilityLoss_ActivatesFallbackWithoutRetryingUnavailablePlugin()
        {
            var gateway = new FakeGateway("fake-output");
            using var coordinator = new OutputCoordinator(gateway, NativeSetupJson.CreateDefault());
            var settings = Settings(("default", "fake-output", "provider-managed", null));
            await coordinator.ApplyAsync(settings, State("default"), SelectionReason.Manual(),
                TestContext.Current.CancellationToken);

            var unavailable = await coordinator.ReportAvailabilityAsync(
                "fake-output", available: false, TestContext.Current.CancellationToken);

            Assert.Single(gateway.Applied);
            Assert.Equal("fallback", unavailable!.Effective.Source);
            Assert.Equal("native-output-unavailable", Assert.Single(unavailable.Pending).ReasonCode);
        }

        [Fact]
        public async Task Recovery_DeclineKeepsFallbackAndDoesNotApply()
        {
            var gateway = new FakeGateway();
            using var coordinator = new OutputCoordinator(gateway, NativeSetupJson.CreateDefault());
            var settings = Settings(("default", "fake-output", "provider-managed", null));
            await coordinator.ApplyAsync(settings, State("default"), SelectionReason.Manual(),
                TestContext.Current.CancellationToken);
            gateway.Enable("fake-output");
            var recovered = await coordinator.ReportAvailabilityAsync("fake-output", available: true,
                TestContext.Current.CancellationToken);

            var declined = await coordinator.RespondToNotificationAsync(
                recovered!.Notifications[0].Id, "decline", TestContext.Current.CancellationToken);

            Assert.Empty(declined!.Notifications);
            Assert.Empty(gateway.Applied);
            Assert.Equal("fallback", declined.Effective.Source);
        }

        private static SelectionStateDocument State(string profile) => new()
        {
            SchemaVersion = 1,
            Mode = "manual",
            ManualProfile = profile,
            LastSelectedProfile = profile,
            UpdatedAt = Now
        };

        private static SettingsDocument Settings(
            params (string Profile, string Plugin, string Scope, string? Device)[] changes)
        {
            var root = JsonNode.Parse(SettingsJson.Serialize(SettingsJson.LoadDefaults().Settings!))!;
            foreach (var change in changes)
            {
                var profile = root["profiles"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == change.Profile)!;
                var binding = profile["bindings"]![0]!;
                binding["plugin"] = change.Plugin;
                binding["deviceScope"] = change.Scope == "devices"
                    ? JsonNode.Parse($$"""{"kind":"devices","deviceIds":["{{change.Device}}"]}""")
                    : JsonNode.Parse("""{"kind":"provider-managed"}""");
            }
            return SettingsJson.Load(root.ToJsonString()).Settings!;
        }

        private sealed class FakeGateway(params string[] enabled) : IPluginOutputGateway
        {
            private readonly HashSet<string> _enabled = new(enabled, StringComparer.Ordinal);
            public List<BindingApplyParameters> Applied { get; } = [];
            public string ReleaseOutcome { get; init; } = "released";

            public void Enable(string pluginId) => _enabled.Add(pluginId);

            public IReadOnlyList<PluginCapability>? GetCapabilities(string pluginId) =>
                _enabled.Contains(pluginId)
                    ?
                    [
                        new PluginCapability { Id = "provider.effect.catalog", Support = "supported" },
                        new PluginCapability { Id = "provider.effect.select", Support = "supported" },
                        new PluginCapability { Id = "provider.layout.select", Support = "supported" }
                    ]
                    : null;

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
                                ResourceId = "fake-aurora",
                                DisplayName = "Aurora",
                                Available = true
                            },
                            new PluginCatalogItem
                            {
                                ResourceId = "fake-screen",
                                DisplayName = "Screen Ambient",
                                Available = true
                            }
                        ]
                    }));

            public Task<PluginGatewayResult<BindingApplyResult>> ApplyBindingAsync(
                string pluginId,
                BindingApplyParameters parameters,
                CancellationToken cancellationToken = default)
            {
                Applied.Add(parameters);
                return Task.FromResult(new PluginGatewayResult<BindingApplyResult>(new BindingApplyResult
                {
                    Outcome = "applied",
                    Actual = new PluginSelection { Effect = parameters.Effect, Layout = parameters.Layout }
                }));
            }

            public Task<PluginGatewayResult<BindingReleaseResult>> ReleaseBindingAsync(
                string pluginId,
                BindingReleaseParameters parameters,
                CancellationToken cancellationToken = default) => Task.FromResult(
                    new PluginGatewayResult<BindingReleaseResult>(new BindingReleaseResult
                    {
                        Outcome = ReleaseOutcome
                    }));
        }
    }
}
