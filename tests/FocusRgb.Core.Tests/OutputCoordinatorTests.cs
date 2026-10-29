using System.Text.Json.Nodes;
using FocusRgb.Core.Protocol;
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
            Assert.Equal("fake-output", coordinator.CurrentOutput!.PluginId);
            Assert.Equal("provider", coordinator.CurrentOutput.RenderingAuthority);
            Assert.Equal(["provider-managed"], coordinator.CurrentOutput.Scopes);
            Assert.Equal(coordinator.CurrentOutput.AppliedAt, coordinator.LastOutputAt);
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
            Assert.Equal("unavailable", Assert.Single(status.Applied!.Bindings).Outcome);
            Assert.Equal("plugin-absent", Assert.Single(status.Effective.Skipped).ReasonCode);
            Assert.Null(coordinator.CurrentOutput);
            Assert.Null(coordinator.LastOutputAt);
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

            await coordinator.ReportAvailabilityAsync("fake-output", available: false,
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
            var applied = await coordinator.ApplyAsync(settings, State("default"), SelectionReason.Manual(),
                TestContext.Current.CancellationToken);

            var unavailable = await coordinator.ReportAvailabilityAsync(
                "fake-output", available: false, TestContext.Current.CancellationToken);

            Assert.Single(gateway.Applied);
            Assert.True(unavailable!.Sequence > applied.Sequence);
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
            await coordinator.ReportAvailabilityAsync("fake-output", available: false,
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

        [Fact]
        public async Task ApplyAsync_TriesRemainingSiblingBeforeNestedFallbacks()
        {
            var gateway = new FakeGateway("plugin-music");
            using var coordinator = new OutputCoordinator(gateway, NativeSetupJson.CreateDefault());
            var settings = Settings(
                ("default", "missing-default", "provider-managed", null),
                ("movie", "missing-movie", "provider-managed", null),
                ("programming", "missing-programming", "provider-managed", null),
                ("music", "plugin-music", "provider-managed", null));
            SetFallback(settings, "default", "movie", "music");
            SetFallback(settings, "movie", "programming");

            var status = await coordinator.ApplyAsync(
                settings, State("default"), SelectionReason.Manual(), TestContext.Current.CancellationToken);

            Assert.Equal("music", status.Effective.ProfileId);
            Assert.Equal(1, status.Effective.CandidateIndex);
            Assert.Equal(["default", "movie"],
                status.Effective.Skipped.Select(item => item.ProfileId));
        }

        [Fact]
        public async Task ApplyAsync_TriesFailedFallbackProfilesOwnFallbacks()
        {
            var gateway = new FakeGateway("plugin-music");
            using var coordinator = new OutputCoordinator(gateway, NativeSetupJson.CreateDefault());
            var settings = Settings(
                ("default", "missing-default", "provider-managed", null),
                ("movie", "missing-movie", "provider-managed", null),
                ("music", "plugin-music", "provider-managed", null));
            SetFallback(settings, "default", "movie");
            SetFallback(settings, "movie", "music");

            var status = await coordinator.ApplyAsync(
                settings, State("default"), SelectionReason.Manual(), TestContext.Current.CancellationToken);

            Assert.Equal("music", status.Effective.ProfileId);
            Assert.Equal(0, status.Effective.CandidateIndex);
            Assert.Equal(["default", "movie"],
                status.Effective.Skipped.Select(item => item.ProfileId));
        }

        [Fact]
        public async Task ApplyAsync_ReportsDesiredFailure_WhenEveryFallbackFails()
        {
            using var coordinator = new OutputCoordinator(new FakeGateway(), NativeSetupJson.CreateDefault());
            var original = Settings(
                ("default", "missing-default", "provider-managed", null),
                ("movie", "missing-movie", "provider-managed", null));
            var root = JsonNode.Parse(SettingsJson.Serialize(original))!;
            var movie = root["profiles"]!.AsArray()
                .Single(profile => profile!["id"]!.GetValue<string>() == "movie")!;
            movie["bindings"]![0]!["id"] = "movie-binding";
            var settings = SettingsJson.Load(root.ToJsonString()).Settings!;
            SetFallback(settings, "default", "movie");
            SetFallback(settings, "movie");

            var status = await coordinator.ApplyAsync(
                settings, State("default"), SelectionReason.Manual(), TestContext.Current.CancellationToken);

            Assert.Equal("default", status.Applied!.ProfileId);
            Assert.Equal("plugin-absent", Assert.Single(status.Applied.Bindings).ReasonCode);
            var pending = Assert.Single(status.Pending);
            Assert.Equal("main", pending.BindingId);
            Assert.Equal("plugin-absent", pending.ReasonCode);
            Assert.Contains(status.Effective.Skipped, item => item.ProfileId == "movie");
        }

        [Theory]
        [InlineData("timeout")]
        [InlineData("malformed-response")]
        [InlineData("plugin-exited")]
        public async Task ApplyAsync_RetriesAfterTransientGatewayFailure(string errorCode)
        {
            var gateway = new FakeGateway("fake-output") { ApplyErrorCode = errorCode };
            using var coordinator = new OutputCoordinator(gateway, NativeSetupJson.CreateDefault());
            var settings = Settings(("default", "fake-output", "provider-managed", null));

            var failed = await coordinator.ApplyAsync(
                settings, State("default"), SelectionReason.Manual(), TestContext.Current.CancellationToken);
            gateway.ApplyErrorCode = null;
            var retried = await coordinator.ApplyAsync(
                settings, State("default"), SelectionReason.Manual(), TestContext.Current.CancellationToken);

            Assert.Equal(errorCode, Assert.Single(failed.Effective.Skipped).ReasonCode);
            Assert.Equal("applied", Assert.Single(retried.Applied!.Bindings).Outcome);
            Assert.Single(gateway.Applied);
        }

        [Fact]
        public async Task ApplyAsync_DoesNotRetryTimedOutPluginThroughFallbacks()
        {
            var gateway = new FakeGateway("fake-output") { ApplyErrorCode = "timeout" };
            using var coordinator = new OutputCoordinator(gateway, NativeSetupJson.CreateDefault());
            var settings = Settings(
                ("default", "fake-output", "provider-managed", null),
                ("movie", "fake-output", "provider-managed", null),
                ("music", "fake-output", "provider-managed", null));
            SetFallback(settings, "default", "movie", "music");

            var status = await coordinator.ApplyAsync(
                settings, State("default"), SelectionReason.Manual(), TestContext.Current.CancellationToken);

            Assert.Equal(1, gateway.ApplyCalls);
            Assert.Equal("Transient request failure.", status.AppliedDetail);
        }

        [Fact]
        public async Task Recovery_DeclineCanBeOverriddenByExplicitSelection()
        {
            var gateway = new FakeGateway();
            using var coordinator = new OutputCoordinator(gateway, NativeSetupJson.CreateDefault());
            var settings = Settings(("default", "fake-output", "provider-managed", null));
            await coordinator.ApplyAsync(
                settings, State("default"), SelectionReason.Manual(), TestContext.Current.CancellationToken);
            await coordinator.ReportAvailabilityAsync(
                "fake-output", false, TestContext.Current.CancellationToken);
            gateway.Enable("fake-output");
            var recovered = await coordinator.ReportAvailabilityAsync(
                "fake-output", true, TestContext.Current.CancellationToken);
            await coordinator.RespondToNotificationAsync(
                recovered!.Notifications[0].Id, "decline", TestContext.Current.CancellationToken);

            var selected = await coordinator.ApplyAsync(
                settings, State("default"), SelectionReason.Manual(), approveRecovery: true,
                TestContext.Current.CancellationToken);

            Assert.Equal("applied", Assert.Single(selected.Applied!.Bindings).Outcome);
            Assert.Empty(selected.Notifications);
        }

        [Fact]
        public async Task ApplyAsync_AcceptsNothingToReleaseAndOverlapsProviderManagedScope()
        {
            var gateway = new FakeGateway("plugin-a", "plugin-b") { ReleaseOutcome = "nothing-to-release" };
            using var coordinator = new OutputCoordinator(gateway, NativeSetupJson.CreateDefault());
            var settings = Settings(
                ("default", "plugin-a", "provider-managed", null),
                ("movie", "plugin-b", "devices", "desk"));
            await coordinator.ApplyAsync(
                settings, State("default"), SelectionReason.Manual(), TestContext.Current.CancellationToken);

            var status = await coordinator.ApplyAsync(
                settings, State("movie"), SelectionReason.Manual(), TestContext.Current.CancellationToken);

            Assert.Equal("movie", status.Applied!.ProfileId);
            Assert.Single(gateway.Released);
        }

        [Fact]
        public async Task DeadOwnerDoesNotBlockAutomaticSwitching()
        {
            var gateway = new FakeGateway("plugin-a", "plugin-b") { ReleaseErrorCode = "plugin-exited" };
            using var coordinator = new OutputCoordinator(gateway, NativeSetupJson.CreateDefault());
            var settings = Settings(
                ("default", "plugin-a", "devices", "desk"),
                ("movie", "plugin-b", "devices", "desk"));
            await coordinator.ApplyAsync(
                settings, State("default"), SelectionReason.Manual(), TestContext.Current.CancellationToken);

            var unavailable = await coordinator.ReportAvailabilityAsync(
                "plugin-a", false, "plugin-exited", "The plugin process exited.",
                TestContext.Current.CancellationToken);
            var automatic = await coordinator.ApplyAsync(
                settings, State("movie"), SelectionReason.Manual(), TestContext.Current.CancellationToken);
            var selected = await coordinator.ApplyAsync(
                settings, State("movie"), SelectionReason.Manual(), approveRecovery: true,
                TestContext.Current.CancellationToken);

            Assert.Equal("unavailable", Assert.Single(unavailable!.Applied!.Bindings).Outcome);
            Assert.Equal("plugin-exited", Assert.Single(unavailable.Applied.Bindings).ReasonCode);
            Assert.Equal("movie", automatic.Applied!.ProfileId);
            Assert.Equal("applied", Assert.Single(automatic.Applied.Bindings).Outcome);
            Assert.Equal("movie", selected.Applied!.ProfileId);
            Assert.Equal("applied", Assert.Single(selected.Applied.Bindings).Outcome);
            Assert.Contains(gateway.Applied, item => item.Profile == "movie");
        }

        [Fact]
        public async Task ExplicitSelectionAsksUnavailableLiveOwnerToRelease()
        {
            var gateway = new FakeGateway("plugin-a", "plugin-b");
            using var coordinator = new OutputCoordinator(gateway, NativeSetupJson.CreateDefault());
            var settings = Settings(
                ("default", "plugin-a", "devices", "desk"),
                ("movie", "plugin-b", "devices", "desk"));
            await coordinator.ApplyAsync(
                settings, State("default"), SelectionReason.Manual(), TestContext.Current.CancellationToken);
            await coordinator.ReportAvailabilityAsync(
                "plugin-a", false, "provider-not-running", "The provider is unavailable.",
                TestContext.Current.CancellationToken);

            var selected = await coordinator.ApplyAsync(
                settings, State("movie"), SelectionReason.Manual(), approveRecovery: true,
                TestContext.Current.CancellationToken);

            Assert.Single(gateway.Released);
            Assert.Equal("movie", selected.Applied!.ProfileId);
        }

        [Fact]
        public async Task ApplyAsync_ReportsAmbiguousAndRestrictedCatalogEntries()
        {
            var gateway = new FakeGateway("fake-output")
            {
                CatalogItems =
                [
                    Item("Aurora", "one"),
                    Item("Aurora", "two")
                ]
            };
            using var coordinator = new OutputCoordinator(gateway, NativeSetupJson.CreateDefault());
            var settings = Settings(("default", "fake-output", "provider-managed", null));

            var ambiguous = await coordinator.ApplyAsync(
                settings, State("default"), SelectionReason.Manual(), TestContext.Current.CancellationToken);
            gateway.CatalogItems =
            [
                Item("Aurora", "paid", false, "account-restricted")
            ];
            var restricted = await coordinator.ApplyAsync(
                settings, State("default"), SelectionReason.Manual(), TestContext.Current.CancellationToken);

            Assert.Equal("resource-ambiguous", Assert.Single(ambiguous.Effective.Skipped).ReasonCode);
            Assert.Equal("account-restricted", Assert.Single(restricted.Effective.Skipped).ReasonCode);
        }

        [Theory]
        [InlineData("rejected", "account-restricted")]
        [InlineData("unsupported", "capability-unsupported")]
        public async Task ApplyAsync_PreservesPluginBusinessOutcome(string outcome, string reasonCode)
        {
            var gateway = new FakeGateway("fake-output")
            {
                ApplyOutcome = outcome,
                ApplyReason = new PluginReason { Code = reasonCode, Message = "Request was not applied." }
            };
            using var coordinator = new OutputCoordinator(gateway, NativeSetupJson.CreateDefault());

            var status = await coordinator.ApplyAsync(
                Settings(("default", "fake-output", "provider-managed", null)),
                State("default"), SelectionReason.Manual(), TestContext.Current.CancellationToken);

            var binding = Assert.Single(status.Applied!.Bindings);
            Assert.Equal(outcome, binding.Outcome);
            Assert.Equal(reasonCode, binding.ReasonCode);
        }

        [Fact]
        public async Task ApplyAsync_UsesCaseSensitiveCatalogNames()
        {
            var gateway = new FakeGateway("fake-output")
            {
                CatalogItems = [Item("aurora", "wrong-case")]
            };
            using var coordinator = new OutputCoordinator(gateway, NativeSetupJson.CreateDefault());

            var status = await coordinator.ApplyAsync(
                Settings(("default", "fake-output", "provider-managed", null)),
                State("default"), SelectionReason.Manual(), TestContext.Current.CancellationToken);

            Assert.Equal("resource-not-found", Assert.Single(status.Effective.Skipped).ReasonCode);
            Assert.Empty(gateway.Applied);
        }

        [Fact]
        public async Task ApplyAsync_BoundsFallbackCyclesWithoutInventingAReasonCode()
        {
            using var coordinator = new OutputCoordinator(new FakeGateway(), NativeSetupJson.CreateDefault());
            var settings = Settings(
                ("default", "missing-default", "provider-managed", null),
                ("movie", "missing-movie", "provider-managed", null));
            SetFallback(settings, "default", "movie");
            SetFallback(settings, "movie", "default");

            var status = await coordinator.ApplyAsync(
                settings, State("default"), SelectionReason.Manual(), TestContext.Current.CancellationToken);

            Assert.Equal(3, status.Effective.Skipped.Count);
            Assert.DoesNotContain(status.Effective.Skipped, item => item.ReasonCode == "fallback-cycle");
        }

        [Fact]
        public async Task RespondToNotificationAsync_ReturnsNullForUnknownId()
        {
            using var coordinator = new OutputCoordinator(new FakeGateway(), NativeSetupJson.CreateDefault());

            var status = await coordinator.RespondToNotificationAsync(
                "missing", "decline", TestContext.Current.CancellationToken);

            Assert.Null(status);
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

        private static void SetFallback(SettingsDocument settings, string profileId, params string[] profileIds)
        {
            var profile = settings.Profiles!.Single(item => item.Id == profileId);
            profile.Fallback!.Clear();
            profile.Fallback.AddRange(profileIds.Select(id => new FallbackSettings
            {
                Kind = "profile",
                Profile = id
            }));
        }

        private static PluginCatalogItem Item(
            string displayName,
            string resourceId,
            bool available = true,
            string? reasonCode = null) => new()
            {
                ResourceId = resourceId,
                DisplayName = displayName,
                Available = available,
                Reason = reasonCode is null ? null : new PluginReason
                {
                    Code = reasonCode,
                    Message = "Resource is restricted."
                }
            };

        private sealed class FakeGateway(params string[] enabled) : IPluginOutputGateway
        {
            private readonly HashSet<string> _enabled = new(enabled, StringComparer.Ordinal);
            public List<BindingApplyParameters> Applied { get; } = [];
            public List<BindingReleaseParameters> Released { get; } = [];
            public string ReleaseOutcome { get; init; } = "released";
            public string? ReleaseErrorCode { get; init; }
            public string? ApplyErrorCode { get; set; }
            public int ApplyCalls { get; private set; }
            public string ApplyOutcome { get; init; } = "applied";
            public PluginReason? ApplyReason { get; init; }
            public IReadOnlyList<PluginCatalogItem> CatalogItems { get; set; } =
            [
                Item("Aurora", "fake-aurora"),
                Item("Screen Ambience", "fake-screen"),
                Item("Logarithmic Visualizer", "fake-music")
            ];

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
                        Items = CatalogItems
                    }));

            public Task<PluginGatewayResult<BindingApplyResult>> ApplyBindingAsync(
                string pluginId,
                BindingApplyParameters parameters,
                CancellationToken cancellationToken = default)
            {
                ApplyCalls++;
                if (ApplyErrorCode is not null)
                    return Task.FromResult(new PluginGatewayResult<BindingApplyResult>(
                        ApplyErrorCode, "Transient request failure."));
                Applied.Add(parameters);
                return Task.FromResult(new PluginGatewayResult<BindingApplyResult>(new BindingApplyResult
                {
                    Outcome = ApplyOutcome,
                    Reason = ApplyReason,
                    Actual = new PluginSelection { Effect = parameters.Effect, Layout = parameters.Layout }
                }));
            }

            public Task<PluginGatewayResult<BindingReleaseResult>> ReleaseBindingAsync(
                string pluginId,
                BindingReleaseParameters parameters,
                CancellationToken cancellationToken = default)
            {
                Released.Add(parameters);
                return Task.FromResult(ReleaseErrorCode is null
                    ? new PluginGatewayResult<BindingReleaseResult>(new BindingReleaseResult
                    {
                        Outcome = ReleaseOutcome
                    })
                    : new PluginGatewayResult<BindingReleaseResult>(ReleaseErrorCode, "Release failed."));
            }
        }
    }
}
