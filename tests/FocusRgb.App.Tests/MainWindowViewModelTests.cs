using FocusRgb.App.ViewModels;
using FocusRgb.Core.Configuration;

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

        [Fact]
        public void CreatePreview_RejectsProfileWithoutItsEffectAndLayoutBinding()
        {
            var settings = SettingsJson.LoadDefaults().Settings!;
            settings.Profiles!.Single(profile => profile.Id == "programming").Bindings!.Clear();

            Assert.Throws<ArgumentException>(() => MainWindowViewModel.CreatePreview(settings));
        }

        [Fact]
        public async Task ProfileEditor_SaveAndSaveAsFollowGlobalItemActions()
        {
            var viewModel = MainWindowViewModel.CreatePreview();
            viewModel.EditingProfile = viewModel.Profiles.Single(profile => profile.Id == "movie");
            viewModel.ProfileName = "Cinema";
            await viewModel.SaveProfileAsync(TestContext.Current.CancellationToken);

            Assert.Equal("Cinema", viewModel.EditingProfile.DisplayName);

            viewModel.ProfileName = "Cinema Copy";
            await viewModel.SaveProfileAsAsync(TestContext.Current.CancellationToken);

            Assert.Equal(5, viewModel.Profiles.Count);
            Assert.Equal("Cinema Copy", viewModel.EditingProfile.DisplayName);
        }

        [Fact]
        public void ApplicationGroupEditor_SavesDraftExecutablesAndProtectsReferencedGroup()
        {
            var viewModel = MainWindowViewModel.CreatePreview();
            viewModel.EditingApplicationGroup = viewModel.ApplicationGroups[0];
            viewModel.ApplicationGroupName = "Editors";
            viewModel.AddDraftExecutable(@"C:\Tools\Rider.exe");
            viewModel.SaveApplicationGroup();

            Assert.Equal("Editors", viewModel.ApplicationGroups[0].DisplayName);
            Assert.Contains("Rider.exe", viewModel.ApplicationGroups[0].Executables);

            viewModel.RemoveApplicationGroup();
            Assert.Contains("first", viewModel.ApplicationGroupEditorStatus, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void RemoveApplicationGroup_RejectsReferenceRemovedOnlyFromMappingDraft()
        {
            var viewModel = MainWindowViewModel.CreatePreview();
            var group = viewModel.ApplicationGroups[0];
            var mapping = viewModel.MappingEditor.Mappings.Single(item => item.SelectedApplicationGroup.Id == group.Id);
            viewModel.MappingEditor.Remove(mapping);
            viewModel.EditingApplicationGroup = group;

            viewModel.RemoveApplicationGroup();
            viewModel.MappingEditor.Cancel();

            Assert.Contains(group, viewModel.ApplicationGroups);
            Assert.Contains(viewModel.MappingEditor.Mappings, item => item.SelectedApplicationGroup.Id == group.Id);
        }
    }
}
