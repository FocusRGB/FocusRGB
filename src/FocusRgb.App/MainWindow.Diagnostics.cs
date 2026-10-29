using Avalonia.Controls;
using FocusRgb.App.Mcp;

namespace FocusRgb.App
{
    public sealed partial class MainWindow
    {
        internal DiagnosticUiState GetDiagnosticUiState(bool profileToastVisible)
        {
            var dialogs = OwnedWindows.Where(window => window.IsVisible).Take(9)
                .Select(window => window.Name ?? window.GetType().Name).ToList();
            if (_mcpApprovalId is not null)
            {
                dialogs.Add("McpApproval");
            }

            return new DiagnosticUiState((MainTabs.SelectedItem as TabItem)?.Header as string ?? "none",
                ViewModel.IsApplicationSettingsDirty, ViewModel.MappingEditor.IsDirty, ViewModel.IsProfileDirty, IsVisible,
                dialogs, profileToastVisible ? ["FocusChange"] : [],
                [new("Settings", ViewModel.ApplicationSettingsStatus),
                    new("FocusLibrary", ViewModel.MappingEditor.PreviewStatus), new("Profiles", ViewModel.ProfileEditorStatus),
                    new("Plugins", ViewModel.PluginsStatus), new("Runtime", ViewModel.RuntimeStatusMessage),
                    new("Games", ViewModel.GameEditorStatus), new("ApplicationGroups", ViewModel.ApplicationGroupEditorStatus)]);
        }
    }
}
