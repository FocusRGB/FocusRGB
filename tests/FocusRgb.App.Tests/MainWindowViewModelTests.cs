using FocusRgb.App.ViewModels;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Persistence;
using FocusRgb.Core.Selection;
using FocusRgb.TestSupport;

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
        public void AutomaticMode_StatesItIsWaitingForObservation()
        {
            var viewModel = MainWindowViewModel.CreatePreview();

            viewModel.IsAutomatic = true;

            Assert.False(viewModel.IsManual);
            Assert.Contains("observation", viewModel.SelectionReason, StringComparison.OrdinalIgnoreCase);
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
        public async Task ApplicationGroupEditor_SavesDraftExecutablesAndProtectsReferencedGroup()
        {
            var viewModel = MainWindowViewModel.CreatePreview();
            viewModel.EditingApplicationGroup = viewModel.ApplicationGroups[0];
            viewModel.ApplicationGroupName = "Editors";
            viewModel.AddDraftExecutable(@"C:\Tools\Rider.exe");
            await viewModel.SaveApplicationGroupAsync(TestContext.Current.CancellationToken);

            Assert.Equal("Editors", viewModel.ApplicationGroups[0].DisplayName);
            Assert.Contains("Rider.exe", viewModel.ApplicationGroups[0].Executables);

            await viewModel.RemoveApplicationGroupAsync(TestContext.Current.CancellationToken);
            Assert.Contains("first", viewModel.ApplicationGroupEditorStatus, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task RemoveApplicationGroup_RejectsReferenceRemovedOnlyFromMappingDraft()
        {
            var viewModel = MainWindowViewModel.CreatePreview();
            var group = viewModel.ApplicationGroups[0];
            var mapping = viewModel.MappingEditor.Mappings.Single(item => item.SelectedApplicationGroup.Id == group.Id);
            viewModel.MappingEditor.Remove(mapping);
            viewModel.EditingApplicationGroup = group;

            await viewModel.RemoveApplicationGroupAsync(TestContext.Current.CancellationToken);
            viewModel.MappingEditor.Cancel();

            Assert.Contains(group, viewModel.ApplicationGroups);
            Assert.Contains(viewModel.MappingEditor.Mappings, item => item.SelectedApplicationGroup.Id == group.Id);
        }

        [Fact]
        public async Task ApplicationGroupEditor_AcceptsDetectedAndManualExecutables()
        {
            var viewModel = MainWindowViewModel.CreatePreview();
            viewModel.EditingApplicationGroup = viewModel.ApplicationGroups[0];
            await viewModel.ObserveApplicationsAsync(new ApplicationObservation
            {
                RunningExecutables = ["Rider.exe", "Code.exe", "rider.EXE"]
            }, TestContext.Current.CancellationToken);
            viewModel.SelectedDetectedExecutable = "Rider.exe";
            await viewModel.ObserveApplicationsAsync(new ApplicationObservation
            {
                RunningExecutables = ["Code.exe", "Rider.exe", "Spotify.exe"]
            }, TestContext.Current.CancellationToken);
            Assert.Equal("Rider.exe", viewModel.SelectedDetectedExecutable);
            viewModel.AddDetectedExecutable();
            viewModel.ManualExecutable = "tool.exe";
            viewModel.AddManualExecutable();

            Assert.Equal(["Code.exe", "Rider.exe", "Spotify.exe"], viewModel.DetectedExecutables);
            Assert.Contains("Rider.exe", viewModel.DraftExecutables);
            Assert.Contains("tool.exe", viewModel.DraftExecutables);
            Assert.False(viewModel.AddDraftExecutable("not-an-executable"));
        }

        [Fact]
        public async Task SavedApplicationGroupAndMappingsSurviveRestart()
        {
            var tempDirectory = new TempDirectory(nameof(MainWindowViewModelTests));
            var directory = tempDirectory.Path;
            try
            {
                var store = new LocalStateStore(directory);
                var startup = await store.InitializeAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
                Assert.True(startup.IsSuccess);
                var customJson = System.Text.Json.Nodes.JsonNode.Parse(
                    SettingsJson.Serialize(startup.Snapshot!.Settings))!;
                customJson["applicationGroups"]![0]!["futureGroup"] = 42;
                customJson["rules"]![0]!["futureRule"] = true;
                customJson["rules"]![0]!["enabled"] = false;
                var custom = SettingsJson.Load(customJson.ToJsonString()).Settings!;
                Assert.True((await store.SaveSettingsAsync(custom, TestContext.Current.CancellationToken)).IsSuccess);
                startup = await new LocalStateStore(directory).InitializeAsync(
                    DateTimeOffset.UtcNow.AddMilliseconds(1), TestContext.Current.CancellationToken);
                var viewModel = MainWindowViewModel.CreatePersistent(startup.Snapshot!, store);
                viewModel.EditingApplicationGroup = viewModel.ApplicationGroups[0];
                viewModel.ApplicationGroupName = "Development tools";
                viewModel.AddDraftExecutable("Rider.exe");
                await viewModel.SaveApplicationGroupAsync(TestContext.Current.CancellationToken);
                viewModel.MappingEditor.Mappings[0].Eligibility = "while-running";
                await viewModel.SaveMappingsAsync(TestContext.Current.CancellationToken);

                var restarted = await new LocalStateStore(directory).InitializeAsync(
                    DateTimeOffset.UtcNow.AddSeconds(1), TestContext.Current.CancellationToken);

                Assert.True(restarted.IsSuccess);
                var group = restarted.Snapshot!.Settings.ApplicationGroups!.Single(item => item.Id == "programming");
                Assert.Equal("Development tools", group.DisplayName);
                Assert.Contains("Rider.exe", group.Executables!);
                Assert.Equal("while-running", restarted.Snapshot.Settings.Rules![0].Eligibility);
                Assert.False(restarted.Snapshot.Settings.Rules[0].Enabled);
                Assert.Equal(42, group.AdditionalProperties!["futureGroup"].GetInt32());
                Assert.True(restarted.Snapshot.Settings.Rules[0].AdditionalProperties!["futureRule"].GetBoolean());

                Assert.Equal(TimeSpan.FromSeconds(5), viewModel.ApplicationObservationInterval);
                await viewModel.SetModeAsync(manual: false, TestContext.Current.CancellationToken);
                Assert.Equal(TimeSpan.FromSeconds(1), viewModel.ApplicationObservationInterval);
                var observation = new ApplicationObservation { RunningExecutables = ["Code.exe"] };
                await viewModel.ObserveApplicationsAsync(observation, TestContext.Current.CancellationToken);
                viewModel.ReportUnexpectedError("Keep this status");
                await viewModel.ObserveApplicationsAsync(observation, TestContext.Current.CancellationToken);
                Assert.Equal("Keep this status", viewModel.RuntimeStatusMessage);
            }
            finally
            {
                tempDirectory.Dispose();
            }
        }
    }
}
