using System.Text.Json;
using System.Text.Json.Nodes;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Persistence;

namespace FocusRgb.Core.Tests
{
    /// <summary>Checks atomic local persistence, startup restoration, and explicit recovery.</summary>
    public sealed class LocalStateStoreTests : IDisposable
    {
        private static readonly DateTimeOffset Start = new(2026, 10, 12, 14, 0, 0, TimeSpan.Zero);
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            "FocusRgb.Core.Tests",
            Guid.NewGuid().ToString("N"));

        [Fact]
        public async Task InitializeAsync_CreatesManualDefault_WhenFilesAreMissing()
        {
            var result = await Store().InitializeAsync(Start, TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Equal("manual", result.Snapshot!.State.Mode);
            Assert.Equal("default", result.Snapshot.State.ManualProfile);
            Assert.Equal("default", result.Snapshot.Desired.ProfileId);
            Assert.Equal("first-run", result.Snapshot.Desired.Reason.Kind);
            Assert.Empty(result.Snapshot.Recoveries);
            Assert.True(File.Exists(PathFor(LocalStateStore.SettingsFileName)));
            Assert.True(File.Exists(PathFor(LocalStateStore.SettingsRecoveryFileName)));
            Assert.True(File.Exists(PathFor(LocalStateStore.StateFileName)));
            Assert.True(File.Exists(PathFor(LocalStateStore.NativeSetupFileName)));
        }

        [Fact]
        public async Task InitializeAsync_MigratesSchemaOneSettingsAndPersistsVersionTwo()
        {
            var firstRun = await Store().InitializeAsync(Start, TestContext.Current.CancellationToken);
            Assert.True(firstRun.IsSuccess);
            var legacy = JsonNode.Parse(SettingsJson.Serialize(firstRun.Snapshot!.Settings))!;
            legacy["schemaVersion"] = 1;
            legacy.AsObject().Remove("application");
            await File.WriteAllTextAsync(
                PathFor(LocalStateStore.SettingsFileName),
                legacy.ToJsonString(),
                TestContext.Current.CancellationToken);

            var restarted = await Store().InitializeAsync(Start.AddMinutes(1), TestContext.Current.CancellationToken);
            var persisted = JsonNode.Parse(await File.ReadAllTextAsync(
                PathFor(LocalStateStore.SettingsFileName),
                TestContext.Current.CancellationToken))!;

            Assert.True(restarted.IsSuccess);
            Assert.Equal(SettingsJson.CurrentSchemaVersion, restarted.Snapshot!.Settings.SchemaVersion);
            Assert.Equal("system", restarted.Snapshot.Settings.Application!.Theme);
            Assert.Equal(SettingsJson.CurrentSchemaVersion, persisted["schemaVersion"]!.GetValue<int>());
            Assert.NotNull(persisted["application"]);
        }

        [Fact]
        public async Task InitializeAsync_RestoresLastKnownGoodSettings_WhenCurrentFileIsInvalid()
        {
            var store = Store();
            var firstRun = await store.InitializeAsync(Start, TestContext.Current.CancellationToken);
            Assert.True(firstRun.IsSuccess);
            var current = firstRun.Snapshot!.Settings;
            var dark = new SettingsDocument
            {
                SchemaVersion = current.SchemaVersion,
                Application = new ApplicationSettings
                {
                    CloseButtonBehavior = current.Application!.CloseButtonBehavior,
                    StartMinimized = current.Application.StartMinimized,
                    Theme = "dark",
                    VerboseLogging = current.Application.VerboseLogging
                },
                Selection = current.Selection,
                Profiles = current.Profiles,
                Rules = current.Rules,
                AdditionalProperties = current.AdditionalProperties
            };
            Assert.True((await store.SaveSettingsAsync(dark, TestContext.Current.CancellationToken)).IsSuccess);
            await File.WriteAllTextAsync(
                PathFor(LocalStateStore.SettingsFileName),
                "{",
                TestContext.Current.CancellationToken);

            var restarted = await Store().InitializeAsync(Start.AddMinutes(1), TestContext.Current.CancellationToken);

            Assert.True(restarted.IsSuccess);
            Assert.Equal("dark", restarted.Snapshot!.Settings.Application!.Theme);
            var recovery = Assert.Single(restarted.Snapshot.Recoveries);
            Assert.Equal(LocalStateStore.SettingsFileName, recovery.FileName);
        }

        [Fact]
        public async Task InitializeAsync_UsesDefaults_WhenCurrentAndRecoverySettingsAreInvalid()
        {
            var firstRun = await Store().InitializeAsync(Start, TestContext.Current.CancellationToken);
            Assert.True(firstRun.IsSuccess);
            await File.WriteAllTextAsync(PathFor(LocalStateStore.SettingsFileName), "{", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(PathFor(LocalStateStore.SettingsRecoveryFileName), "{", TestContext.Current.CancellationToken);

            var restarted = await Store().InitializeAsync(Start.AddMinutes(1), TestContext.Current.CancellationToken);

            Assert.True(restarted.IsSuccess);
            Assert.Equal("system", restarted.Snapshot!.Settings.Application!.Theme);
            Assert.Equal(2, restarted.Snapshot.Recoveries.Count);
        }

        [Fact]
        public async Task InitializeAsync_RestoresAutomaticDesiredProfile_AfterRestart()
        {
            var store = Store();
            var firstRun = await store.InitializeAsync(Start, TestContext.Current.CancellationToken);
            var state = AutomaticState("default", "music", Start.AddMinutes(1));
            var save = await store.SaveStateAsync(state, firstRun.Snapshot!.Settings, TestContext.Current.CancellationToken);

            var restarted = await Store().InitializeAsync(Start.AddMinutes(2), TestContext.Current.CancellationToken);

            Assert.True(save.IsSuccess);
            Assert.True(restarted.IsSuccess);
            Assert.Equal("automatic", restarted.Snapshot!.State.Mode);
            Assert.Equal("music", restarted.Snapshot.Desired.ProfileId);
            Assert.Equal("restored", restarted.Snapshot.Desired.Reason.Kind);
        }

        [Fact]
        public async Task InitializeAsync_ResetsToManualDefault_WhenSavedProfileNoLongerExists()
        {
            var firstRun = await Store().InitializeAsync(Start, TestContext.Current.CancellationToken);
            Assert.True(firstRun.IsSuccess);
            var invalidState = AutomaticState("missing", "missing", Start.AddMinutes(1));
            await File.WriteAllTextAsync(PathFor(LocalStateStore.StateFileName), StateJson.Serialize(invalidState), TestContext.Current.CancellationToken);

            var restarted = await Store().InitializeAsync(Start.AddMinutes(2), TestContext.Current.CancellationToken);

            Assert.True(restarted.IsSuccess);
            Assert.Equal("manual", restarted.Snapshot!.State.Mode);
            Assert.Equal("default", restarted.Snapshot.Desired.ProfileId);
            Assert.Equal("invalid-state-reset", restarted.Snapshot.Desired.Reason.Kind);
            var recovery = Assert.Single(restarted.Snapshot.Recoveries);
            Assert.Equal(LocalStateStore.StateFileName, recovery.FileName);
            Assert.Contains("unknown profile", recovery.Message, StringComparison.Ordinal);
            Assert.True(File.Exists(recovery.ArchivedPath));
        }

        [Theory]
        [InlineData(LocalStateStore.SettingsFileName)]
        [InlineData(LocalStateStore.StateFileName)]
        [InlineData(LocalStateStore.NativeSetupFileName)]
        public async Task InitializeAsync_ArchivesCorruptFile_BeforeCreatingReplacement(string fileName)
        {
            var firstRun = await Store().InitializeAsync(Start, TestContext.Current.CancellationToken);
            Assert.True(firstRun.IsSuccess);
            await File.WriteAllTextAsync(PathFor(fileName), "{", TestContext.Current.CancellationToken);

            var restarted = await Store().InitializeAsync(Start.AddMinutes(1), TestContext.Current.CancellationToken);

            Assert.True(restarted.IsSuccess);
            var recovery = Assert.Single(restarted.Snapshot!.Recoveries);
            Assert.Equal(fileName, recovery.FileName);
            Assert.True(File.Exists(recovery.ArchivedPath));
            Assert.Equal("{", await File.ReadAllTextAsync(recovery.ArchivedPath, TestContext.Current.CancellationToken));
            Assert.True(File.Exists(PathFor(fileName)));
        }

        [Theory]
        [InlineData(LocalStateStore.SettingsFileName)]
        [InlineData(LocalStateStore.StateFileName)]
        [InlineData(LocalStateStore.NativeSetupFileName)]
        public async Task InitializeAsync_ArchivesDocument_WhenSchemaVersionIsMissing(string fileName)
        {
            var firstRun = await Store().InitializeAsync(Start, TestContext.Current.CancellationToken);
            Assert.True(firstRun.IsSuccess);
            await File.WriteAllTextAsync(PathFor(fileName), "{}", TestContext.Current.CancellationToken);

            var restarted = await Store().InitializeAsync(Start.AddMinutes(1), TestContext.Current.CancellationToken);

            Assert.True(restarted.IsSuccess);
            var recovery = Assert.Single(restarted.Snapshot!.Recoveries);
            Assert.Equal(fileName, recovery.FileName);
            Assert.Equal("{}", await File.ReadAllTextAsync(recovery.ArchivedPath, TestContext.Current.CancellationToken));
            Assert.True(File.Exists(PathFor(fileName)));
        }

        [Fact]
        public async Task InitializeAsync_PreservesUnsupportedVersion_WithoutReplacingIt()
        {
            var firstRun = await Store().InitializeAsync(Start, TestContext.Current.CancellationToken);
            Assert.True(firstRun.IsSuccess);
            const string unsupported = """
                {
                  "schemaVersion": 2,
                  "mode": "manual",
                  "manualProfile": "default",
                  "lastSelectedProfile": "default",
                  "updatedAt": "2026-10-12T14:00:00Z"
                }
                """;
            await File.WriteAllTextAsync(PathFor(LocalStateStore.StateFileName), unsupported, TestContext.Current.CancellationToken);

            var restarted = await Store().InitializeAsync(Start.AddMinutes(1), TestContext.Current.CancellationToken);

            Assert.False(restarted.IsSuccess);
            Assert.Equal(LocalStateStore.StateFileName, restarted.Error!.FileName);
            Assert.Equal(unsupported, await File.ReadAllTextAsync(PathFor(LocalStateStore.StateFileName), TestContext.Current.CancellationToken));
            Assert.Empty(Directory.GetFiles(_directory, "state.json.corrupt-*"));
        }

        [Fact]
        public async Task SaveSettingsAsync_PreservesPreviousFile_WhenSerializationFails()
        {
            var store = Store();
            var firstRun = await store.InitializeAsync(Start, TestContext.Current.CancellationToken);
            Assert.True(firstRun.IsSuccess);
            var previous = await File.ReadAllTextAsync(PathFor(LocalStateStore.SettingsFileName), TestContext.Current.CancellationToken);
            var current = firstRun.Snapshot!.Settings;
            var unserializable = new SettingsDocument
            {
                SchemaVersion = current.SchemaVersion,
                Application = current.Application,
                Selection = current.Selection,
                Profiles = current.Profiles,
                Rules = current.Rules,
                AdditionalProperties = new Dictionary<string, JsonElement>
                {
                    ["invalid"] = default
                }
            };

            var result = await store.SaveSettingsAsync(unserializable, TestContext.Current.CancellationToken);

            Assert.False(result.IsSuccess);
            Assert.Equal(previous, await File.ReadAllTextAsync(PathFor(LocalStateStore.SettingsFileName), TestContext.Current.CancellationToken));
            Assert.Empty(Directory.GetFiles(_directory, ".settings.json.*.tmp"));
        }

        [Fact]
        public async Task SaveSettingsAndStateAsync_DoNotOverwriteNativeFallbackSnapshot()
        {
            var store = Store();
            var firstRun = await store.InitializeAsync(Start, TestContext.Current.CancellationToken);
            Assert.True(firstRun.IsSuccess);
            var nativeBefore = await File.ReadAllTextAsync(PathFor(LocalStateStore.NativeSetupFileName), TestContext.Current.CancellationToken);
            var settingsJson = JsonNode.Parse(SettingsJson.Serialize(firstRun.Snapshot!.Settings))!;
            settingsJson["profiles"]![0]!["bindings"]![0]!["effect"]!["displayName"] = "Preview effect";
            var changedSettings = SettingsJson.Load(settingsJson.ToJsonString()).Settings!;

            var settingsSave = await store.SaveSettingsAsync(changedSettings, TestContext.Current.CancellationToken);
            var stateSave = await store.SaveStateAsync(AutomaticState("default", "programming", Start.AddMinutes(1)), changedSettings, TestContext.Current.CancellationToken);

            Assert.True(settingsSave.IsSuccess);
            Assert.True(stateSave.IsSuccess);
            Assert.Equal(nativeBefore, await File.ReadAllTextAsync(PathFor(LocalStateStore.NativeSetupFileName), TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task SaveStateAsync_PersistsDesiredIntent_WithoutAppliedOutputState()
        {
            var store = Store();
            var firstRun = await store.InitializeAsync(Start, TestContext.Current.CancellationToken);
            var save = await store.SaveStateAsync(AutomaticState("movie", "music", Start.AddMinutes(1)), firstRun.Snapshot!.Settings, TestContext.Current.CancellationToken);

            var json = JsonNode.Parse(await File.ReadAllTextAsync(PathFor(LocalStateStore.StateFileName), TestContext.Current.CancellationToken))!;

            Assert.True(save.IsSuccess);
            Assert.Equal("music", json["lastSelectedProfile"]!.GetValue<string>());
            Assert.Null(json["applied"]);
            Assert.Null(json["output"]);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        private LocalStateStore Store() => new(_directory);

        private string PathFor(string fileName) => Path.Combine(_directory, fileName);

        private static SelectionStateDocument AutomaticState(
            string manualProfile,
            string selectedProfile,
            DateTimeOffset updatedAt) => new()
            {
                SchemaVersion = 1,
                Mode = "automatic",
                ManualProfile = manualProfile,
                LastSelectedProfile = selectedProfile,
                UpdatedAt = updatedAt
            };
    }
}
