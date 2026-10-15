using System.Runtime.InteropServices;
using System.Text.Json;
using FocusRgb.App.ViewModels;
using FocusRgb.Core.Configuration;

namespace FocusRgb.App.Diagnostics
{
    /// <summary>Builds a diagnostic summary that deliberately excludes user content and paths.</summary>
    public static class DiagnosticExport
    {
        public static string CreateJson(MainWindowViewModel viewModel, DateTimeOffset generatedAtUtc)
        {
            ArgumentNullException.ThrowIfNull(viewModel);
            return JsonSerializer.Serialize(new
            {
                generatedAtUtc,
                appVersion = typeof(App).Assembly.GetName().Version?.ToString(),
                framework = RuntimeInformation.FrameworkDescription,
                operatingSystem = RuntimeInformation.OSDescription,
                settingsSchema = SettingsJson.CurrentSchemaVersion,
                theme = viewModel.SavedApplicationTheme,
                closeBehavior = viewModel.SavedCloseButtonBehavior,
                startMinimized = viewModel.SavedStartMinimized,
                verboseLogging = viewModel.SavedVerboseLogging,
                appliedOutput = "unavailable"
            });
        }
    }
}
