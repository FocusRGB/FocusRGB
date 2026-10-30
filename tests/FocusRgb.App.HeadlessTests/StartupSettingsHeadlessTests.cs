using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
                var minimized = settings.GetLogicalDescendants().OfType<CheckBox>()
                    .Single(box => AutomationProperties.GetName(box) == "Start minimized");
                Assert.True(toggle.IsEnabled);
                Assert.False(toggle.IsChecked);
                toggle.IsChecked = true;

                Assert.True(registration.Enabled);
                Assert.Equal(1, registration.Changes);
                Assert.False(viewModel.SavedStartMinimized);
                Assert.Equal(8, SettingsJson.CurrentSchemaVersion);
                minimized.IsChecked = true;
                Assert.True(viewModel.StartMinimized);
                Assert.False(viewModel.SavedStartMinimized);
                Assert.Equal(1, registration.Changes);
                settings.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Cancel"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(registration.Enabled);
                Assert.True(toggle.IsChecked);
                Assert.False(minimized.IsChecked);
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

        [AvaloniaFact]
        public void StartupOptions_ShowAccessibleTogglesInVisualAndKeyboardOrder()
        {
            var window = new MainWindow(MainWindowViewModel.CreatePreview(), null, new FakeStartupRegistration());
            var settings = window.GetLogicalDescendants().OfType<TabItem>().Single(tab => Equals(tab.Header, "Settings"));
            settings.IsSelected = true;
            window.Show();
            try
            {
                window.UpdateLayout();
                var startup = window.FindControl<CheckBox>("StartWithWindowsCheckBox")!;
                var minimized = settings.GetLogicalDescendants().OfType<CheckBox>()
                    .Single(box => AutomationProperties.GetName(box) == "Start minimized");
                Assert.Equal("Run Focus when system starts", AutomationProperties.GetName(startup));
                Assert.Null(startup.Content);
                Assert.Null(minimized.Content);
                Assert.True(startup.IsEffectivelyVisible);
                Assert.True(minimized.IsEffectivelyVisible);
                var grid = Assert.IsType<Grid>(startup.Parent);
                var startupLabel = grid.Children.OfType<TextBlock>().Single(text => text.Text == "Run Focus when system starts");
                var minimizedLabel = grid.Children.OfType<TextBlock>().Single(text => text.Text == "Start minimized");
                Assert.Equal(1, Grid.GetRow(startup));
                Assert.Equal(2, Grid.GetRow(minimized));
                Assert.Equal(Grid.GetRow(startup), Grid.GetRow(startupLabel));
                Assert.Equal(Grid.GetRow(minimized), Grid.GetRow(minimizedLabel));
                Assert.True(startupLabel.Bounds.Right <= startup.Bounds.Left);
                Assert.True(minimizedLabel.Bounds.Right <= minimized.Bounds.Left);
                Assert.True(startup.Bounds.Bottom <= minimized.Bounds.Top);
                Assert.Contains(settings.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text ==
                    "Run Focus when system starts takes effect immediately. Start minimized controls ordinary launches.");
                var closeBehavior = grid.Children.OfType<ComboBox>().Single();
                closeBehavior.Focus();
                window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                Assert.True(startup.IsFocused);
                window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                Assert.True(minimized.IsFocused);
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
