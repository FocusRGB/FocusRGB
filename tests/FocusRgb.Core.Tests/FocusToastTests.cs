using System.Text.Json.Nodes;
using FocusRgb.Core.Commands;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Selection;

namespace FocusRgb.Core.Tests
{
    public sealed class FocusToastTests
    {
        [Theory]
        [InlineData("fast", 300)]
        [InlineData("normal", 750)]
        [InlineData("smooth", 2000)]
        public void Toast_UsesFocusNameOnlyAfterPresetCommits(string speed, int milliseconds)
        {
            var json = JsonNode.Parse(SettingsJson.Serialize(SettingsJson.LoadDefaults().Settings!))!;
            json["application"]!["switchingSpeed"] = speed;
            json["applicationGroups"]![0]!["displayName"] = "Code work";
            var settings = SettingsJson.Load(json.ToJsonString()).Settings!;
            var clock = new Clock();
            var engine = new SelectionEngine(settings, clock);
            var toast = new ProfileChangePopupDecision();
            Assert.Null(toast.Update(Snapshot(settings, "default", SelectionReason.Restored())));
            var observation = new ApplicationObservation { ForegroundExecutable = "Code.exe", RunningExecutables = ["Code.exe"] };
            var initial = engine.Select(SelectionMode.Automatic, "default", observation, DateTimeOffset.UtcNow);
            Assert.Null(toast.Update(Snapshot(settings, initial.ProfileId, initial.Reason)));
            clock.Advance(milliseconds - 1);
            var pending = engine.Select(SelectionMode.Automatic, "default", observation, DateTimeOffset.UtcNow);
            Assert.Null(toast.Update(Snapshot(settings, pending.ProfileId, pending.Reason)));
            clock.Advance(1);
            var committed = engine.Select(SelectionMode.Automatic, "default", observation, DateTimeOffset.UtcNow);
            var announcement = toast.Update(Snapshot(settings, committed.ProfileId, committed.Reason));
            Assert.Equal("Focus: Code work\nOutput: None", announcement);
            Assert.Null(toast.Update(Snapshot(settings, committed.ProfileId, committed.Reason)));
        }

        private static ApplicationStateSnapshot Snapshot(SettingsDocument settings, string profile, SelectionReason reason) => new()
        {
            Settings = settings,
            GameCatalog = GameCatalogJson.CreateDefault(),
            SelectionState = new SelectionStateDocument { Mode = "automatic", LastSelectedProfile = profile, ManualProfile = "default" },
            SelectionReason = reason,
            Source = "observation"
        };

        [Fact]
        public void Toast_UsesFriendlyWildcardFocusName()
        {
            var settings = SettingsJson.LoadDefaults().Settings!;
            settings.Rules!.Add(new RuleSettings
            {
                Id = "any-game-movie", Profile = "movie", Enabled = true, Eligibility = "while-focused",
                Match = new ApplicationMatch { Game = new GameMatch { Launcher = "*", Id = "*" } }
            });
            var toast = new ProfileChangePopupDecision();
            toast.Update(Snapshot(settings, "default", SelectionReason.Restored()));
            Assert.StartsWith("Focus: Any known game", toast.Update(Snapshot(settings, "movie", SelectionReason.Rule("any-game-movie"))), StringComparison.Ordinal);
        }

        private sealed class Clock : TimeProvider
        {
            private long _timestamp;
            public override long TimestampFrequency => 1000;
            public override long GetTimestamp() => _timestamp;
            public void Advance(int milliseconds) => _timestamp += milliseconds;
        }
    }
}
