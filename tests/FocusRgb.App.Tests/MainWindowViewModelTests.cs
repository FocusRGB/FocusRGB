using FocusRgb.App.ViewModels;

namespace FocusRgb.App.Tests
{
    public sealed class MainWindowViewModelTests
    {
        [Fact]
        public void CreatePreview_StartsManualWithDefaultProfile()
        {
            var viewModel = MainWindowViewModel.CreatePreview();

            Assert.True(viewModel.IsManual);
            Assert.False(viewModel.IsAutomatic);
            Assert.Equal("Default", viewModel.DesiredProfileName);
            Assert.Contains("preview", viewModel.AppliedProfileName, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CreatePreview_ListsValidatedFixtureProfiles()
        {
            var viewModel = MainWindowViewModel.CreatePreview();

            Assert.Equal(["Default", "Programming", "Movie", "Music"],
                viewModel.Profiles.Select(profile => profile.DisplayName));
        }

        [Fact]
        public void SelectedProfile_UpdatesDisplayedPreviewState()
        {
            var viewModel = MainWindowViewModel.CreatePreview();

            viewModel.SelectedProfile = viewModel.Profiles.Single(profile => profile.Id == "movie");

            Assert.Equal("Movie", viewModel.DesiredProfileName);
            Assert.Equal("Movie (preview)", viewModel.AppliedProfileName);
        }

        [Fact]
        public void AutomaticMode_StatesDetectionIsNotConnected()
        {
            var viewModel = MainWindowViewModel.CreatePreview();

            viewModel.IsAutomatic = true;

            Assert.False(viewModel.IsManual);
            Assert.Contains("not connected", viewModel.SelectionReason, StringComparison.OrdinalIgnoreCase);
        }
    }
}
