using FocusRgb.App.Diagnostics;
using FocusRgb.App.ViewModels;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Persistence;

namespace FocusRgb.App.Tests
{
    public sealed class ApplicationSettingsViewModelTests : IDisposable
    {
        private static readonly DateTimeOffset Start = new(2026, 10, 14, 16, 0, 0, TimeSpan.Zero);
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(), "FocusRgb.App.Tests", Guid.NewGuid().ToString("N"));

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

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
