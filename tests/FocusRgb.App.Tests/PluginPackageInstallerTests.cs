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

        [Fact]
        public async Task StageOfficialAsync_VerifiesReleaseSetBeforeStaging()
        {
            var archive = CreatePackage();
            var (manifest, checksums) = CreateReleaseMetadata(archive, ManifestJson());
            var installer = new PluginPackageInstaller(PackagesDirectory);

            var result = await installer.StageOfficialAsync(
                Listing(), archive, manifest, checksums, TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.NotNull(result.StagedPackage);
        }

        [Fact]
        public async Task StageOfficialAsync_RejectsAmbiguousZipAssets()
        {
            var archive = CreatePackage();
            var (manifest, checksums) = CreateReleaseMetadata(archive, ManifestJson());
            var listing = Listing(
                [
                    new OfficialPluginAsset { Name = "plugin.zip", Url = "https://example.test/plugin.zip" },
                    new OfficialPluginAsset { Name = "other.zip", Url = "https://example.test/other.zip" }
                ]);
            var installer = new PluginPackageInstaller(PackagesDirectory);

            var result = await installer.StageOfficialAsync(
                listing, archive, manifest, checksums, TestContext.Current.CancellationToken);

            Assert.False(result.IsSuccess);
            Assert.Equal("package-ambiguous", result.ErrorCode);
        }

        [Fact]
        public async Task StageOfficialAsync_RejectsZipMissingFromChecksums()
        {
            var archive = CreatePackage();
            var (manifest, checksums) = CreateReleaseMetadata(
                archive, ManifestJson(), includePackage: false);
            var installer = new PluginPackageInstaller(PackagesDirectory);

            var result = await installer.StageOfficialAsync(
                Listing(), archive, manifest, checksums, TestContext.Current.CancellationToken);

            Assert.False(result.IsSuccess);
            Assert.Equal("checksum-missing", result.ErrorCode);
        }

        [Fact]
        public async Task StageOfficialAsync_RejectsManifestAssetHashMismatch()
        {
            var archive = CreatePackage();
            var (manifest, checksums) = CreateReleaseMetadata(
                archive, ManifestJson(), manifestHash: new string('0', 64));
            var installer = new PluginPackageInstaller(PackagesDirectory);

            var result = await installer.StageOfficialAsync(
                Listing(), archive, manifest, checksums, TestContext.Current.CancellationToken);

            Assert.False(result.IsSuccess);
            Assert.Equal("checksum-mismatch", result.ErrorCode);
        }

        [Fact]
        public async Task StageOfficialAsync_RejectsZipManifestVersionThatDiffersFromListing()
        {
            var archive = CreatePackage(version: "2.0.0");
            var (manifest, checksums) = CreateReleaseMetadata(archive, ManifestJson());
            var installer = new PluginPackageInstaller(PackagesDirectory);

            var result = await installer.StageOfficialAsync(
                Listing(), archive, manifest, checksums, TestContext.Current.CancellationToken);

            Assert.False(result.IsSuccess);
            Assert.Equal("listing-mismatch", result.ErrorCode);
            Assert.Empty(Directory.GetDirectories(Path.Combine(PackagesDirectory, ".staging")));
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
            int additionalEntries = 0,
            string version = "1.0.0")
        {
            var path = Path.Combine(_temporaryDirectory.Path, $"package-{Guid.NewGuid():N}.zip");
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            WriteEntry(archive, PluginPackage.ManifestFileName, ManifestJson(version));
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

        private (string Manifest, string Checksums) CreateReleaseMetadata(
            string archive,
            string manifestJson,
            bool includePackage = true,
            string? manifestHash = null)
        {
            var manifest = Path.Combine(_temporaryDirectory.Path, $"manifest-{Guid.NewGuid():N}.json");
            var checksums = Path.Combine(_temporaryDirectory.Path, $"checksums-{Guid.NewGuid():N}.txt");
            File.WriteAllText(manifest, manifestJson);
            var lines = new List<string>();
            if (includePackage) lines.Add($"{Sha256(archive)}  plugin.zip");
            lines.Add($"{manifestHash ?? Sha256(manifest)}  {PluginPackage.ManifestFileName}");
            File.WriteAllLines(checksums, lines);
            return (manifest, checksums);
        }

        private static OfficialPluginListing Listing(
            IReadOnlyList<OfficialPluginAsset>? packageCandidates = null) => new()
        {
            Id = "fake-output",
            Name = "Fake Output",
            Version = "1.0.0",
            RepositoryUrl = "https://github.com/FocusRGB/FocusRGB-Plugin-Fake",
            ReleaseUrl = "https://github.com/FocusRGB/FocusRGB-Plugin-Fake/releases/tag/v1.0.0",
            PackageUrl = "https://example.test/plugin.zip",
            PackageFileName = "plugin.zip",
            PackageCandidates = packageCandidates ??
                [new OfficialPluginAsset { Name = "plugin.zip", Url = "https://example.test/plugin.zip" }],
            ManifestUrl = "https://example.test/focusrgb-plugin.json",
            ChecksumsUrl = "https://example.test/SHA256SUMS",
            ContractVersion = "1.0",
            Resources = [],
            OperatingSystems = OperatingSystems,
            Architectures = Architectures,
            IsCompatible = true
        };

        private static string ManifestJson(string version = "1.0.0") => JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            id = "fake-output",
            version,
            contractVersion = "1.0",
            entryPoint = "plugin.exe",
            operatingSystems = OperatingSystems,
            architectures = Architectures,
            roles = Roles,
            resources = Array.Empty<string>(),
            license = "MIT"
        }, PluginJson.Options);

        private static string Sha256(string path) => Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

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
