using FocusRgb.App.Diagnostics;
using FocusRgb.App.ViewModels;
using FocusRgb.Core.Commands;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Persistence;
using FocusRgb.TestSupport;

namespace FocusRgb.App.Tests
{
    public sealed class ApplicationSettingsViewModelTests : IDisposable
    {
        private static readonly DateTimeOffset Start = new(2026, 10, 14, 16, 0, 0, TimeSpan.Zero);
        private readonly TempDirectory _tempDirectory = new(nameof(ApplicationSettingsViewModelTests));
        private string _directory => _tempDirectory.Path;

        [Fact]
        public async Task SaveApplicationSettingsAsync_PersistsPreferencesWithoutChangingSelectionState()
        {
            var (viewModel, store) = await CreateAsync();
            var statePath = Path.Combine(_directory, LocalStateStore.StateFileName);
            var stateBefore = await File.ReadAllTextAsync(statePath, TestContext.Current.CancellationToken);
            var effectBefore = viewModel.Profiles.Single(profile => profile.Id == "default").EffectName;
            viewModel.CloseButtonBehavior = "Exit application";
            viewModel.StartMinimized = true;
            viewModel.ApplicationTheme = "Dark";
            viewModel.VerboseLogging = true;

            var saved = await viewModel.SaveApplicationSettingsAsync(TestContext.Current.CancellationToken);
            var restarted = await store.InitializeAsync(Start.AddMinutes(1), TestContext.Current.CancellationToken);

            Assert.True(saved);
            Assert.Equal("exit", restarted.Snapshot!.Settings.Application!.CloseButtonBehavior);
            Assert.True(restarted.Snapshot.Settings.Application.StartMinimized);
            Assert.Equal("dark", restarted.Snapshot.Settings.Application.Theme);
            Assert.True(restarted.Snapshot.Settings.Application.VerboseLogging);
            Assert.Equal(stateBefore, await File.ReadAllTextAsync(statePath, TestContext.Current.CancellationToken));
            Assert.Equal(effectBefore, restarted.Snapshot.Settings.Profiles!
                .Single(profile => profile.Id == "default").Bindings![0].Effect!.DisplayName);
        }

        [Fact]
        public async Task SaveApplicationSettingsAsync_AdoptsSavedValues_WhenRecoveryCopyFails()
        {
            var (viewModel, _) = await CreateAsync();
            var recoveryPath = Path.Combine(_directory, LocalStateStore.SettingsRecoveryFileName);
            await using var locked = new FileStream(
                recoveryPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            viewModel.ApplicationTheme = "Dark";

            var saved = await viewModel.SaveApplicationSettingsAsync(TestContext.Current.CancellationToken);

            Assert.True(saved);
            Assert.False(viewModel.IsApplicationSettingsDirty);
            Assert.Contains("Warning:", viewModel.ApplicationSettingsStatus, StringComparison.Ordinal);
            Assert.Equal("dark", SettingsJson.Load(await File.ReadAllTextAsync(
                Path.Combine(_directory, LocalStateStore.SettingsFileName),
                TestContext.Current.CancellationToken)).Settings!.Application!.Theme);
        }

        [Fact]
        public async Task CancelApplicationSettings_RestoresDraftAndCloseBehaviorUsesOnlySavedValue()
        {
            var (viewModel, _) = await CreateAsync();
            Assert.True(viewModel.ShouldMinimizeOnClose);

            viewModel.CloseButtonBehavior = "Exit application";
            viewModel.ApplicationTheme = "Dark";

            Assert.True(viewModel.ShouldMinimizeOnClose);
            Assert.True(viewModel.IsApplicationSettingsDirty);
            viewModel.CancelApplicationSettings();
            Assert.Equal("Minimize to system tray", viewModel.CloseButtonBehavior);
            Assert.Equal("System", viewModel.ApplicationTheme);
            Assert.False(viewModel.IsApplicationSettingsDirty);
        }

        [Fact]
        public async Task CreatePersistent_RemovesTrayOnlyBehavior_WhenTrayIsUnavailable()
        {
            var store = new LocalStateStore(_directory);
            var startup = await store.InitializeAsync(Start, TestContext.Current.CancellationToken);

            var viewModel = MainWindowViewModel.CreatePersistent(
                startup.Snapshot!,
                store,
                canMinimizeToTray: false);

            Assert.Equal(["Exit application"], viewModel.CloseButtonBehaviors);
            Assert.Equal("Exit application", viewModel.CloseButtonBehavior);
            Assert.False(viewModel.ShouldMinimizeOnClose);
        }

        [Fact]
        public async Task SetPluginEnabledAsync_PersistsOnlyRequestedPluginState()
        {
            var (viewModel, store) = await CreateAsync();

            Assert.Null(await viewModel.SetPluginEnabledAsync(
                "test-plugin", true, TestContext.Current.CancellationToken));
            Assert.Equal(["test-plugin"], viewModel.EnabledPluginIds);

            var enabled = await store.InitializeAsync(
                Start.AddMinutes(1), TestContext.Current.CancellationToken);
            Assert.Equal(["test-plugin"], enabled.Snapshot!.Settings.Plugins!.Enabled);

            Assert.Null(await viewModel.SetPluginEnabledAsync(
                "test-plugin", false, TestContext.Current.CancellationToken));
            Assert.Empty(viewModel.EnabledPluginIds);
        }

        [Fact]
        public async Task ExternalSettingsChange_RefreshesACleanEditor()
        {
            var (viewModel, service) = await CreateWithServiceAsync();

            var changed = WithTheme(service.Current.Settings, "dark");
            var applied = await service.ApplySettingsAsync(
                changed,
                service.Current.SettingsRevision,
                "mcp",
                TestContext.Current.CancellationToken);

            Assert.True(applied.IsSuccess);
            Assert.False(viewModel.HasSettingsConflict);
            Assert.Equal("Dark", viewModel.ApplicationTheme);
        }

        [Fact]
        public async Task ExternalProfileRemoval_PreservesDirtyProjectionUntilExplicitReload()
        {
            var (viewModel, service) = await CreateWithServiceAsync();
            var removed = viewModel.Profiles.Single(profile => profile.Id == "movie");
            viewModel.EditingProfile = removed;
            viewModel.ProfileName = "Unsaved draft";

            var applied = await service.ApplySettingsAsync(
                WithoutProfile(service.Current.Settings, "movie"),
                service.Current.SettingsRevision,
                "mcp",
                TestContext.Current.CancellationToken);

            Assert.True(applied.IsSuccess);
            Assert.True(viewModel.HasSettingsConflict);
            Assert.Equal("Unsaved draft", viewModel.ProfileName);
            Assert.Contains("Reload", viewModel.ProfileEditorStatus, StringComparison.Ordinal);
            viewModel.CancelProfile();
            viewModel.EditingProfile = viewModel.Profiles.Single(profile => profile.Id == "music");
            viewModel.EditingProfile = removed;
            Assert.Equal("Movie", viewModel.ProfileName);

            var pluginError = await viewModel.SetPluginEnabledAsync(
                "test-plugin", true, TestContext.Current.CancellationToken);
            Assert.Contains("Settings changed elsewhere", pluginError, StringComparison.Ordinal);

            var second = await service.ApplySettingsAsync(
                WithTheme(service.Current.Settings, "dark"),
                service.Current.SettingsRevision,
                "mcp",
                TestContext.Current.CancellationToken);
            Assert.True(second.IsSuccess);
            Assert.True(viewModel.HasSettingsConflict);
            Assert.Contains(viewModel.Profiles, profile => profile.Id == "movie");

            viewModel.ReloadSettingsAfterConflict();
            Assert.False(viewModel.HasSettingsConflict);
            Assert.DoesNotContain(viewModel.Profiles, profile => profile.Id == "movie");
            Assert.Equal("Dark", viewModel.ApplicationTheme);
        }

        [Fact]
        public async Task ExternalSettingsChange_PreservesNewGroupDraftWithoutASelection()
        {
            var (viewModel, service) = await CreateWithServiceAsync();
            viewModel.EditingApplicationGroup = null;
            viewModel.ApplicationGroupName = "New group";
            viewModel.AddDraftExecutable("NewApp.exe");

            var applied = await service.ApplySettingsAsync(
                WithTheme(service.Current.Settings, "dark"),
                service.Current.SettingsRevision,
                "mcp",
                TestContext.Current.CancellationToken);

            Assert.True(applied.IsSuccess);
            Assert.True(viewModel.HasSettingsConflict);
            Assert.Null(viewModel.EditingApplicationGroup);
            Assert.Equal("New group", viewModel.ApplicationGroupName);
            Assert.Equal(["NewApp.exe"], viewModel.DraftExecutables);
        }

        [Fact]
        public void DiagnosticExport_ExcludesProfileMappingAndPathContent()
        {
            var viewModel = MainWindowViewModel.CreatePreview();
            viewModel.ProfileName = "secret-profile-value";
            viewModel.ApplicationGroupName = @"C:\Private\secret-app.exe";

            var json = DiagnosticExport.CreateJson(viewModel, Start);

            Assert.DoesNotContain("secret-profile-value", json, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-app", json, StringComparison.Ordinal);
            Assert.DoesNotContain("profiles", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("mappings", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("settingsSchema", json, StringComparison.Ordinal);
        }

        [Fact]
        public void DiagnosticExport_UsesSavedPreferences_NotDrafts()
        {
            var viewModel = MainWindowViewModel.CreatePreview();
            viewModel.ApplicationTheme = "Dark";
            viewModel.CloseButtonBehavior = "Exit application";
            viewModel.StartMinimized = true;
            viewModel.VerboseLogging = true;

            var json = DiagnosticExport.CreateJson(viewModel, Start);

            Assert.Contains("\"theme\":\"system\"", json, StringComparison.Ordinal);
            Assert.Contains("\"closeBehavior\":\"minimize-to-tray\"", json, StringComparison.Ordinal);
            Assert.Contains("\"startMinimized\":false", json, StringComparison.Ordinal);
            Assert.Contains("\"verboseLogging\":false", json, StringComparison.Ordinal);
        }

        private async Task<(MainWindowViewModel ViewModel, LocalStateStore Store)> CreateAsync()
        {
            var store = new LocalStateStore(_directory);
            var startup = await store.InitializeAsync(Start, TestContext.Current.CancellationToken);
            Assert.True(startup.IsSuccess, startup.Error?.Message);
            return (MainWindowViewModel.CreatePersistent(startup.Snapshot!, store), store);
        }

        private async Task<(MainWindowViewModel ViewModel, ApplicationStateService Service)> CreateWithServiceAsync()
        {
            var store = new LocalStateStore(_directory);
            var startup = await store.InitializeAsync(Start, TestContext.Current.CancellationToken);
            Assert.True(startup.IsSuccess, startup.Error?.Message);
            var service = new ApplicationStateService(
                startup.Snapshot!.Settings,
                startup.Snapshot.State,
                startup.Snapshot.Desired.Reason,
                store);
            return (MainWindowViewModel.CreatePersistent(
                startup.Snapshot, store, applicationState: service), service);
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

        private static SettingsDocument WithoutProfile(SettingsDocument settings, string profileId) => new()
        {
            SchemaVersion = settings.SchemaVersion,
            Application = settings.Application,
            Plugins = settings.Plugins,
            Selection = settings.Selection,
            Profiles = settings.Profiles!.Where(profile => profile.Id != profileId).ToList(),
            ApplicationGroups = settings.ApplicationGroups,
            Rules = settings.Rules!.Where(rule => rule.Profile != profileId).ToList(),
            AdditionalProperties = settings.AdditionalProperties
        };

        public void Dispose()
        {
            _tempDirectory.Dispose();
        }
    }
}
