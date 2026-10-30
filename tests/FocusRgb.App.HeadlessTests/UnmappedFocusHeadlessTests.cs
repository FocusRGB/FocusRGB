using System.Text.Json.Nodes;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FocusRgb.App.ViewModels;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Persistence;
using FocusRgb.Core.Selection;
using FocusRgb.TestSupport;

namespace FocusRgb.App.HeadlessTests
{
    public sealed class UnmappedFocusHeadlessTests
    {
        [AvaloniaFact]
        public async Task UnmappedApp_KeepsProfilePickerUntilProcessExit()
        {
            using var directory = new TempDirectory(nameof(UnmappedFocusHeadlessTests));
            var store = new LocalStateStore(directory.Path);
            var initial = await store.InitializeAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
            var json = JsonNode.Parse(SettingsJson.Serialize(initial.Snapshot!.Settings))!;
            json["application"]!["switchingSpeed"] = "fast";
            await store.SaveSettingsAsync(SettingsJson.Load(json.ToJsonString()).Settings!, TestContext.Current.CancellationToken);
            var loaded = await store.InitializeAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
            var model = MainWindowViewModel.CreatePersistent(loaded.Snapshot!, store);
            var window = new MainWindow(model);
            window.Show();
            try
            {
                await model.SetModeAsync(false, TestContext.Current.CancellationToken);
                var code = new ApplicationObservation { ForegroundExecutable = "Code.exe", RunningExecutables = ["Code.exe"] };
                await model.ObserveApplicationsAsync(code, TestContext.Current.CancellationToken);
                await Task.Delay(350, TestContext.Current.CancellationToken);
                await model.ObserveApplicationsAsync(code, TestContext.Current.CancellationToken);
                var picker = window.GetLogicalDescendants().OfType<ComboBox>().First(box => AutomationProperties.GetName(box) == "Selected lighting profile");
                Assert.Same(model.Profiles.Single(profile => profile.Id == "programming"), picker.SelectedItem);
                await model.ObserveApplicationsAsync(new ApplicationObservation { ForegroundExecutable = "chrome.exe", RunningExecutables = ["Code.exe"] }, TestContext.Current.CancellationToken);
                await Task.Delay(350, TestContext.Current.CancellationToken);
                await model.ObserveApplicationsAsync(new ApplicationObservation { ForegroundExecutable = "chrome.exe", RunningExecutables = ["Code.exe"] }, TestContext.Current.CancellationToken);
                Assert.Same(model.Profiles.Single(profile => profile.Id == "programming"), picker.SelectedItem);
                await model.ObserveApplicationsAsync(new ApplicationObservation { ForegroundExecutable = "chrome.exe" }, TestContext.Current.CancellationToken);
                Assert.Same(model.Profiles.Single(profile => profile.Id == "default"), picker.SelectedItem);
            }
            finally { window.AllowClose(); window.Close(); }
        }
    }
}
