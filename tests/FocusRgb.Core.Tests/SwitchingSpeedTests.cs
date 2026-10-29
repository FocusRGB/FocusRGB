using System.Text.Json.Nodes;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Selection;

namespace FocusRgb.Core.Tests
{
    public sealed class SwitchingSpeedTests
    {
        [Theory]
        [InlineData("fast", 300)]
        [InlineData("normal", 750)]
        [InlineData("smooth", 2000)]
        public void Select_RequiresStableForegroundForEntirePreset(string speed, int milliseconds)
        {
            var root = Defaults();
            root["application"]!["switchingSpeed"] = speed;
            var clock = new Clock();
            var engine = new SelectionEngine(SettingsJson.Load(root.ToJsonString()).Settings!, clock);
            var code = Observation("Code.exe");
            engine.Select(SelectionMode.Automatic, "default", code, DateTimeOffset.UtcNow);
            clock.Advance(milliseconds - 1);
            Assert.Equal("default", engine.Select(SelectionMode.Automatic, "default", code, DateTimeOffset.UtcNow).ProfileId);
            clock.Advance(1);
            Assert.Equal("programming", engine.Select(SelectionMode.Automatic, "default", code, DateTimeOffset.UtcNow).ProfileId);

            for (var index = 0; index < 10; index++)
            {
                clock.Advance(100);
                var observation = Observation(index % 2 == 0 ? "stremio-shell-ng.exe" : "Code.exe");
                Assert.Equal("programming", engine.Select(SelectionMode.Automatic, "programming", observation, DateTimeOffset.UtcNow).ProfileId);
            }
            var movie = Observation("stremio-shell-ng.exe");
            engine.Select(SelectionMode.Automatic, "programming", movie, DateTimeOffset.UtcNow);
            clock.Advance(milliseconds);
            Assert.Equal("movie", engine.Select(SelectionMode.Automatic, "programming", movie, DateTimeOffset.UtcNow).ProfileId);
        }

        [Fact]
        public void Load_MigratesSchemaFiveToNormal_WithoutLosingPreferences()
        {
            var root = Defaults();
            root["schemaVersion"] = 5;
            root["application"]!.AsObject().Remove("switchingSpeed");
            root["application"]!["includeBetaPlugins"] = false;
            root["application"]!["futurePreference"] = "keep";
            root["selection"]!["debounceMs"] = 2000;
            var loaded = SettingsJson.Load(root.ToJsonString());
            Assert.True(loaded.IsSuccess);
            Assert.Equal(SettingsJson.CurrentSchemaVersion, loaded.Settings!.SchemaVersion);
            Assert.Equal("normal", loaded.Settings.Application!.SwitchingSpeed);
            Assert.Equal(750, loaded.Settings.Selection!.DebounceMs);
            Assert.False(loaded.Settings.Application.IncludeBetaPlugins);
            Assert.Equal("keep", loaded.Settings.Application.AdditionalProperties!["futurePreference"].GetString());
            Assert.Equal("normal", SettingsJson.Load(SettingsJson.Serialize(loaded.Settings)).Settings!.Application!.SwitchingSpeed);
        }

        [Fact]
        public void Load_RejectsUnknownPreset()
        {
            var root = Defaults();
            root["application"]!["switchingSpeed"] = "instant";
            Assert.Contains(SettingsJson.Load(root.ToJsonString()).Errors, error => error.Path == "application.switchingSpeed");
        }

        private static JsonNode Defaults() => JsonNode.Parse(SettingsJson.Serialize(SettingsJson.LoadDefaults().Settings!))!;
        private static ApplicationObservation Observation(string foreground) => new()
        {
            ForegroundExecutable = foreground, RunningExecutables = ["Code.exe", "stremio-shell-ng.exe"]
        };
        private sealed class Clock : TimeProvider
        {
            private long _ticks;
            public override long TimestampFrequency => 1000;
            public override long GetTimestamp() => _ticks;
            public void Advance(int milliseconds) => _ticks += milliseconds;
        }
    }
}
