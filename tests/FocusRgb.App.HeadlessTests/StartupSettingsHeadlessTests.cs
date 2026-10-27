using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using FocusRgb.App.ViewModels;
using FocusRgb.Core.Configuration;
using FocusRgb.Platform.Windows;

namespace FocusRgb.App.HeadlessTests
{
    public sealed class StartupSettingsHeadlessTests
    {
        [AvaloniaFact]
        public void StartWithWindows_UsesRegistryServiceImmediatelyAndSurvivesPreferenceCancel()
        {
            var registration = new FakeStartupRegistration();
            var viewModel = MainWindowViewModel.CreatePreview();
            var window = new MainWindow(viewModel, null, registration);
            var settings = window.GetLogicalDescendants().OfType<TabItem>().Single(tab => Equals(tab.Header, "Settings"));
            settings.IsSelected = true;
            window.Show();
            try
            {
                var toggle = window.FindControl<CheckBox>("StartWithWindowsCheckBox")!;
                Assert.True(toggle.IsEnabled);
                Assert.False(toggle.IsChecked);
                toggle.IsChecked = true;

                Assert.True(registration.Enabled);
                Assert.Equal(1, registration.Changes);
                Assert.False(viewModel.SavedStartMinimized);
                Assert.Equal(6, SettingsJson.CurrentSchemaVersion);
                settings.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Cancel"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(registration.Enabled);
                Assert.True(toggle.IsChecked);
                toggle.IsChecked = false;
                Assert.False(registration.Enabled);
                Assert.Equal(2, registration.Changes);
            }
            finally
            {
                window.AllowClose();
                window.Close();
            }
        }

        [AvaloniaFact]
        public void StartWithWindows_RestoresActualStateAndShowsPermissionFailure()
        {
            var registration = new FakeStartupRegistration(deny: true);
            var window = new MainWindow(MainWindowViewModel.CreatePreview(), null, registration);
            window.GetLogicalDescendants().OfType<TabItem>().Single(tab => Equals(tab.Header, "Settings")).IsSelected = true;
            window.Show();
            try
            {
                var toggle = window.FindControl<CheckBox>("StartWithWindowsCheckBox")!;
                toggle.IsChecked = true;
                Assert.False(toggle.IsChecked);
                Assert.False(toggle.IsEnabled);
                Assert.Contains("denied", window.FindControl<TextBlock>("StartupStatusText")!.Text!, StringComparison.Ordinal);
            }
            finally
            {
                window.AllowClose();
                window.Close();
            }
        }

        private sealed class FakeStartupRegistration(bool deny = false) : IStartupRegistration
        {
            public bool Enabled { get; private set; }
            public int Changes { get; private set; }
            public StartupRegistrationStatus Refresh() => new(Enabled, true, "Current registry state.");
            public StartupRegistrationStatus SetEnabled(bool enabled)
            {
                if (deny)
                {
                    return new(Enabled, false, "Update denied.");
                }
                Enabled = enabled;
                Changes++;
                return Refresh();
            }
        }
    }
}
