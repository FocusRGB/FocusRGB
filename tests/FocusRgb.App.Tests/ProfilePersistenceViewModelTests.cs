using FocusRgb.App.ViewModels;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Persistence;

namespace FocusRgb.App.Tests
{
    public sealed class ProfilePersistenceViewModelTests : IDisposable
    {
        private static readonly DateTimeOffset Start = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(), "FocusRgb.App.Tests", Guid.NewGuid().ToString("N"));

        [Fact]
        public async Task SaveProfileAsync_RestoresEditedProfileAfterRestart()
        {
            var viewModel = await CreateAsync();
            viewModel.EditingProfile = viewModel.Profiles.Single(profile => profile.Id == "movie");
            viewModel.ProfileName = "Cinema";
            viewModel.ProfileEffect = "Cinema effect";
            viewModel.ProfileLayout = "Monitor layout";

            await viewModel.SaveProfileAsync(TestContext.Current.CancellationToken);

            var restarted = await Store().InitializeAsync(Start.AddMinutes(1), TestContext.Current.CancellationToken);
            var saved = restarted.Snapshot!.Settings.Profiles!.Single(profile => profile.Id == "movie");
            Assert.Equal("Cinema", saved.DisplayName);
            Assert.Equal("Cinema effect", saved.Bindings![0].Effect!.DisplayName);
            Assert.Equal("Monitor layout", saved.Bindings[0].Layout!.DisplayName);
        }

        [Fact]
        public async Task SaveProfileAsAsync_CreatesPersistentProfileWithStableUniqueId()
        {
            var viewModel = await CreateAsync();
            viewModel.EditingProfile = viewModel.Profiles.Single(profile => profile.Id == "movie");
            viewModel.ProfileName = "Movie";

            await viewModel.SaveProfileAsAsync(TestContext.Current.CancellationToken);

            var restarted = await Store().InitializeAsync(Start.AddMinutes(1), TestContext.Current.CancellationToken);
            Assert.Contains(restarted.Snapshot!.Settings.Profiles!, profile => profile.Id == "movie-2");
        }

        [Fact]
        public async Task CancelProfile_RestoresDraftWithoutSavingOrPreviewingWhenDisabled()
        {
            var viewModel = await CreateAsync();
            viewModel.EditingProfile = viewModel.Profiles.Single(profile => profile.Id == "music");
            var original = viewModel.ProfileEffect;

            viewModel.ProfileEffect = "Unsaved effect";
            Assert.True(viewModel.IsProfileDirty);
            Assert.Equal(0, viewModel.ProfilePreviewRevision);

            viewModel.CancelProfile();

            Assert.Equal(original, viewModel.ProfileEffect);
            Assert.False(viewModel.IsProfileDirty);
        }

        [Fact]
        public async Task LivePreview_UpdatesOnlyPreviewRevisionUntilSave()
        {
            var viewModel = await CreateAsync();
            viewModel.EditingProfile = viewModel.Profiles.Single(profile => profile.Id == "music");
            viewModel.ProfileLivePreview = true;

            viewModel.ProfileEffect = "Preview effect";

            Assert.True(viewModel.IsProfileDirty);
            Assert.Equal(1, viewModel.ProfilePreviewRevision);
            var restarted = await Store().InitializeAsync(Start.AddMinutes(1), TestContext.Current.CancellationToken);
            Assert.Equal("Logarithmic Visualizer",
                restarted.Snapshot!.Settings.Profiles!.Single(profile => profile.Id == "music")
                    .Bindings![0].Effect!.DisplayName);
        }

        [Fact]
        public async Task SaveProfileAsync_RejectsFallbackCycleWithConfigurationPath()
        {
            var viewModel = await CreateAsync();
            viewModel.EditingProfile = viewModel.Profiles.Single(profile => profile.Id == "programming");
            viewModel.ProfileFallbackKind = "profile";
            viewModel.ProfileFallbackProfile = viewModel.Profiles.Single(profile => profile.Id == "movie");
            await viewModel.SaveProfileAsync(TestContext.Current.CancellationToken);

            viewModel.EditingProfile = viewModel.Profiles.Single(profile => profile.Id == "movie");
            viewModel.ProfileFallbackKind = "profile";
            viewModel.ProfileFallbackProfile = viewModel.Profiles.Single(profile => profile.Id == "programming");
            await viewModel.SaveProfileAsync(TestContext.Current.CancellationToken);

            Assert.Contains("profiles[", viewModel.ProfileEditorStatus, StringComparison.Ordinal);
            Assert.Contains("fallback", viewModel.ProfileEditorStatus, StringComparison.Ordinal);
        }

        [Fact]
        public async Task RemoveProfileAsync_RejectsMappedAndFallbackReferencedProfiles()
        {
            var viewModel = await CreateAsync();
            viewModel.EditingProfile = viewModel.Profiles.Single(profile => profile.Id == "programming");
            await viewModel.RemoveProfileAsync(TestContext.Current.CancellationToken);
            Assert.StartsWith("rules:", viewModel.ProfileEditorStatus, StringComparison.Ordinal);

            viewModel.EditingProfile = viewModel.Profiles.Single(profile => profile.Id == "default");
            await viewModel.RemoveProfileAsync(TestContext.Current.CancellationToken);
            Assert.Contains("protected", viewModel.ProfileEditorStatus, StringComparison.OrdinalIgnoreCase);

            viewModel.EditingProfile = viewModel.Profiles.Single(profile => profile.Id == "movie");
            viewModel.ProfileName = "Fallback only";
            await viewModel.SaveProfileAsAsync(TestContext.Current.CancellationToken);
            var fallbackOnly = viewModel.EditingProfile!;

            viewModel.EditingProfile = viewModel.Profiles.Single(profile => profile.Id == "default");
            viewModel.ProfileFallbackKind = "profile";
            viewModel.ProfileFallbackProfile = fallbackOnly;
            await viewModel.SaveProfileAsync(TestContext.Current.CancellationToken);

            viewModel.EditingProfile = fallbackOnly;
            await viewModel.RemoveProfileAsync(TestContext.Current.CancellationToken);
            Assert.Contains("fallback", viewModel.ProfileEditorStatus, StringComparison.Ordinal);
        }

        [Fact]
        public async Task RemoveProfileAsync_ReassignsPersistedSelectionToDefault()
        {
            var viewModel = await CreateAsync();
            viewModel.EditingProfile = viewModel.Profiles.Single(profile => profile.Id == "movie");
            viewModel.ProfileName = "Temporary";
            await viewModel.SaveProfileAsAsync(TestContext.Current.CancellationToken);
            var temporaryId = viewModel.EditingProfile!.Id;

            var store = Store();
            var snapshot = await store.InitializeAsync(Start.AddMinutes(1), TestContext.Current.CancellationToken);
            var stateSave = await store.SaveStateAsync(new SelectionStateDocument
            {
                SchemaVersion = 1,
                Mode = "manual",
                ManualProfile = temporaryId,
                LastSelectedProfile = temporaryId,
                UpdatedAt = Start.AddMinutes(1)
            }, snapshot.Snapshot!.Settings, TestContext.Current.CancellationToken);
            Assert.True(stateSave.IsSuccess, stateSave.Error?.Message);

            var restored = await store.InitializeAsync(Start.AddMinutes(2), TestContext.Current.CancellationToken);
            viewModel = MainWindowViewModel.CreatePersistent(restored.Snapshot!, store);
            viewModel.EditingProfile = viewModel.Profiles.Single(profile => profile.Id == temporaryId);
            await viewModel.RemoveProfileAsync(TestContext.Current.CancellationToken);

            var restarted = await store.InitializeAsync(Start.AddMinutes(3), TestContext.Current.CancellationToken);
            Assert.DoesNotContain(restarted.Snapshot!.Settings.Profiles!, profile => profile.Id == temporaryId);
            Assert.Equal("default", restarted.Snapshot.State.ManualProfile);
            Assert.Equal("default", restarted.Snapshot.State.LastSelectedProfile);
        }

        [Fact]
        public async Task SelectProfileAsync_RestoresLastManualProfileAfterRestart()
        {
            var viewModel = await CreateAsync();
            var movie = viewModel.Profiles.Single(profile => profile.Id == "movie");

            await viewModel.SelectProfileAsync(movie, TestContext.Current.CancellationToken);

            var restarted = await Store().InitializeAsync(Start.AddMinutes(1), TestContext.Current.CancellationToken);
            var restored = MainWindowViewModel.CreatePersistent(restarted.Snapshot!, Store());
            Assert.True(restored.IsManual);
            Assert.Equal("movie", restored.SelectedProfile.Id);
            Assert.Contains("restored", restored.SelectionReason, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("not applied", restored.AppliedProfileName, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task SelectProfileAsync_ReportsStorageFailureAndRemainsResponsive()
        {
            var viewModel = await CreateAsync();
            var statePath = Path.Combine(_directory, LocalStateStore.StateFileName);
            await using var lockedState = new FileStream(
                statePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None,
                bufferSize: 1,
                useAsync: true);

            await viewModel.SelectProfileAsync(
                viewModel.Profiles.Single(profile => profile.Id == "movie"),
                TestContext.Current.CancellationToken);

            Assert.Equal("default", viewModel.SelectedProfile.Id);
            Assert.False(viewModel.IsRuntimeCommandPending);
            Assert.Contains(LocalStateStore.StateFileName, viewModel.RuntimeStatusMessage, StringComparison.Ordinal);
        }

        private async Task<MainWindowViewModel> CreateAsync()
        {
            var store = Store();
            var startup = await store.InitializeAsync(Start, TestContext.Current.CancellationToken);
            Assert.True(startup.IsSuccess, startup.Error?.Message);
            return MainWindowViewModel.CreatePersistent(startup.Snapshot!, store);
        }

        private LocalStateStore Store() => new(_directory);

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
