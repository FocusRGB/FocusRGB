using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FocusRgb.App.Plugins
{
    /// <summary>Downloads bounded plugin release assets without trusting repository-provided file names.</summary>
    public sealed class PluginPackageDownloader
    {
        private const long MaximumMetadataBytes = 1024 * 1024;
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly HttpClient _httpClient;
        private readonly string _temporaryRoot;

        public PluginPackageDownloader(HttpClient httpClient, string temporaryRoot)
        {
            ArgumentNullException.ThrowIfNull(httpClient);
            ArgumentException.ThrowIfNullOrWhiteSpace(temporaryRoot);
            _httpClient = httpClient;
            _temporaryRoot = Path.GetFullPath(temporaryRoot);
        }

        public async Task<DownloadedPluginPackage> DownloadOfficialAsync(
            OfficialPluginListing listing,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(listing);
            var directory = CreateTemporaryDirectory();
            try
            {
                var archive = Path.Combine(directory, "plugin.zip");
                var manifest = Path.Combine(directory, PluginPackage.ManifestFileName);
                var checksums = Path.Combine(directory, "SHA256SUMS");
                await DownloadAsync(listing.PackageUrl, archive, PluginPackageInstaller.MaximumExpandedBytes,
                    cancellationToken).ConfigureAwait(false);
                await DownloadAsync(listing.ManifestUrl, manifest, MaximumMetadataBytes,
                    cancellationToken).ConfigureAwait(false);
                await DownloadAsync(listing.ChecksumsUrl, checksums, MaximumMetadataBytes,
                    cancellationToken).ConfigureAwait(false);
                return new DownloadedPluginPackage(directory, archive, manifest, checksums);
            }
            catch
            {
                DeleteWithoutThrowing(directory);
                throw;
            }
        }

        public async Task<DownloadedPluginPackage> DownloadCustomGitHubAsync(
            string repository,
            CancellationToken cancellationToken = default)
        {
            var (owner, name) = ParseRepository(repository);
            using var request = CreateRequest(new Uri(
                $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}/releases/latest"));
            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            EnsureHttps(response.RequestMessage?.RequestUri ?? request.RequestUri);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(
                stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("GitHub returned an empty release response.");
            var packages = release.Assets.Where(asset =>
                asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
            if (packages.Count != 1)
                throw new InvalidDataException("The custom repository's latest release must contain exactly one zip asset.");

            var directory = CreateTemporaryDirectory();
            try
            {
                var archive = Path.Combine(directory, "plugin.zip");
                await DownloadAsync(packages[0].DownloadUrl, archive, PluginPackageInstaller.MaximumExpandedBytes,
                    cancellationToken).ConfigureAwait(false);
                return new DownloadedPluginPackage(
                    directory, archive, source: $"https://github.com/{owner}/{name}");
            }
            catch
            {
                DeleteWithoutThrowing(directory);
                throw;
            }
        }

        private async Task DownloadAsync(
            string address,
            string destination,
            long maximumBytes,
            CancellationToken cancellationToken)
        {
            using var request = CreateRequest(EnsureHttps(address));
            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            EnsureHttps(response.RequestMessage?.RequestUri ?? request.RequestUri);
            if (response.Content.Headers.ContentLength > maximumBytes)
                throw new InvalidDataException("The plugin download exceeds the size limit.");
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(
                destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
            var buffer = new byte[81920];
            long copied = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (read > maximumBytes - copied)
                    throw new InvalidDataException("The plugin download exceeds the size limit.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                copied += read;
            }
        }

        private string CreateTemporaryDirectory()
        {
            Directory.CreateDirectory(_temporaryRoot);
            var directory = Path.Combine(_temporaryRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static HttpRequestMessage CreateRequest(Uri uri)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd("FocusRGB/0.1");
            return request;
        }

        private static (string Owner, string Repository) ParseRepository(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            var path = value.Trim().TrimEnd('/');
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
            {
                if (uri.Scheme != Uri.UriSchemeHttps
                    || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Enter an HTTPS github.com repository URL.", nameof(value));
                path = uri.AbsolutePath.Trim('/');
            }
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || parts.Any(part => part is "." or ".." || part.Any(character =>
                    !(char.IsLetterOrDigit(character) || character is '-' or '_' or '.'))))
                throw new ArgumentException("Enter a GitHub repository as owner/repo or its HTTPS URL.", nameof(value));
            return (parts[0], parts[1]);
        }

        private static Uri EnsureHttps(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? EnsureHttps(uri)
            : throw new InvalidDataException("The plugin release contains an invalid URL.");

        private static Uri EnsureHttps(Uri? uri) => uri?.Scheme == Uri.UriSchemeHttps
            ? uri
            : throw new InvalidDataException("Plugin downloads require HTTPS.");

        private static void DeleteWithoutThrowing(string directory)
        {
            try { Directory.Delete(directory, true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }

        private sealed class GitHubRelease
        {
            public List<GitHubAsset> Assets { get; init; } = [];
        }

        private sealed class GitHubAsset
        {
            public required string Name { get; init; }
            [JsonPropertyName("browser_download_url")]
            public required string DownloadUrl { get; init; }
        }
    }

    /// <summary>Owns downloaded release files and deletes them when the install flow ends.</summary>
    public sealed class DownloadedPluginPackage : IDisposable
    {
        private readonly bool _deleteOnDispose;
        internal DownloadedPluginPackage(
            string directory,
            string archivePath,
            string? manifestPath = null,
            string? checksumsPath = null,
            string? source = null,
            bool deleteOnDispose = true)
        {
            Directory = directory;
            ArchivePath = archivePath;
            ManifestPath = manifestPath;
            ChecksumsPath = checksumsPath;
            Source = source;
            _deleteOnDispose = deleteOnDispose;
        }

        public string Directory { get; }
        public string ArchivePath { get; }
        public string? ManifestPath { get; }
        public string? ChecksumsPath { get; }
        public string? Source { get; }

        public void Dispose()
        {
            if (!_deleteOnDispose) return;
            try { System.IO.Directory.Delete(Directory, true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}
