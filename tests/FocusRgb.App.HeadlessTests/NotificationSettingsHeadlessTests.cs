using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FocusRgb.App.ViewModels;

namespace FocusRgb.App.HeadlessTests
{
    public sealed class NotificationSettingsHeadlessTests
    {
        [AvaloniaFact]
        public async Task Appearance_ChoicesSaveAndCancelLikeOtherSettings()
        {
            var model = MainWindowViewModel.CreatePreview();
            var window = new MainWindow(model);
            window.Show();
            try
            {
                var size = window.FindControl<ComboBox>("NotificationSizePicker")!;
                var background = window.FindControl<ComboBox>("NotificationBackgroundPicker")!;
                var duration = window.FindControl<ComboBox>("NotificationDurationPicker")!;
                Assert.Equal("Medium", size.SelectedItem);
                Assert.Equal("Dark", background.SelectedItem);
                Assert.Equal(3, duration.SelectedItem);
                size.SelectedItem = "Small";
                background.SelectedItem = "Blue";
                duration.SelectedItem = 2;
                Assert.True(model.IsApplicationSettingsDirty);
                Assert.True(await model.SaveApplicationSettingsAsync(TestContext.Current.CancellationToken));
                size.SelectedItem = "Large";
                model.CancelApplicationSettings();
                Assert.Equal("Small", size.SelectedItem);
                Assert.Equal("Blue", background.SelectedItem);
                Assert.Equal(2, duration.SelectedItem);
                Assert.False(model.IsApplicationSettingsDirty);
            }
            finally { window.AllowClose(); window.Close(); }
        }
    }
}
