using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
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
                debuggingMode = viewModel.SavedDebuggingMode,
                rawLogsIncluded = false
            });
        }

        public static async Task WriteZipAsync(
            MainWindowViewModel viewModel,
            DateTimeOffset generatedAtUtc,
            Stream output,
            CancellationToken cancellationToken = default)
        {
            var json = CreateJson(viewModel, generatedAtUtc);
            using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
            var entry = archive.CreateEntry("diagnostics.json", CompressionLevel.Optimal);
            await using var entryStream = entry.Open();
            await using var writer = new StreamWriter(entryStream, new UTF8Encoding(false));
            // Raw host/plugin logs can contain arbitrary credentials; export only allowlisted metadata.
            await writer.WriteAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }
}
