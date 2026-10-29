using FocusRgb.Core.Commands;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Selection;

namespace FocusRgb.Core.Tests
{
    public sealed class ProfileChangePopupTests
    {
        [Fact]
        public void Update_ShowsAutomaticChangesAndReplacesRapidTransitions()
        {
            var decision = new ProfileChangePopupDecision();
            Assert.Null(decision.Update(Snapshot("default")));
            Assert.Contains("Movie", decision.Update(Snapshot("movie")), StringComparison.Ordinal);
            Assert.Contains("Music", decision.Update(Snapshot("music")), StringComparison.Ordinal);
            Assert.Contains("No matching app", decision.Update(Snapshot("default")), StringComparison.Ordinal);
            Assert.Null(decision.Update(Snapshot("default")));
        }

        [Theory]
        [InlineData("manual", "ui", "manual", true)]
        [InlineData("automatic", "ui", "restored", true)]
        [InlineData("automatic", "output", "rule", true)]
        [InlineData("automatic", "observation", "observation-pending", true)]
        [InlineData("automatic", "observation", "rule", false)]
        public void Update_SuppressesNonAutomaticTransitions(string mode, string source, string reason, bool enabled)
        {
            var decision = new ProfileChangePopupDecision();
            decision.Update(Snapshot("default"));
            Assert.Null(decision.Update(Snapshot("movie", mode, source, reason, enabled)));
            Assert.Null(decision.Update(Snapshot("movie")));
        }

        [Theory]
        [InlineData("")]
        [InlineData(",\"showProfileChangePopup\":null")]
        public void Load_DefaultsPopupOnForExistingSchemaV5(string property)
        {
            var defaults = SettingsJson.Serialize(SettingsJson.LoadDefaults().Settings!);
            var root = System.Text.Json.Nodes.JsonNode.Parse(defaults)!;
            root["schemaVersion"] = 5;
            root["application"]!.AsObject().Remove("showProfileChangePopup");
            if (property.Length > 0) root["application"]!["showProfileChangePopup"] = null;
            var loaded = SettingsJson.Load(root.ToJsonString());
            Assert.True(loaded.IsSuccess);
            Assert.True(loaded.Settings!.Application!.ShowProfileChangePopup ?? true);
            Assert.Equal(SettingsJson.CurrentSchemaVersion, loaded.Settings.SchemaVersion);
        }

        private static ApplicationStateSnapshot Snapshot(string profile, string mode = "automatic",
            string source = "observation", string reason = "unmatched-default", bool enabled = true)
        {
            var settings = System.Text.Json.Nodes.JsonNode.Parse(SettingsJson.Serialize(SettingsJson.LoadDefaults().Settings!))!;
            settings["application"]!["showProfileChangePopup"] = enabled;
            return new ApplicationStateSnapshot
            {
                Settings = SettingsJson.Load(settings.ToJsonString()).Settings!,
                GameCatalog = GameCatalogJson.CreateDefault(),
                SelectionState = new SelectionStateDocument { Mode = mode, LastSelectedProfile = profile, ManualProfile = profile },
                SelectionReason = new SelectionReason { Kind = reason },
                Source = source
            };
        }
    }
}
