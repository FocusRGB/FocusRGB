using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FocusRgb.App.ViewModels;

namespace FocusRgb.App.HeadlessTests
{
    public sealed class DebuggingModeHeadlessTests
    {
        [AvaloniaFact]
        public async Task Settings_LoggingIsDisabledUntilModeEnabledAndKeepsChoiceWhenModeReturns()
        {
            var model = MainWindowViewModel.CreatePreview();
            var window = new MainWindow(model);
            window.Show();
            try
            {
                var tabs = window.FindControl<TabControl>("MainTabs")!;
                tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(tab => Equals(tab.Header, "Settings"));
                var mode = window.FindControl<CheckBox>("DebuggingModeCheckBox")!;
                var logging = window.FindControl<CheckBox>("DiagnosticLoggingCheckBox")!;
                Assert.False(mode.IsChecked);
                Assert.False(logging.IsChecked);
                Assert.False(logging.IsEnabled);
                mode.IsChecked = true;
                Assert.True(logging.IsEnabled);
                logging.IsChecked = true;
                Assert.False(model.SavedDebuggingMode);
                Assert.False(model.SavedVerboseLogging);
                model.CancelApplicationSettings();
                Assert.False(logging.IsEnabled);
                Assert.False(logging.IsChecked);

                mode.IsChecked = true;
                logging.IsChecked = true;
                Assert.True(await model.SaveApplicationSettingsAsync(TestContext.Current.CancellationToken));
                Assert.True(model.SavedDebuggingMode);
                Assert.True(model.SavedVerboseLogging);
                mode.IsChecked = false;
                Assert.False(logging.IsEnabled);
                Assert.False(logging.IsChecked);
                Assert.True(model.SavedDebuggingMode);
                Assert.True(await model.SaveApplicationSettingsAsync(TestContext.Current.CancellationToken));
                Assert.False(model.SavedDebuggingMode);
                Assert.True(model.SavedVerboseLogging);
                mode.IsChecked = true;
                Assert.True(logging.IsEnabled);
                Assert.True(logging.IsChecked);
            }
            finally { window.AllowClose(); window.Close(); }
        }
    }
}
