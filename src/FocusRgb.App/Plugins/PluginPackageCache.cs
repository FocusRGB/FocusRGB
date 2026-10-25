using System.Security.Cryptography;
using System.Text;

namespace FocusRgb.App.Plugins
{
    /// <summary>Retains verified release assets for a separate, offline-capable install action.</summary>
    public sealed class PluginPackageCache(string directory)
    {
        private readonly string _directory = Path.GetFullPath(directory);
        private const int MaximumEntries = 32;
        private static readonly TimeSpan MaximumAge = TimeSpan.FromDays(30);

        public bool Contains(OfficialPluginListing listing) => Open(listing) is not null;

        public DownloadedPluginPackage? Open(OfficialPluginListing listing)
        {
            var path = PackageDirectory(listing);
            var archive = Path.Combine(path, "plugin.zip");
            var manifest = Path.Combine(path, PluginPackage.ManifestFileName);
            var checksums = Path.Combine(path, "SHA256SUMS");
            return HasContent(archive) && HasContent(manifest) && HasContent(checksums)
                ? new DownloadedPluginPackage(path, archive, manifest, checksums, deleteOnDispose: false)
                : null;
        }

        public async Task DownloadAsync(
            OfficialPluginListing listing,
            PluginPackageDownloader downloader,
            PluginPackageInstaller installer,
            CancellationToken cancellationToken = default)
        {
            if (Contains(listing)) return;
            Invalidate(listing);
            using var download = await downloader.DownloadOfficialAsync(listing, cancellationToken).ConfigureAwait(false);
            var staged = await installer.StageOfficialAsync(listing,
                download.ArchivePath, download.ManifestPath!, download.ChecksumsPath!, cancellationToken).ConfigureAwait(false);
            if (!staged.IsSuccess)
                throw new InvalidDataException(staged.ErrorMessage ?? "The release package failed verification.");
            installer.Discard(staged.StagedPackage!);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_directory);
            var target = PackageDirectory(listing);
            try
            {
                Directory.Move(download.Directory, target);
            }
            catch (IOException) when (Contains(listing))
            {
                // Another completed download won the atomic cache insertion.
            }
            Prune();
        }

        public async Task<PluginArchiveResult> StageAsync(
            OfficialPluginListing listing,
            PluginPackageInstaller installer,
            CancellationToken cancellationToken = default)
        {
            using var cached = Open(listing);
            if (cached is null)
            {
                Invalidate(listing);
                return PluginArchiveResult.Fail("cache-missing", "Download this release before installing it.");
            }
            var staged = await installer.StageOfficialAsync(listing, cached.ArchivePath,
                cached.ManifestPath!, cached.ChecksumsPath!, cancellationToken).ConfigureAwait(false);
            if (!staged.IsSuccess)
            {
                Invalidate(listing);
            }
            return staged;
        }

        public void Invalidate(OfficialPluginListing listing) => DeleteEntry(new DirectoryInfo(PackageDirectory(listing)));

        /// <summary>Keeps at most 32 releases and removes releases cached over 30 days ago.</summary>
        public void Prune()
        {
            if (!System.IO.Directory.Exists(_directory)) return;
            var entries = new DirectoryInfo(_directory).EnumerateDirectories()
                .Where(entry => entry.Name.Length == 64 && entry.Name.All(Uri.IsHexDigit))
                .OrderByDescending(entry => entry.LastWriteTimeUtc).ToArray();
            var cutoff = DateTime.UtcNow - MaximumAge;
            for (var index = 0; index < entries.Length; index++)
            {
                if (index >= MaximumEntries || entries[index].LastWriteTimeUtc < cutoff)
                {
                    DeleteEntry(entries[index]);
                }
            }
        }

        private static bool HasContent(string path)
        {
            try
            {
                var file = new FileInfo(path);
                return file.Exists && file.Length > 0;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static void DeleteEntry(DirectoryInfo entry)
        {
            if (!entry.Exists) return;
            entry.Delete(recursive: (entry.Attributes & FileAttributes.ReparsePoint) == 0);
        }

        private string PackageDirectory(OfficialPluginListing listing) => Path.Combine(_directory,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(listing.Id + "\n" + listing.Version))));
    }
}
