using System.IO.Compression;
using System.Text.Json;
using FocusRgb.App.Plugins;
using FocusRgb.Core.Protocol;
using FocusRgb.TestSupport;

namespace FocusRgb.App.Tests
{
    public sealed class PluginPackageInstallerTests : IDisposable
    {
        private static readonly string[] OperatingSystems = [CurrentOperatingSystem()];
        private static readonly string[] Architectures =
            [System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()];
        private static readonly string[] Roles = ["output"];
        private readonly TempDirectory _temporaryDirectory = new(nameof(PluginPackageInstallerTests));
        private string PackagesDirectory => Path.Combine(_temporaryDirectory.Path, "plugins");

        [Fact]
        public async Task InspectAsync_ReturnsManifestAndHash_WithoutCreatingPluginFiles()
        {
            var archive = CreatePackage();
            var installer = new PluginPackageInstaller(PackagesDirectory);

            var result = await installer.InspectAsync(
                archive, cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal("fake-output", result.Manifest!.Id);
            Assert.Matches("^[0-9a-f]{64}$", result.Sha256!);
            Assert.Null(result.StagedPackage);
            Assert.False(Directory.Exists(PackagesDirectory));
        }

        [Fact]
        public async Task StageAsync_RejectsChecksumMismatch_WithoutChangingInstalledPackage()
        {
            var archive = CreatePackage();
            var installed = Path.Combine(PackagesDirectory, "fake-output");
            Directory.CreateDirectory(installed);
            File.WriteAllText(Path.Combine(installed, "old.txt"), "old");
            var installer = new PluginPackageInstaller(PackagesDirectory);

            var result = await installer.StageAsync(
                archive, "fake-output", new string('0', 64), TestContext.Current.CancellationToken);

            Assert.False(result.IsSuccess);
            Assert.Equal("checksum-mismatch", result.ErrorCode);
            Assert.True(File.Exists(Path.Combine(installed, "old.txt")));
            Assert.False(Directory.Exists(Path.Combine(PackagesDirectory, ".staging")));
        }

        [Theory]
        [InlineData("../outside.txt")]
        [InlineData("/absolute.txt")]
        [InlineData("plugin.exe:payload")]
        public async Task StageAsync_RejectsUnsafePaths(string unsafePath)
        {
            var archive = CreatePackage(extraEntry: unsafePath);
            var installer = new PluginPackageInstaller(PackagesDirectory);
            var inspected = await installer.InspectAsync(
                archive, cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(inspected.IsSuccess);
            Assert.Equal("package-invalid", inspected.ErrorCode);
            Assert.False(File.Exists(Path.Combine(_temporaryDirectory.Path, "outside.txt")));
        }

        [Fact]
        public async Task StageAsync_RejectsSymbolicLinkEntry()
        {
            var archive = CreatePackage(symbolicLink: true);
            var installer = new PluginPackageInstaller(PackagesDirectory);

            var result = await installer.InspectAsync(
                archive, cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result.IsSuccess);
            Assert.Contains("link or reparse point", result.ErrorMessage, StringComparison.Ordinal);
        }

        [Fact]
        public async Task InspectAsync_RejectsPackageOverFileCountLimit()
        {
            var archive = CreatePackage(additionalEntries: PluginPackageInstaller.MaximumEntryCount - 1);
            var installer = new PluginPackageInstaller(PackagesDirectory);

            var result = await installer.InspectAsync(
                archive, cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result.IsSuccess);
            Assert.Contains("at most", result.ErrorMessage, StringComparison.Ordinal);
        }

        [Fact]
        public async Task StageAndCommit_ReplacesOnlyMatchingPackage()
        {
            var archive = CreatePackage();
            var installed = Path.Combine(PackagesDirectory, "fake-output");
            var other = Path.Combine(PackagesDirectory, "other-plugin");
            Directory.CreateDirectory(installed);
            Directory.CreateDirectory(other);
            File.WriteAllText(Path.Combine(installed, "old.txt"), "old");
            File.WriteAllText(Path.Combine(other, "keep.txt"), "keep");
            var installer = new PluginPackageInstaller(PackagesDirectory);
            var inspection = await installer.InspectAsync(
                archive, "fake-output", TestContext.Current.CancellationToken);
            var staged = await installer.StageAsync(
                archive, "fake-output", inspection.Sha256!, TestContext.Current.CancellationToken);

            var result = installer.Commit(staged.StagedPackage!);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.False(File.Exists(Path.Combine(installed, "old.txt")));
            Assert.True(File.Exists(Path.Combine(installed, "plugin.exe")));
            Assert.True(File.Exists(Path.Combine(other, "keep.txt")));
        }

        [Fact]
        public void Uninstall_RemovesOnlyPackageAndPreservesSettings()
        {
            var installed = Path.Combine(PackagesDirectory, "fake-output");
            var other = Path.Combine(PackagesDirectory, "other-plugin");
            Directory.CreateDirectory(installed);
            Directory.CreateDirectory(other);
            var settings = Path.Combine(_temporaryDirectory.Path, "settings.json");
            File.WriteAllText(settings, "settings");
            var installer = new PluginPackageInstaller(PackagesDirectory);

            var result = installer.Uninstall("fake-output");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.False(Directory.Exists(installed));
            Assert.True(Directory.Exists(other));
            Assert.Equal("settings", File.ReadAllText(settings));
        }

        public void Dispose()
        {
            _temporaryDirectory.Dispose();
        }

        private string CreatePackage(
            string? extraEntry = null,
            bool symbolicLink = false,
            int additionalEntries = 0)
        {
            var path = Path.Combine(_temporaryDirectory.Path, $"package-{Guid.NewGuid():N}.zip");
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            WriteEntry(archive, PluginPackage.ManifestFileName, JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                id = "fake-output",
                version = "1.0.0",
                contractVersion = "1.0",
                entryPoint = "plugin.exe",
                operatingSystems = OperatingSystems,
                architectures = Architectures,
                roles = Roles,
                resources = Array.Empty<string>(),
                license = "MIT"
            }, PluginJson.Options));
            WriteEntry(archive, "plugin.exe", "plugin");
            if (extraEntry is not null) WriteEntry(archive, extraEntry, "unsafe");
            for (var index = 0; index < additionalEntries; index++)
                WriteEntry(archive, $"extra/{index}.txt", string.Empty);
            if (symbolicLink)
            {
                var link = archive.CreateEntry("link");
                link.ExternalAttributes = 0xA000 << 16;
                using var writer = new StreamWriter(link.Open());
                writer.Write("target");
            }
            return path;
        }

        private static void WriteEntry(ZipArchive archive, string path, string content)
        {
            var entry = archive.CreateEntry(path);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        private static string CurrentOperatingSystem() => OperatingSystem.IsWindows()
            ? "windows"
            : OperatingSystem.IsMacOS() ? "macos" : "linux";
    }
}
