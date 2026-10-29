using System.Text.Json.Nodes;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Persistence;
using FocusRgb.TestSupport;

namespace FocusRgb.Core.Tests
{
    public sealed class DebuggingModeSettingsTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Migration_PreservesLoggingAndSpeedIncludingLastKnownGood(bool logging)
        {
            var legacy = JsonNode.Parse(SettingsJson.Serialize(SettingsJson.LoadDefaults().Settings!))!;
            legacy["schemaVersion"] = 6;
            legacy["application"]!.AsObject().Remove("debuggingMode");
            legacy["application"]!["verboseLogging"] = logging;
            legacy["application"]!["switchingSpeed"] = "fast";
            var migrated = SettingsJson.Load(legacy.ToJsonString());
            Assert.True(migrated.IsSuccess);
            Assert.Equal(7, migrated.Settings!.SchemaVersion);
            Assert.Equal(logging, migrated.Settings.Application!.DebuggingMode);
            Assert.Equal(logging, migrated.Settings.Application.VerboseLogging);
            Assert.Equal("fast", migrated.Settings.Application.SwitchingSpeed);

            using var directory = new TempDirectory(nameof(DebuggingModeSettingsTests));
            var store = new LocalStateStore(directory.Path);
            Assert.True((await store.InitializeAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken)).IsSuccess);
            await File.WriteAllTextAsync(Path.Combine(directory.Path, LocalStateStore.SettingsFileName), "broken", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory.Path, LocalStateStore.SettingsRecoveryFileName), legacy.ToJsonString(), TestContext.Current.CancellationToken);
            var recovered = await store.InitializeAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
            Assert.True(recovered.IsSuccess);
            Assert.Equal(logging, recovered.Snapshot!.Settings.Application!.DebuggingMode);
            Assert.Equal(logging, recovered.Snapshot.Settings.Application.VerboseLogging);
            foreach (var file in new[] { LocalStateStore.SettingsFileName, LocalStateStore.SettingsRecoveryFileName })
            {
                var saved = SettingsJson.Load(await File.ReadAllTextAsync(Path.Combine(directory.Path, file), TestContext.Current.CancellationToken));
                Assert.Equal(7, saved.Settings!.SchemaVersion);
                Assert.Equal(logging, saved.Settings.Application!.DebuggingMode);
            }
        }

        [Fact]
        public void Defaults_DisableDebuggingAndKeepLoggingChoiceWithoutMode()
        {
            var defaults = SettingsJson.LoadDefaults().Settings!;
            Assert.False(defaults.Application!.DebuggingMode);
            Assert.False(defaults.Application.VerboseLogging);
            var withoutMode = JsonNode.Parse(SettingsJson.Serialize(defaults))!;
            withoutMode["application"]!["verboseLogging"] = true;
            var kept = SettingsJson.Load(withoutMode.ToJsonString());
            Assert.True(kept.IsSuccess);
            Assert.True(kept.Settings!.Application!.VerboseLogging);
            Assert.False(kept.Settings.Application.DebuggingMode);
        }
    }
}
