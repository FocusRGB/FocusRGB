using System.Net;
using System.Text;
using FocusRgb.App.Plugins;
using FocusRgb.TestSupport;

namespace FocusRgb.App.Tests
{
    public sealed class OfficialPluginDiscoveryTests : IDisposable
    {
        private readonly TempDirectory _temporaryDirectory = new(nameof(OfficialPluginDiscoveryTests));

        [Fact]
        public async Task DiscoverAsync_ReturnsOfficialReleaseAndUsesFreshCache()
        {
            var handler = new DiscoveryHandler();
            using var client = new HttpClient(handler);
            using var discovery = new OfficialPluginDiscovery(
                client, Path.Combine(_temporaryDirectory.Path, "catalog.json"));

            var first = await discovery.DiscoverAsync(cancellationToken: TestContext.Current.CancellationToken);
            var second = await discovery.DiscoverAsync(cancellationToken: TestContext.Current.CancellationToken);

            var plugin = Assert.Single(first.Items);
            Assert.Equal("signalrgb", plugin.Id);
            Assert.Equal("SignalRGB", plugin.Name);
            Assert.Equal("1.0.0", plugin.Version);
            Assert.Equal("SignalRGB integration", plugin.Description);
            Assert.Equal(["process.read", "filesystem.read"], plugin.Resources);
            Assert.True(plugin.IsCompatible);
            Assert.False(first.IsFromCache);
            Assert.True(second.IsFromCache);
            Assert.Equal(3, handler.RequestCount);
        }

        [Fact]
        public async Task DiscoverAsync_ReturnsStaleCache_WhenGitHubIsOffline()
        {
            var time = new TestTimeProvider(new DateTimeOffset(2026, 10, 21, 10, 0, 0, TimeSpan.Zero));
            using var onlineClient = new HttpClient(new DiscoveryHandler());
            var cachePath = Path.Combine(_temporaryDirectory.Path, "catalog.json");
            using (var onlineDiscovery = new OfficialPluginDiscovery(onlineClient, cachePath, time))
            {
                await onlineDiscovery.DiscoverAsync(cancellationToken: TestContext.Current.CancellationToken);
            }
            time.Advance(TimeSpan.FromHours(7));
            using var offlineClient = new HttpClient(new OfflineHandler());
            using var discovery = new OfficialPluginDiscovery(offlineClient, cachePath, time);

            var result = await discovery.DiscoverAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(result.IsFromCache);
            Assert.Single(result.Items);
            Assert.Contains("Showing the cached", result.Status, StringComparison.Ordinal);
        }

        [Fact]
        public async Task DiscoverAsync_IgnoresRepositoriesOutsideOfficialOrganization()
        {
            var handler = new DiscoveryHandler(includeUntrustedRepository: true);
            using var client = new HttpClient(handler);
            using var discovery = new OfficialPluginDiscovery(
                client, Path.Combine(_temporaryDirectory.Path, "catalog.json"));

            var result = await discovery.DiscoverAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Single(result.Items);
            Assert.Equal(3, handler.RequestCount);
        }

        [Fact]
        public async Task DiscoverAsync_DoesNotListRunnerUsingContract10()
        {
            var handler = new DiscoveryHandler(runtime: "node", contractVersion: "1.0");
            using var client = new HttpClient(handler);
            using var discovery = new OfficialPluginDiscovery(
                client, Path.Combine(_temporaryDirectory.Path, "catalog.json"));

            var result = await discovery.DiscoverAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Empty(result.Items);
            Assert.Equal(3, handler.RequestCount);
        }

        [Fact]
        public async Task DiscoverAsync_ReturnsClearMessage_WhenOfflineWithoutCache()
        {
            using var client = new HttpClient(new OfflineHandler());
            using var discovery = new OfficialPluginDiscovery(
                client, Path.Combine(_temporaryDirectory.Path, "catalog.json"));

            var result = await discovery.DiscoverAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Empty(result.Items);
            Assert.False(result.IsFromCache);
            Assert.Contains("unavailable", result.Status, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task DiscoverAsync_StopsRequests_WhenGitHubReportsExhaustedRateLimit()
        {
            var handler = new DiscoveryHandler(rateLimitedAfterSearch: true);
            using var client = new HttpClient(handler);
            using var discovery = new OfficialPluginDiscovery(
                client, Path.Combine(_temporaryDirectory.Path, "catalog.json"));

            var result = await discovery.DiscoverAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.Empty(result.Items);
            Assert.Equal(1, handler.RequestCount);
            Assert.Contains("rate limit", result.Status, StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose()
        {
            _temporaryDirectory.Dispose();
        }

        [Theory]
        [InlineData(false, false, false, 1)]
        [InlineData(false, true, false, 0)]
        [InlineData(true, true, false, 1)]
        [InlineData(true, false, false, 1)]
        [InlineData(true, true, true, 0)]
        public async Task DiscoverAsync_FiltersReleaseChannelAndDrafts(
            bool includeBeta, bool prerelease, bool draft, int expectedCount)
        {
            var handler = new DiscoveryHandler(prerelease: prerelease, draft: draft);
            using var client = new HttpClient(handler);
            using var discovery = new OfficialPluginDiscovery(client, Path.Combine(_temporaryDirectory.Path, "channels.json"));

            var result = await discovery.DiscoverAsync(cancellationToken: TestContext.Current.CancellationToken,
                includePrereleases: includeBeta);

            Assert.Equal(expectedCount, result.Items.Count);
            Assert.Equal(includeBeta, handler.UsedReleaseList);
        }

        [Fact]
        public async Task DiscoverAsync_DoesNotReuseBetaCache_WhenStableOnlyIsSelectedOffline()
        {
            var cachePath = Path.Combine(_temporaryDirectory.Path, "channels.json");
            using var onlineClient = new HttpClient(new DiscoveryHandler(prerelease: true));
            using (var online = new OfficialPluginDiscovery(onlineClient, cachePath))
            {
                var beta = await online.DiscoverAsync(cancellationToken: TestContext.Current.CancellationToken,
                    includePrereleases: true);
                Assert.Single(beta.Items);
            }
            using var offlineClient = new HttpClient(new OfflineHandler());
            using var offline = new OfficialPluginDiscovery(offlineClient, cachePath);
            var stable = await offline.DiscoverAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Empty(stable.Items);
            var cachedBeta = await offline.DiscoverAsync(cancellationToken: TestContext.Current.CancellationToken,
                includePrereleases: true);
            Assert.True(cachedBeta.IsFromCache);
            Assert.Single(cachedBeta.Items);
        }

        private sealed class DiscoveryHandler(
            bool includeUntrustedRepository = false,
            bool rateLimitedAfterSearch = false,
            string? runtime = null,
            string contractVersion = "1.0",
            bool prerelease = false,
            bool draft = false) : HttpMessageHandler
        {
            public int RequestCount { get; private set; }
            public bool UsedReleaseList { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                RequestCount++;
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/releases", StringComparison.Ordinal)) UsedReleaseList = true;
                var json = path switch
                {
                    "/search/repositories" => SearchJson(includeUntrustedRepository),
                    "/repos/FocusRGB/FocusRGB-Plugin-SignalRGB/releases/latest" => ReleaseJson(),
                    "/repos/FocusRGB/FocusRGB-Plugin-SignalRGB/releases" => "[" + ReleaseJson() + "]",
                    "/assets/focusrgb-plugin.json" => ManifestJson(),
                    _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
                };
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                    RequestMessage = request
                };
                if (rateLimitedAfterSearch && path == "/search/repositories")
                {
                    response.Headers.Add("X-RateLimit-Remaining", "0");
                    response.Headers.Add("X-RateLimit-Reset",
                        DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString(
                            System.Globalization.CultureInfo.InvariantCulture));
                }
                return Task.FromResult(response);
            }

            private static string SearchJson(bool includeUntrusted) => """
                {"items":[
                  {"name":"FocusRGB-Plugin-SignalRGB","description":"SignalRGB integration","html_url":"https://github.com/FocusRGB/FocusRGB-Plugin-SignalRGB","owner":{"login":"FocusRGB"}}
                """ + (includeUntrusted ? """
                  ,{"name":"FocusRGB-Plugin-Evil","description":null,"html_url":"https://github.com/elsewhere/FocusRGB-Plugin-Evil","owner":{"login":"elsewhere"}}
                """ : string.Empty) + "]}";

            private string ReleaseJson() => $$"""
                {"draft":{{draft.ToString().ToLowerInvariant()}},"prerelease":{{prerelease.ToString().ToLowerInvariant()}},"html_url":"https://github.com/FocusRGB/FocusRGB-Plugin-SignalRGB/releases/tag/v1.0.0","assets":[
                  {"name":"FocusRGB-Plugin-SignalRGB-win-x64.zip","browser_download_url":"https://github.com/assets/plugin.zip"},
                  {"name":"focusrgb-plugin.json","browser_download_url":"https://github.com/assets/focusrgb-plugin.json"},
                  {"name":"SHA256SUMS","browser_download_url":"https://github.com/assets/SHA256SUMS"}
                ]}
                """;

            private string ManifestJson()
            {
                var entryPoint = runtime == "node" ? "plugin.js" : "FocusRgb.Plugin.SignalRgb.dll";
                var runtimeProperty = runtime is null ? string.Empty : $",\"runtime\":\"{runtime}\"";
                return $$"""
                    {"schemaVersion":1,"id":"signalrgb","version":"1.0.0","contractVersion":"{{contractVersion}}","entryPoint":"{{entryPoint}}"{{runtimeProperty}},"operatingSystems":["{{CurrentOperatingSystem()}}"],"architectures":["{{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}}"],"roles":["integration","output"],"resources":["process.read","filesystem.read"],"license":"MIT","source":"https://github.com/FocusRGB/FocusRGB-Plugin-SignalRGB"}
                    """;
            }

            private static string CurrentOperatingSystem() => OperatingSystem.IsWindows()
                ? "windows"
                : OperatingSystem.IsMacOS() ? "macos" : "linux";
        }

        private sealed class OfflineHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken) => throw new HttpRequestException("offline");
        }

        private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;

            public void Advance(TimeSpan duration)
            {
                now += duration;
            }
        }
    }
}
