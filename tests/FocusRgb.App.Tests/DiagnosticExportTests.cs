using System.IO.Compression;
using System.Text.Json;
using FocusRgb.App.Diagnostics;
using FocusRgb.App.ViewModels;
using FocusRgb.Core.Persistence;
using FocusRgb.TestSupport;

namespace FocusRgb.App.Tests
{
    public sealed class DiagnosticExportTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task WriteZipAsync_OmitsUntrustedLogsAndCredentials_WhenLoggingIsEnabledOrDisabled(bool enabled)
        {
            using var directory = new TempDirectory(nameof(DiagnosticExportTests));
            var store = new LocalStateStore(directory.Path);
            var startup = await store.InitializeAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
            var viewModel = MainWindowViewModel.CreatePersistent(startup.Snapshot!, store);
            viewModel.VerboseLogging = enabled;
            await viewModel.SaveApplicationSettingsAsync(TestContext.Current.CancellationToken);
            Assert.Equal(enabled, viewModel.SavedVerboseLogging);
            var logs = Path.Combine(directory.Path, "logs");
            Directory.CreateDirectory(logs);
            const string contents = "ordinary startup\nAuthorization: Bearer SECRET_AUTH\nPlugin fake: token=SECRET_TOKEN https://example.test/?api_key=SECRET_URL\nC:\\Private\\SECRET_PATH\npassword=SECRET_PASSWORD\n";
            await File.WriteAllTextAsync(Path.Combine(logs, "focusrgb.log"), contents, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(logs, "plugin.log"), contents, TestContext.Current.CancellationToken);
            using var output = new MemoryStream();

            await DiagnosticExport.WriteZipAsync(viewModel, DateTimeOffset.UtcNow, output, TestContext.Current.CancellationToken);

            output.Position = 0;
            using var archive = new ZipArchive(output, ZipArchiveMode.Read);
            var entry = Assert.Single(archive.Entries);
            Assert.Equal("diagnostics.json", entry.FullName);
            using var reader = new StreamReader(entry.Open());
            var text = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
            Assert.DoesNotContain("SECRET_", text, StringComparison.Ordinal);
            using var json = JsonDocument.Parse(text);
            Assert.False(json.RootElement.GetProperty("rawLogsIncluded").GetBoolean());
            Assert.NotEmpty(json.RootElement.GetProperty("framework").GetString()!);
            Assert.NotEmpty(json.RootElement.GetProperty("operatingSystem").GetString()!);
            Assert.False(json.RootElement.TryGetProperty("appliedOutput", out _));
            Assert.Equal(contents, await File.ReadAllTextAsync(Path.Combine(logs, "focusrgb.log"), TestContext.Current.CancellationToken));
            Assert.True(output.CanRead);
        }
    }
}
