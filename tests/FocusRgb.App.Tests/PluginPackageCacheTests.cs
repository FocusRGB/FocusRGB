using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FocusRgb.App.Plugins;
using FocusRgb.App.ViewModels;
using FocusRgb.Core.Protocol;
using FocusRgb.TestSupport;

namespace FocusRgb.App.Tests
{
    public sealed class PluginPackageCacheTests : IDisposable
    {
        private readonly TempDirectory _temporary = new(nameof(PluginPackageCacheTests));

        [Fact]
        public async Task DownloadAsync_VerifiesAndRetainsPackageUntilSeparateInstall()
        {
            var listing = Listing();
            var manifest = JsonSerializer.Serialize(FakePluginPackage.Manifest("plugin.dll"), PluginJson.Options);
            var zip = CreateZip(manifest);
            var sums = $"{Hash(zip)}  plugin.zip\n{Hash(Encoding.UTF8.GetBytes(manifest))}  focusrgb-plugin.json\n";
            var requests = 0;
            using var client = new HttpClient(new ReleaseHandler(path =>
            {
                requests++;
                return path switch
                {
                    "/plugin.zip" => zip,
                    "/focusrgb-plugin.json" => Encoding.UTF8.GetBytes(manifest),
                    "/SHA256SUMS" => Encoding.UTF8.GetBytes(sums),
                    _ => throw new InvalidOperationException(path)
                };
            }));
            var packages = Path.Combine(_temporary.Path, "plugins");
            var installer = new PluginPackageInstaller(packages);
            var cacheDirectory = Path.Combine(_temporary.Path, "cache");
            var cache = new PluginPackageCache(cacheDirectory);
            var downloader = new PluginPackageDownloader(client, Path.Combine(_temporary.Path, "downloads"));
            Assert.False(cache.Contains(listing));

            await cache.DownloadAsync(listing, downloader, installer, TestContext.Current.CancellationToken);

            Assert.True(cache.Contains(listing));
            Assert.False(Directory.Exists(Path.Combine(packages, listing.Id)));
            using var cached = new PluginPackageCache(cacheDirectory).Open(listing);
            Assert.NotNull(cached);
            var staged = await installer.StageOfficialAsync(listing, cached.ArchivePath,
                cached.ManifestPath!, cached.ChecksumsPath!, TestContext.Current.CancellationToken);
            Assert.True(staged.IsSuccess, staged.ErrorMessage);
            Assert.True(installer.Commit(staged.StagedPackage!).IsSuccess);
            await cache.DownloadAsync(listing, downloader, installer, TestContext.Current.CancellationToken);
            Assert.Equal(3, requests);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task InspectThenInstall_UsesVerifiedConsentBeforeZipAndPinsApprovedHashes(bool consent, bool changed)
        {
            var listing = Listing();
            var manifest = JsonSerializer.Serialize(FakePluginPackage.Manifest("plugin.dll"), PluginJson.Options);
            var zip = CreateZip(manifest);
            var sums = $"{Hash(zip)}  plugin.zip\n{Hash(Encoding.UTF8.GetBytes(manifest))}  focusrgb-plugin.json\n";
            var requests = new List<string>();
            using var client = new HttpClient(new ReleaseHandler(path =>
            {
                requests.Add(path);
                return path switch
                {
                    "/plugin.zip" => zip,
                    "/focusrgb-plugin.json" => Encoding.UTF8.GetBytes(manifest),
                    "/SHA256SUMS" => Encoding.UTF8.GetBytes(sums),
                    _ => throw new InvalidOperationException(path)
                };
            }));
            var packages = Path.Combine(_temporary.Path, "plugins");
            var installer = new PluginPackageInstaller(packages);
            var cache = new PluginPackageCache(Path.Combine(_temporary.Path, "cache"));
            var downloader = new PluginPackageDownloader(client, Path.Combine(_temporary.Path, "downloads"));

            var inspected = await cache.InspectOfficialAsync(listing, downloader, TestContext.Current.CancellationToken);

            Assert.True(inspected.IsSuccess, inspected.ErrorMessage);
            Assert.Equal(Hash(zip), inspected.Sha256);
            Assert.Equal(["/focusrgb-plugin.json", "/SHA256SUMS"], requests);
            Assert.False(cache.Contains(listing));
            Assert.False(Directory.Exists(Path.Combine(packages, listing.Id)));
            if (!consent) return;
            if (changed)
            {
                manifest = manifest.Replace("\"license\":\"MIT\"", "\"license\":\"Apache-2.0\"", StringComparison.Ordinal);
                zip = CreateZip(manifest);
                sums = $"{Hash(zip)}  plugin.zip\n{Hash(Encoding.UTF8.GetBytes(manifest))}  focusrgb-plugin.json\n";
            }
            await cache.DownloadAsync(listing, downloader, installer, TestContext.Current.CancellationToken);
            var staged = await cache.StageAsync(listing, installer, inspected, TestContext.Current.CancellationToken);

            Assert.Equal(!changed, staged.IsSuccess);
            if (changed)
            {
                Assert.Equal("release-changed", staged.ErrorCode);
                Assert.False(cache.Contains(listing));
                Assert.False(Directory.Exists(Path.Combine(packages, listing.Id)));
                return;
            }
            Assert.True(installer.Commit(staged.StagedPackage!).IsSuccess);
            var requestCount = requests.Count;
            var offline = await cache.InspectOfficialAsync(listing, downloader, TestContext.Current.CancellationToken);
            await cache.DownloadAsync(listing, downloader, installer, TestContext.Current.CancellationToken);
            var reinstalled = await cache.StageAsync(listing, installer, offline, TestContext.Current.CancellationToken);
            Assert.True(reinstalled.IsSuccess, reinstalled.ErrorMessage);
            Assert.True(installer.Commit(reinstalled.StagedPackage!).IsSuccess);
            Assert.Equal(requestCount, requests.Count);
        }

        [Fact]
        public async Task DownloadAsync_DoesNotCacheUnverifiedAssets()
        {
            using var client = new HttpClient(new ReleaseHandler(_ => Encoding.UTF8.GetBytes("invalid")));
            var cache = new PluginPackageCache(Path.Combine(_temporary.Path, "cache"));
            var downloader = new PluginPackageDownloader(client, Path.Combine(_temporary.Path, "downloads"));
            var installer = new PluginPackageInstaller(Path.Combine(_temporary.Path, "plugins"));

            await Assert.ThrowsAsync<InvalidDataException>(() => cache.DownloadAsync(
                Listing(), downloader, installer, TestContext.Current.CancellationToken));

            Assert.False(cache.Contains(Listing()));
        }

        [Theory]
        [InlineData("plugin.zip", true)]
        [InlineData("plugin.zip", false)]
        [InlineData("focusrgb-plugin.json", true)]
        [InlineData("focusrgb-plugin.json", false)]
        [InlineData("SHA256SUMS", true)]
        [InlineData("SHA256SUMS", false)]
        public async Task StageAsync_PrunesDamagedCacheAndAllowsRedownload(string damagedFile, bool empty)
        {
            var manifest = JsonSerializer.Serialize(FakePluginPackage.Manifest("plugin.dll"), PluginJson.Options);
            var zip = CreateZip(manifest);
            var sums = $"{Hash(zip)}  plugin.zip\n{Hash(Encoding.UTF8.GetBytes(manifest))}  focusrgb-plugin.json\n";
            var requests = 0;
            using var client = new HttpClient(new ReleaseHandler(path =>
            {
                requests++;
                return path switch
                {
                    "/plugin.zip" => zip,
                    "/focusrgb-plugin.json" => Encoding.UTF8.GetBytes(manifest),
                    _ => Encoding.UTF8.GetBytes(sums)
                };
            }));
            var cache = new PluginPackageCache(Path.Combine(_temporary.Path, "cache"));
            var downloader = new PluginPackageDownloader(client, Path.Combine(_temporary.Path, "downloads"));
            var installer = new PluginPackageInstaller(Path.Combine(_temporary.Path, "plugins"));
            var listing = Listing();
            await cache.DownloadAsync(listing, downloader, installer, TestContext.Current.CancellationToken);
            using var cached = cache.Open(listing)!;
            File.WriteAllBytes(Path.Combine(cached.Directory, damagedFile), empty ? [] : [1, 2, 3]);

            var failed = await cache.StageAsync(listing, installer, cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(failed.IsSuccess);
            Assert.False(cache.Contains(listing));
            Assert.False(Directory.Exists(cached.Directory));
            await cache.DownloadAsync(listing, downloader, installer, TestContext.Current.CancellationToken);
            Assert.Equal(6, requests);
            var repaired = await cache.StageAsync(listing, installer, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(repaired.IsSuccess, repaired.ErrorMessage);
            installer.Discard(repaired.StagedPackage!);
        }

        [Fact]
        public void Open_UsesReleaseIdentityDespiteDescriptionChanges()
        {
            var directory = Path.Combine(_temporary.Path, "cache");
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("fake-output\n0.1.0")));
            var entry = Path.Combine(directory, key);
            Directory.CreateDirectory(entry);
            foreach (var name in new[] { "plugin.zip", "focusrgb-plugin.json", "SHA256SUMS" })
                File.WriteAllText(Path.Combine(entry, name), "content");
            var cache = new PluginPackageCache(directory);

            Assert.True(cache.Contains(Listing(description: "Old description")));
            Assert.True(cache.Contains(Listing(description: "Edited description")));
        }

        [Fact]
        public void Prune_BoundsCacheCountAndAgeWhilePreservingOtherDirectories()
        {
            var directory = Path.Combine(_temporary.Path, "cache");
            for (var index = 0; index < 35; index++)
            {
                var entry = Path.Combine(directory, index.ToString("X64", System.Globalization.CultureInfo.InvariantCulture));
                Directory.CreateDirectory(entry);
                Directory.SetLastWriteTimeUtc(entry, DateTime.UtcNow.AddMinutes(-index));
            }
            var old = Path.Combine(directory, new string('F', 64));
            Directory.CreateDirectory(old);
            Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-31));
            Directory.CreateDirectory(Path.Combine(directory, "unrelated"));

            new PluginPackageCache(directory).Prune();

            Assert.Equal(32, Directory.GetDirectories(directory).Count(path => Path.GetFileName(path).Length == 64));
            Assert.False(Directory.Exists(old));
            Assert.True(Directory.Exists(Path.Combine(directory, "unrelated")));
        }

        [Fact]
        public void PluginLists_FilterInstalledAndDetectNewerOfficialReleaseRegardlessOfLoadOrder()
        {
            var viewModel = MainWindowViewModel.CreatePreview();
            var listing = Listing("1.10.0");
            viewModel.SetOfficialPlugins(OfficialPluginCatalogResult.Fresh(
                DateTimeOffset.UtcNow, [listing], "Ready"), _ => false);
            Assert.False(Assert.Single(viewModel.OfficialPlugins).IsCached);
            viewModel.SetOfficialPlugins(OfficialPluginCatalogResult.Fresh(
                DateTimeOffset.UtcNow, [listing], "Ready"), _ => true);
            Assert.True(Assert.Single(viewModel.OfficialPlugins).IsCached);
            viewModel.SetInstalledPlugins([new InstalledPluginInfo
            {
                Id = listing.Id, DisplayName = listing.Name, Version = "1.9.0", IsOfficial = true
            }]);

            Assert.Empty(viewModel.OfficialPlugins);
            Assert.Equal("1.10.0", Assert.Single(viewModel.InstalledPlugins).AvailableVersion);
            viewModel.SetInstalledPlugins([]);
            Assert.Single(viewModel.OfficialPlugins);
        }

        [Theory]
        [InlineData("1.0.0", true)]
        [InlineData("2.0.0", true)]
        [InlineData("0.9.0", false)]
        public void InstalledPlugin_OffersOnlyNewerOfficialUpdates(string installedVersion, bool official)
        {
            var item = new InstalledPluginItemViewModel(new InstalledPluginInfo
            {
                Id = "fake-output", DisplayName = "Fake", Version = installedVersion, IsOfficial = official
            }, Listing());

            Assert.False(item.HasUpdate);
        }

        internal static OfficialPluginListing Listing(string version = "0.1.0", string? description = null) => new()
        {
            Id = "fake-output", Name = "Fake", Version = version,
            Description = description,
            RepositoryUrl = "https://github.com/FocusRGB/FocusRGB-Plugin-Fake",
            ReleaseUrl = "https://github.com/FocusRGB/FocusRGB-Plugin-Fake/releases/tag/v" + version,
            PackageUrl = "https://example.test/plugin.zip", PackageFileName = "plugin.zip",
            PackageCandidates = [new OfficialPluginAsset { Name = "plugin.zip", Url = "https://example.test/plugin.zip" }],
            ManifestUrl = "https://example.test/focusrgb-plugin.json", ChecksumsUrl = "https://example.test/SHA256SUMS",
            ContractVersion = ContractVersion.Current, IsCompatible = true
        };

        private static byte[] CreateZip(string manifest)
        {
            using var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                using (var writer = new StreamWriter(zip.CreateEntry("focusrgb-plugin.json").Open()))
                    writer.Write(manifest);
                using var binary = new StreamWriter(zip.CreateEntry("plugin.dll").Open());
                binary.Write("fixture");
            }
            return buffer.ToArray();
        }

        private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        public void Dispose() => _temporary.Dispose();

        private sealed class ReleaseHandler(Func<string, byte[]> response) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(response(request.RequestUri!.AbsolutePath))
                    });
        }
    }
}
