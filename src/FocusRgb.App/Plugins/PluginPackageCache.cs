using System.Security.Cryptography;
using System.Text.Json;

namespace FocusRgb.App.Plugins
{
    /// <summary>Retains verified release assets for a separate, offline-capable install action.</summary>
    public sealed class PluginPackageCache(string directory)
    {
        private readonly string _directory = Path.GetFullPath(directory);

        public bool Contains(OfficialPluginListing listing) => Open(listing) is not null;

        public DownloadedPluginPackage? Open(OfficialPluginListing listing)
        {
            var path = PackageDirectory(listing);
            var archive = Path.Combine(path, "plugin.zip");
            var manifest = Path.Combine(path, PluginPackage.ManifestFileName);
            var checksums = Path.Combine(path, "SHA256SUMS");
            return File.Exists(archive) && File.Exists(manifest) && File.Exists(checksums)
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
        }

        private string PackageDirectory(OfficialPluginListing listing) => Path.Combine(_directory,
            Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(listing))));
    }
}
