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
        public async Task InitializeAsync_NormalizesFirstRunTimestampToUtc()
        {
            var local = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.FromHours(2));

            var result = await Store().InitializeAsync(local, TestContext.Current.CancellationToken);

            Assert.Equal(TimeSpan.Zero, result.Snapshot!.State.UpdatedAt!.Value.Offset);
            Assert.Equal(local.UtcDateTime, result.Snapshot.State.UpdatedAt.Value.UtcDateTime);
        }

        [Fact]
        public async Task InitializeAsync_MigratesSchemaOneSettingsAndPersistsCurrentVersion()
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
            Assert.NotNull(persisted["plugins"]);
            Assert.Single(Directory.GetFiles(_directory, "settings.json.schema-1-*.backup"));
        }

        [Fact]
        public async Task InitializeAsync_MigratesSchemaTwoAndKeepsBackup()
        {
            var firstRun = await Store().InitializeAsync(Start, TestContext.Current.CancellationToken);
            Assert.True(firstRun.IsSuccess);
            var legacy = JsonNode.Parse(SettingsJson.Serialize(firstRun.Snapshot!.Settings))!;
            legacy["schemaVersion"] = 2;
            legacy.AsObject().Remove("plugins");
            await File.WriteAllTextAsync(
                PathFor(LocalStateStore.SettingsFileName),
                legacy.ToJsonString(),
                TestContext.Current.CancellationToken);

            var restarted = await Store().InitializeAsync(Start.AddMinutes(1), TestContext.Current.CancellationToken);

            Assert.True(restarted.IsSuccess);
            Assert.Empty(restarted.Snapshot!.Settings.Plugins!.Enabled!);
            Assert.Single(Directory.GetFiles(_directory, "settings.json.schema-2-*.backup"));
        }

        [Fact]
        public async Task InitializeAsync_UsesLastKnownGood_WhenCurrentSettingsAreMissing()
        {
            var store = Store();
            var firstRun = await store.InitializeAsync(Start, TestContext.Current.CancellationToken);
            var recoveryBefore = await File.ReadAllTextAsync(
                PathFor(LocalStateStore.SettingsRecoveryFileName), TestContext.Current.CancellationToken);
            File.Delete(PathFor(LocalStateStore.SettingsFileName));

            var restarted = await store.InitializeAsync(Start.AddMinutes(1), TestContext.Current.CancellationToken);

            Assert.True(firstRun.IsSuccess);
            Assert.True(restarted.IsSuccess);
            Assert.Equal("last-known-good", restarted.Snapshot!.SettingsSource);
            Assert.Equal(recoveryBefore, await File.ReadAllTextAsync(
                PathFor(LocalStateStore.SettingsFileName), TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task InitializeAsync_RefreshesLastKnownGood_AfterValidHandEdit()
        {
            var store = Store();
            var firstRun = await store.InitializeAsync(Start, TestContext.Current.CancellationToken);
            var edited = JsonNode.Parse(SettingsJson.Serialize(firstRun.Snapshot!.Settings))!;
            edited["application"]!["theme"] = "dark";
            await File.WriteAllTextAsync(
                PathFor(LocalStateStore.SettingsFileName), edited.ToJsonString(), TestContext.Current.CancellationToken);

            var restarted = await store.InitializeAsync(Start.AddMinutes(1), TestContext.Current.CancellationToken);

            Assert.True(restarted.IsSuccess);
            Assert.Equal("dark", restarted.Snapshot!.Settings.Application!.Theme);
            var recovery = SettingsJson.Load(await File.ReadAllTextAsync(
                PathFor(LocalStateStore.SettingsRecoveryFileName), TestContext.Current.CancellationToken));
            Assert.True(recovery.IsSuccess);
            Assert.Equal("dark", recovery.Settings!.Application!.Theme);
        }

        [Fact]
        public async Task InitializeAsync_LoadsValidSettings_WhenRecoveryRefreshFails()
        {
            var store = Store();
            var firstRun = await store.InitializeAsync(Start, TestContext.Current.CancellationToken);
            var recoveryPath = PathFor(LocalStateStore.SettingsRecoveryFileName);
            await using var locked = new FileStream(
                recoveryPath, FileMode.Open, FileAccess.Read, FileShare.Read);

            var restarted = await store.InitializeAsync(Start.AddMinutes(1), TestContext.Current.CancellationToken);

            Assert.True(firstRun.IsSuccess);
            Assert.True(restarted.IsSuccess, restarted.Error?.Message);
            Assert.Equal("current", restarted.Snapshot!.SettingsSource);
            var warning = Assert.Single(restarted.Snapshot.Recoveries);
            Assert.Equal(LocalStateStore.SettingsRecoveryFileName, warning.FileName);
            Assert.Contains("Could not refresh", warning.Message, StringComparison.Ordinal);
            Assert.Null(warning.ArchivedPath);
        }

        [Fact]
        public async Task SaveSettingsAsync_LeavesRecoveryUnchanged_WhenCurrentWriteFails()
        {
            var store = Store();
            var firstRun = await store.InitializeAsync(Start, TestContext.Current.CancellationToken);
            var recoveryBefore = await File.ReadAllTextAsync(
                PathFor(LocalStateStore.SettingsRecoveryFileName), TestContext.Current.CancellationToken);
            await using var locked = new FileStream(
                PathFor(LocalStateStore.SettingsFileName), FileMode.Open, FileAccess.Read, FileShare.Read);

            var result = await store.SaveSettingsAsync(firstRun.Snapshot!.Settings, TestContext.Current.CancellationToken);

            Assert.False(result.IsSuccess);
            Assert.Equal(recoveryBefore, await File.ReadAllTextAsync(
                PathFor(LocalStateStore.SettingsRecoveryFileName), TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task SaveSettingsAsync_SucceedsWithWarning_WhenRecoveryWriteFails()
        {
            var store = Store();
            var firstRun = await store.InitializeAsync(Start, TestContext.Current.CancellationToken);
            var candidateJson = JsonNode.Parse(SettingsJson.Serialize(firstRun.Snapshot!.Settings))!;
            candidateJson["application"]!["theme"] = "dark";
            var candidate = SettingsJson.Load(candidateJson.ToJsonString()).Settings!;
            var recoveryPath = PathFor(LocalStateStore.SettingsRecoveryFileName);
            var recoveryBefore = await File.ReadAllTextAsync(recoveryPath, TestContext.Current.CancellationToken);
            await using var locked = new FileStream(
                recoveryPath, FileMode.Open, FileAccess.Read, FileShare.Read);

            var result = await store.SaveSettingsAsync(candidate, TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Equal(LocalStateStore.SettingsRecoveryFileName, result.Warning?.FileName);
            Assert.Equal("dark", SettingsJson.Load(await File.ReadAllTextAsync(
                PathFor(LocalStateStore.SettingsFileName), TestContext.Current.CancellationToken)).Settings!.Application!.Theme);
            Assert.Equal(recoveryBefore, await File.ReadAllTextAsync(recoveryPath, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task InitializeAsync_PreservesUnsupportedRecoveryVersion()
        {
            var store = Store();
            Assert.True((await store.InitializeAsync(Start, TestContext.Current.CancellationToken)).IsSuccess);
            File.Delete(PathFor(LocalStateStore.SettingsFileName));
            const string unsupported = "{\"schemaVersion\":999}";
            await File.WriteAllTextAsync(
                PathFor(LocalStateStore.SettingsRecoveryFileName), unsupported, TestContext.Current.CancellationToken);

            var restarted = await store.InitializeAsync(Start.AddMinutes(1), TestContext.Current.CancellationToken);

            Assert.False(restarted.IsSuccess);
            Assert.Equal(unsupported, await File.ReadAllTextAsync(
                PathFor(LocalStateStore.SettingsRecoveryFileName), TestContext.Current.CancellationToken));
            Assert.Empty(Directory.GetFiles(_directory, "settings.last-known-good.json.corrupt-*"));
        }

        [Fact]
        public async Task InitializeAsync_PreservesUnsupportedSettingsVersion()
        {
            var store = Store();
            Assert.True((await store.InitializeAsync(Start, TestContext.Current.CancellationToken)).IsSuccess);
            const string unsupported = "{\"schemaVersion\":999}";
            await File.WriteAllTextAsync(
                PathFor(LocalStateStore.SettingsFileName), unsupported, TestContext.Current.CancellationToken);

            var restarted = await store.InitializeAsync(Start.AddMinutes(1), TestContext.Current.CancellationToken);

            Assert.False(restarted.IsSuccess);
            Assert.Equal(unsupported, await File.ReadAllTextAsync(
                PathFor(LocalStateStore.SettingsFileName), TestContext.Current.CancellationToken));
            Assert.Empty(Directory.GetFiles(_directory, "settings.json.corrupt-*"));
        }

        [Fact]
        public async Task SaveSettingsAsync_SerializesConcurrentWriters()
        {
            var store = Store();
            var secondStore = Store();
            var startup = await store.InitializeAsync(Start, TestContext.Current.CancellationToken);
            var settings = startup.Snapshot!.Settings;

            var saves = await Task.WhenAll(Enumerable.Range(0, 40).Select(index =>
                (index % 2 == 0 ? store : secondStore).SaveSettingsAsync(
                    WithTheme(settings, index % 2 == 0 ? "dark" : "light"),
                    TestContext.Current.CancellationToken)));

            Assert.All(saves, save => Assert.True(save.IsSuccess, save.Error?.Message));
            Assert.Equal(
                await File.ReadAllTextAsync(PathFor(LocalStateStore.SettingsFileName), TestContext.Current.CancellationToken),
                await File.ReadAllTextAsync(PathFor(LocalStateStore.SettingsRecoveryFileName), TestContext.Current.CancellationToken));
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
                Plugins = current.Plugins,
                Selection = current.Selection,
                Profiles = current.Profiles,
                ApplicationGroups = current.ApplicationGroups,
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
            Assert.Equal("{", await File.ReadAllTextAsync(recovery.ArchivedPath!, TestContext.Current.CancellationToken));
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
            Assert.Equal("{}", await File.ReadAllTextAsync(recovery.ArchivedPath!, TestContext.Current.CancellationToken));
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
                Plugins = current.Plugins,
                Selection = current.Selection,
                Profiles = current.Profiles,
                ApplicationGroups = current.ApplicationGroups,
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
        public async Task InitializeAsync_CleansStaleTemporaryFiles()
        {
            var store = Store();
            Assert.True((await store.InitializeAsync(Start, TestContext.Current.CancellationToken)).IsSuccess);
            var stale = Path.Combine(_directory, ".state.json.abandoned.tmp");
            await File.WriteAllTextAsync(stale, "partial", TestContext.Current.CancellationToken);
            File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddMinutes(-1));

            var restarted = await store.InitializeAsync(Start.AddMinutes(1), TestContext.Current.CancellationToken);

            Assert.True(restarted.IsSuccess, restarted.Error?.Message);
            Assert.False(File.Exists(stale));
        }

        [Fact]
        public async Task InitializeAsync_DoesNotDeleteFreshTemporaryFiles()
        {
            var store = Store();
            Assert.True((await store.InitializeAsync(Start, TestContext.Current.CancellationToken)).IsSuccess);
            var active = Path.Combine(_directory, ".state.json.active.tmp");
            await File.WriteAllTextAsync(active, "partial", TestContext.Current.CancellationToken);

            var restarted = await store.InitializeAsync(Start.AddMinutes(1), TestContext.Current.CancellationToken);

            Assert.True(restarted.IsSuccess, restarted.Error?.Message);
            Assert.True(File.Exists(active));
        }

        [Fact]
        public async Task InitializeAsync_RetriesBrieflyLockedSettingsFile()
        {
            var store = Store();
            Assert.True((await store.InitializeAsync(Start, TestContext.Current.CancellationToken)).IsSuccess);
            var path = PathFor(LocalStateStore.SettingsFileName);
            await using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

            var restart = store.InitializeAsync(Start.AddMinutes(1), TestContext.Current.CancellationToken);
            await Task.Delay(100, TestContext.Current.CancellationToken);
            await locked.DisposeAsync();
            var result = await restart;

            Assert.True(result.IsSuccess, result.Error?.Message);
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

        private static SettingsDocument WithTheme(SettingsDocument settings, string theme) => new()
        {
            SchemaVersion = settings.SchemaVersion,
            Application = new ApplicationSettings
            {
                CloseButtonBehavior = settings.Application!.CloseButtonBehavior,
                StartMinimized = settings.Application.StartMinimized,
                Theme = theme,
                VerboseLogging = settings.Application.VerboseLogging
            },
            Plugins = settings.Plugins,
            Selection = settings.Selection,
            Profiles = settings.Profiles,
            ApplicationGroups = settings.ApplicationGroups,
            Rules = settings.Rules,
            AdditionalProperties = settings.AdditionalProperties
        };
    }
}
