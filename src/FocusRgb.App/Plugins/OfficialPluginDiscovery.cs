using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using FocusRgb.Core.Protocol;

namespace FocusRgb.App.Plugins
{
    /// <summary>Loads the official plugin catalog only when the Plugins page requests it.</summary>
    public sealed class OfficialPluginDiscovery : IDisposable
    {
        public const string OfficialOrganization = "FocusRGB";
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly HttpClient _httpClient;
        private readonly string _cachePath;
        private readonly TimeProvider _timeProvider;
        private readonly SemaphoreSlim _refreshGate = new(1, 1);
        private DateTimeOffset? _rateLimitResetAt;

        public OfficialPluginDiscovery(HttpClient httpClient, string cachePath, TimeProvider? timeProvider = null)
        {
            ArgumentNullException.ThrowIfNull(httpClient);
            ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
            _httpClient = httpClient;
            _cachePath = Path.GetFullPath(cachePath);
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public async Task<OfficialPluginCatalogResult> DiscoverAsync(
            bool refresh = false,
            bool includePrereleases = false,
            CancellationToken cancellationToken = default)
        {
            var cachePath = includePrereleases ? _cachePath + ".beta" : _cachePath;
            await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var cached = await ReadCacheAsync(cachePath, cancellationToken).ConfigureAwait(false);
                if (!refresh && cached is not null
                    && _timeProvider.GetUtcNow() - cached.RefreshedAt < CacheLifetime)
                {
                    return OfficialPluginCatalogResult.Cached(
                        cached.RefreshedAt, cached.Items, cached.Items.Count == 0
                            ? "The cached catalog contains no compatible release listings. Refresh to check GitHub."
                            : "Official plugin list loaded from cache.");
                }

                try
                {
                    var items = await FetchAsync(includePrereleases, cancellationToken).ConfigureAwait(false);
                    var refreshedAt = _timeProvider.GetUtcNow();
                    var warning = await WriteCacheAsync(cachePath, refreshedAt, items, cancellationToken).ConfigureAwait(false);
                    return OfficialPluginCatalogResult.Fresh(refreshedAt, items, warning is null
                        ? items.Count == 0
                            ? "No installable official releases were found. Repositories need a supported manifest, ZIP and SHA256SUMS; enable beta plugins for prereleases."
                            : "Official plugin list refreshed."
                        : $"Official plugin list refreshed, but its cache could not be saved: {warning}");
                }
                catch (Exception exception) when (IsOfflineFailure(exception, cancellationToken))
                {
                    return cached is null
                        ? OfficialPluginCatalogResult.Unavailable($"Official plugins are unavailable: {exception.Message}")
                        : OfficialPluginCatalogResult.Cached(cached.RefreshedAt, cached.Items,
                            $"GitHub is unavailable. Showing the cached official plugin list: {exception.Message}");
                }
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        public void Dispose()
        {
            _refreshGate.Dispose();
        }

        private async Task<List<OfficialPluginListing>> FetchAsync(bool includePrereleases, CancellationToken cancellationToken)
        {
            var query = Uri.EscapeDataString($"topic:focusrgb-plugin org:{OfficialOrganization}");
            var search = await GetJsonAsync<RepositorySearch>(
                new Uri($"https://api.github.com/search/repositories?q={query}&per_page=50"),
                cancellationToken).ConfigureAwait(false);
            var listings = new List<OfficialPluginListing>();
            foreach (var repository in search.Items.Where(repository =>
                string.Equals(repository.Owner.Login, OfficialOrganization, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var listing = await FetchRepositoryAsync(repository, includePrereleases, cancellationToken).ConfigureAwait(false);
                    if (listing is not null) listings.Add(listing);
                }
                catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.NotFound)
                {
                    // Repositories without a complete eligible release are not installable catalog entries.
                }
                catch (JsonException)
                {
                    // One invalid manifest must not hide healthy official plugins.
                }
                catch (InvalidDataException)
                {
                    // One unsafe release must not hide healthy official plugins.
                }
            }
            return listings;
        }

        private async Task<OfficialPluginListing?> FetchRepositoryAsync(
            GitHubRepository repository,
            bool includePrereleases,
            CancellationToken cancellationToken)
        {
            var name = Uri.EscapeDataString(repository.Name);
            var releaseRoot = $"https://api.github.com/repos/{OfficialOrganization}/{name}/releases";
            var releases = includePrereleases
                ? await GetJsonAsync<List<GitHubRelease>>(new Uri(releaseRoot + "?per_page=20"), cancellationToken).ConfigureAwait(false)
                : [await GetJsonAsync<GitHubRelease>(new Uri(releaseRoot + "/latest"), cancellationToken).ConfigureAwait(false)];
            foreach (var release in releases.Where(release => !release.Draft && (includePrereleases || !release.Prerelease)))
            {
                var listing = await ReadListingAsync(repository, release, cancellationToken).ConfigureAwait(false);
                if (listing is not null) return listing;
            }
            return null;
        }

        private async Task<OfficialPluginListing?> ReadListingAsync(
            GitHubRepository repository, GitHubRelease release, CancellationToken cancellationToken)
        {
            var packages = release.Assets.Where(asset =>
                asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
            var manifestAsset = release.Assets.FirstOrDefault(asset =>
                asset.Name.Equals(PluginPackage.ManifestFileName, StringComparison.Ordinal));
            var checksums = release.Assets.FirstOrDefault(asset =>
                asset.Name.Equals("SHA256SUMS", StringComparison.Ordinal));
            if (packages.Count == 0 || manifestAsset is null || checksums is null) return null;
            var package = packages[0];

            var manifest = await GetJsonAsync<PluginManifest>(
                RequireHttps(manifestAsset.DownloadUrl), cancellationToken).ConfigureAwait(false);
            if (PluginPackage.ValidateManifest(manifest, manifest.Id) is not null) return null;
            return new OfficialPluginListing
            {
                Id = manifest.Id!,
                Name = DisplayName(repository.Name),
                Version = manifest.Version!,
                Description = repository.Description,
                RepositoryUrl = RequireHttps(repository.HtmlUrl).AbsoluteUri,
                ReleaseUrl = RequireHttps(release.HtmlUrl).AbsoluteUri,
                PackageUrl = RequireHttps(package.DownloadUrl).AbsoluteUri,
                PackageFileName = package.Name,
                PackageCandidates = packages.Select(asset => new OfficialPluginAsset
                {
                    Name = asset.Name,
                    Url = RequireHttps(asset.DownloadUrl).AbsoluteUri
                }).ToList(),
                ManifestUrl = RequireHttps(manifestAsset.DownloadUrl).AbsoluteUri,
                ChecksumsUrl = RequireHttps(checksums.DownloadUrl).AbsoluteUri,
                ContractVersion = manifest.ContractVersion!,
                Resources = manifest.Resources!,
                OperatingSystems = manifest.OperatingSystems!,
                Architectures = manifest.Architectures!,
                IsCompatible = PluginPackage.SupportsCurrentPlatform(manifest)
            };
        }

        private async Task<T> GetJsonAsync<T>(Uri uri, CancellationToken cancellationToken)
        {
            if (_rateLimitResetAt is { } resetAt && resetAt > _timeProvider.GetUtcNow())
                throw new HttpRequestException($"GitHub rate limit reached until {resetAt:u}.");
            using var request = new HttpRequestMessage(HttpMethod.Get, RequireHttps(uri));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd("FocusRGB/0.1");
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues)
                && long.TryParse(remainingValues.FirstOrDefault(), out var remaining)
                && remaining == 0
                && response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues)
                && long.TryParse(resetValues.FirstOrDefault(), out var resetSeconds))
            {
                _rateLimitResetAt = DateTimeOffset.FromUnixTimeSeconds(resetSeconds);
            }
            response.EnsureSuccessStatusCode();
            RequireHttps(response.RequestMessage?.RequestUri ?? uri);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new JsonException("GitHub returned an empty response.");
        }

        private static async Task<DiscoveryCache?> ReadCacheAsync(string cachePath, CancellationToken cancellationToken)
        {
            try
            {
                await using var stream = File.OpenRead(cachePath);
                return await JsonSerializer.DeserializeAsync<DiscoveryCache>(
                    stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                return null;
            }
        }

        private static async Task<string?> WriteCacheAsync(
            string cachePath,
            DateTimeOffset refreshedAt,
            List<OfficialPluginListing> items,
            CancellationToken cancellationToken)
        {
            var temporaryPath = cachePath + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                await using (var stream = File.Create(temporaryPath))
                {
                    await JsonSerializer.SerializeAsync(stream, new DiscoveryCache
                    {
                        RefreshedAt = refreshedAt,
                        Items = items
                    }, JsonOptions, cancellationToken).ConfigureAwait(false);
                }
                File.Move(temporaryPath, cachePath, true);
                return null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                try { File.Delete(temporaryPath); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
                return exception.Message;
            }
        }

        private static bool IsOfflineFailure(Exception exception, CancellationToken cancellationToken) =>
            exception is HttpRequestException or IOException or JsonException
            || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested;

        private static Uri RequireHttps(string value) =>
            Uri.TryCreate(value, UriKind.Absolute, out var uri)
                ? RequireHttps(uri)
                : throw new InvalidDataException("Plugin discovery received an invalid URL.");

        private static Uri RequireHttps(Uri uri) => uri.Scheme == Uri.UriSchemeHttps
            ? uri
            : throw new InvalidDataException("Plugin discovery only accepts HTTPS URLs.");

        private static string DisplayName(string repositoryName)
        {
            const string prefix = "FocusRGB-Plugin-";
            return repositoryName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? repositoryName[prefix.Length..]
                : repositoryName;
        }

        private sealed class DiscoveryCache
        {
            public DateTimeOffset RefreshedAt { get; init; }
            public List<OfficialPluginListing> Items { get; init; } = [];
        }

        private sealed class RepositorySearch
        {
            public List<GitHubRepository> Items { get; init; } = [];
        }

        private sealed class GitHubRepository
        {
            public required string Name { get; init; }
            public string? Description { get; init; }
            [JsonPropertyName("html_url")]
            public required string HtmlUrl { get; init; }
            public required GitHubOwner Owner { get; init; }
        }

        private sealed class GitHubOwner
        {
            public required string Login { get; init; }
        }

        private sealed class GitHubRelease
        {
            public bool Draft { get; init; }
            public bool Prerelease { get; init; }
            [JsonPropertyName("html_url")]
            public required string HtmlUrl { get; init; }
            public List<GitHubAsset> Assets { get; init; } = [];
        }

        private sealed class GitHubAsset
        {
            public required string Name { get; init; }
            [JsonPropertyName("browser_download_url")]
            public required string DownloadUrl { get; init; }
        }
    }

    /// <summary>Describes one verified official release before its package is downloaded.</summary>
    public sealed class OfficialPluginListing
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Version { get; init; }
        public string? Description { get; init; }
        public required string RepositoryUrl { get; init; }
        public required string ReleaseUrl { get; init; }
        public required string PackageUrl { get; init; }
        public required string PackageFileName { get; init; }
        public IReadOnlyList<OfficialPluginAsset> PackageCandidates { get; init; } = [];
        public required string ManifestUrl { get; init; }
        public required string ChecksumsUrl { get; init; }
        public required string ContractVersion { get; init; }
        public IReadOnlyList<string> Resources { get; init; } = [];
        public IReadOnlyList<string> OperatingSystems { get; init; } = [];
        public IReadOnlyList<string> Architectures { get; init; } = [];
        public bool IsCompatible { get; init; }
    }

    /// <summary>Identifies one zip asset advertised by an official release.</summary>
    public sealed class OfficialPluginAsset
    {
        public required string Name { get; init; }
        public required string Url { get; init; }
    }

    /// <summary>Returns a fresh, cached, or unavailable official plugin catalog.</summary>
    public sealed class OfficialPluginCatalogResult
    {
        public IReadOnlyList<OfficialPluginListing> Items { get; private init; } = [];
        public DateTimeOffset? RefreshedAt { get; private init; }
        public bool IsFromCache { get; private init; }
        public required string Status { get; init; }

        public static OfficialPluginCatalogResult Fresh(
            DateTimeOffset refreshedAt,
            IReadOnlyList<OfficialPluginListing> items,
            string status) => new() { RefreshedAt = refreshedAt, Items = items, Status = status };

        public static OfficialPluginCatalogResult Cached(
            DateTimeOffset refreshedAt,
            IReadOnlyList<OfficialPluginListing> items,
            string status) => new()
            {
                RefreshedAt = refreshedAt,
                Items = items,
                IsFromCache = true,
                Status = status
            };

        public static OfficialPluginCatalogResult Unavailable(string status) => new() { Status = status };
    }
}
