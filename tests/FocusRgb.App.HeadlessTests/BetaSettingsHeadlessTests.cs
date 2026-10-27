using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using FocusRgb.App.ViewModels;

namespace FocusRgb.App.HeadlessTests
{
    public sealed class BetaSettingsHeadlessTests
    {
        [AvaloniaFact]
        public void Settings_ShowsBetaPreferenceAndVersionInAboutDialog()
        {
            var viewModel = MainWindowViewModel.CreatePreview();
            var window = new MainWindow(viewModel);
            window.GetLogicalDescendants().OfType<TabItem>().Single(tab => Equals(tab.Header, "Settings")).IsSelected = true;
            window.Show();
            try
            {
                var toggle = window.FindControl<CheckBox>("IncludeBetaPluginsCheckBox")!;
                Assert.True(toggle.IsChecked);
                toggle.IsChecked = false;
                Assert.False(viewModel.IncludeBetaPlugins);
                Assert.True(viewModel.SavedIncludeBetaPlugins);
                viewModel.CancelApplicationSettings();
                Assert.True(toggle.IsChecked);

                window.FindControl<Button>("AboutButton")!
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var about = Assert.Single(window.OwnedWindows);
                Assert.Equal("About FocusRGB", about.Title);
                Assert.Contains(about.GetLogicalDescendants().OfType<TextBlock>(),
                    text => text.Text == "FocusRGB v0.4.0-beta.1 (Beta)");
                about.Close();
            }
            finally
            {
                window.AllowClose();
                window.Close();
            }
        }
    }
}
