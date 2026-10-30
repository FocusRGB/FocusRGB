using System.Text.Json.Nodes;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Persistence;
using FocusRgb.TestSupport;

namespace FocusRgb.Core.Tests
{
    public sealed class NotificationSettingsTests
    {
        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public async Task MigrationAndRecovery_DefaultNotificationWithoutChangingDebuggingOrSpeed(bool debugging, bool logging)
        {
            var legacy = JsonNode.Parse(SettingsJson.Serialize(SettingsJson.LoadDefaults().Settings!))!;
            legacy["schemaVersion"] = 7;
            legacy["application"]!.AsObject().Remove("notification");
            legacy["application"]!["debuggingMode"] = debugging;
            legacy["application"]!["verboseLogging"] = logging;
            legacy["application"]!["switchingSpeed"] = "fast";
            using var directory = new TempDirectory(nameof(NotificationSettingsTests));
            var store = new LocalStateStore(directory.Path);
            await store.InitializeAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory.Path, LocalStateStore.SettingsFileName), "broken", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory.Path, LocalStateStore.SettingsRecoveryFileName), legacy.ToJsonString(), TestContext.Current.CancellationToken);
            var recovered = await store.InitializeAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
            Assert.True(recovered.IsSuccess);
            var application = recovered.Snapshot!.Settings.Application!;
            Assert.Equal(8, recovered.Snapshot.Settings.SchemaVersion);
            Assert.Equal(debugging, application.DebuggingMode);
            Assert.Equal(logging, application.VerboseLogging);
            Assert.Equal("fast", application.SwitchingSpeed);
            Assert.Equal("medium", application.Notification!.Size);
            Assert.Equal("dark", application.Notification.Background);
            Assert.Equal(3, application.Notification.DurationSeconds);
        }

        [Theory]
        [InlineData("size", "huge")]
        [InlineData("background", "transparent")]
        [InlineData("durationSeconds", "100")]
        [InlineData("durationSeconds", "-1")]
        public void Load_RejectsUnsupportedAppearanceValues(string property, string value)
        {
            var json = JsonNode.Parse(SettingsJson.Serialize(SettingsJson.LoadDefaults().Settings!))!;
            json["application"]!["notification"]![property] = property == "durationSeconds" ? JsonValue.Create(int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)) : JsonValue.Create(value);
            Assert.False(SettingsJson.Load(json.ToJsonString()).IsSuccess);
        }
    }
}
