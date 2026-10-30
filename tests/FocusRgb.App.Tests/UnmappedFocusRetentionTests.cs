using System.Text.Json.Nodes;
using FocusRgb.App.ViewModels;
using FocusRgb.Core.Commands;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Persistence;
using FocusRgb.Core.Selection;
using FocusRgb.TestSupport;

namespace FocusRgb.App.Tests
{
    public sealed class UnmappedFocusRetentionTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task UnmappedFocus_DoesNotRewriteIntentOrAnnounceAndFallsBackAfterExit(bool savePreferences)
        {
            using var directory = new TempDirectory(nameof(UnmappedFocusRetentionTests));
            var store = new LocalStateStore(directory.Path);
            var startup = (await store.InitializeAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken)).Snapshot!;
            var json = JsonNode.Parse(SettingsJson.Serialize(startup.Settings))!;
            json["application"]!["switchingSpeed"] = "fast";
            json["rules"]![2]!["eligibility"] = "while-running";
            var settings = SettingsJson.Load(json.ToJsonString()).Settings!;
            Assert.True((await store.SaveSettingsAsync(settings, TestContext.Current.CancellationToken)).IsSuccess);
            var state = new ApplicationStateService(settings, startup.State, startup.Desired.Reason, store);
            var model = MainWindowViewModel.CreatePersistent(startup, store, applicationState: state);
            await model.SetModeAsync(false, TestContext.Current.CancellationToken);
            await model.ObserveApplicationsAsync(Observation("Code.exe", "Code.exe", "Spotify.exe"), TestContext.Current.CancellationToken);
            await Task.Delay(350, TestContext.Current.CancellationToken);
            await model.ObserveApplicationsAsync(Observation("Code.exe", "Code.exe", "Spotify.exe"), TestContext.Current.CancellationToken);
            Assert.Equal("programming", model.SelectedProfile.Id);

            var popup = new ProfileChangePopupDecision();
            popup.Update(state.Current);
            var announcements = new List<string>();
            state.SnapshotChanged += (_, snapshot) =>
            {
                if (popup.Update(snapshot) is { } message) announcements.Add(message);
            };
            if (savePreferences)
            {
                model.ApplicationTheme = "Dark";
                Assert.True(await model.SaveApplicationSettingsAsync(TestContext.Current.CancellationToken));
            }
            var before = await File.ReadAllBytesAsync(Path.Combine(directory.Path, LocalStateStore.StateFileName), TestContext.Current.CancellationToken);
            foreach (var foreground in new[] { "chrome.exe", "chrome.exe", "Code.exe", "chrome.exe" })
            {
                await model.ObserveApplicationsAsync(Observation(foreground, "Code.exe", "Spotify.exe"), TestContext.Current.CancellationToken);
                Assert.Equal("programming", model.SelectedProfile.Id);
            }
            Assert.Empty(announcements);
            Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(directory.Path, LocalStateStore.StateFileName), TestContext.Current.CancellationToken));
            await model.ObserveApplicationsAsync(Observation("chrome.exe", "Spotify.exe"), TestContext.Current.CancellationToken);
            Assert.Equal("music", model.SelectedProfile.Id);
            await model.ObserveApplicationsAsync(Observation("chrome.exe"), TestContext.Current.CancellationToken);
            Assert.Equal("default", model.SelectedProfile.Id);
            Assert.Equal(2, announcements.Count);
        }

        private static ApplicationObservation Observation(string foreground, params string[] running) => new()
        {
            ForegroundExecutable = foreground, RunningExecutables = running
        };
    }
}
