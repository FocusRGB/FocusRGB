using System.Net;
using System.Text;
using FocusRgb.App.Plugins;
using FocusRgb.TestSupport;

namespace FocusRgb.App.Tests
{
    public sealed class PluginPackageDownloaderTests : IDisposable
    {
        private readonly TempDirectory _temporaryDirectory = new(nameof(PluginPackageDownloaderTests));

        [Fact]
        public async Task DownloadCustomGitHubAsync_DownloadsOnlyReleaseZip()
        {
            using var client = new HttpClient(new StubHandler(request => request.RequestUri!.Host == "api.github.com"
                ? Json("""
                    { "assets": [
                      { "name": "plugin.zip", "browser_download_url": "https://downloads.example/plugin.zip" }
                    ] }
                    """)
                : Bytes("package")));
            var downloader = new PluginPackageDownloader(client, _temporaryDirectory.Path);

            using var result = await downloader.DownloadCustomGitHubAsync(
                "FocusRGB/FocusRGB-Plugin-Test", TestContext.Current.CancellationToken);

            Assert.Equal("https://github.com/FocusRGB/FocusRGB-Plugin-Test", result.Source);
            Assert.Equal("package", await File.ReadAllTextAsync(
                result.ArchivePath, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task DownloadCustomGitHubAsync_RejectsAmbiguousZipAssets()
        {
            using var client = new HttpClient(new StubHandler(_ => Json("""
                { "assets": [
                  { "name": "one.zip", "browser_download_url": "https://downloads.example/one.zip" },
                  { "name": "two.zip", "browser_download_url": "https://downloads.example/two.zip" }
                ] }
                """)));
            var downloader = new PluginPackageDownloader(client, _temporaryDirectory.Path);

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                downloader.DownloadCustomGitHubAsync(
                    "https://github.com/example/plugin", TestContext.Current.CancellationToken));

            Assert.Contains("exactly one", exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("http://github.com/example/plugin")]
        [InlineData("https://example.com/example/plugin")]
        [InlineData("example")]
        [InlineData("example/plugin/extra")]
        public async Task DownloadCustomGitHubAsync_RejectsInvalidRepository(string repository)
        {
            using var client = new HttpClient(new StubHandler(_ => Bytes("unused")));
            var downloader = new PluginPackageDownloader(client, _temporaryDirectory.Path);

            await Assert.ThrowsAsync<ArgumentException>(() => downloader.DownloadCustomGitHubAsync(
                repository, TestContext.Current.CancellationToken));
        }

        public void Dispose() => _temporaryDirectory.Dispose();

        private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(value, Encoding.UTF8, "application/json")
        };

        private static HttpResponseMessage Bytes(string value) => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(value))
        };

        private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken) => Task.FromResult(response(request));
        }
    }
}
