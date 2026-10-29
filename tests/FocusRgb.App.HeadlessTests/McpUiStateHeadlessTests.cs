using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FocusRgb.App.ViewModels;

namespace FocusRgb.App.HeadlessTests
{
    public sealed class McpUiStateHeadlessTests
    {
        [AvaloniaFact]
        public async Task DiagnosticUiState_ReportsEditorFlagsAndDialogNamesWithoutTitles()
        {
            var model = MainWindowViewModel.CreatePreview();
            var window = new MainWindow(model);
            window.Show();
            var dialog = new Window { Name = "About", Title = "never-record-this-title" };
            try
            {
                var tabs = window.FindControl<TabControl>("MainTabs")!;
                tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(tab => Equals(tab.Header, "Settings"));
                model.VerboseLogging = !model.VerboseLogging;
                model.MappingEditor.BeginCreate();
                model.ProfileEffect += " draft";
                var closed = dialog.ShowDialog(window);
                var state = window.GetDiagnosticUiState(profileToastVisible: true);
                Assert.Equal("Settings", state.SelectedTab);
                Assert.True(state.SettingsDirty);
                Assert.True(state.FocusLibraryDirty);
                Assert.True(state.ProfilesDirty);
                Assert.Contains("About", state.Dialogs);
                Assert.Contains("FocusChange", state.Toasts);
                Assert.DoesNotContain("never-record-this-title", System.Text.Json.JsonSerializer.Serialize(state), StringComparison.Ordinal);
                dialog.Close();
                await closed;
            }
            finally
            {
                dialog.Close();
                window.AllowClose();
                window.Close();
            }
        }
    }
}
