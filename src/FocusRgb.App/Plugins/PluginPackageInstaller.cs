using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FocusRgb.Core.Protocol;

namespace FocusRgb.App.Plugins
{
    /// <summary>Validates, stages, swaps, and removes local plugin packages without touching user settings.</summary>
    public sealed class PluginPackageInstaller
    {
        public const int MaximumEntryCount = 512;
        public const long MaximumExpandedBytes = 256L * 1024 * 1024;
        private const long MaximumReleaseMetadataBytes = 1024 * 1024;
        private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };
        private readonly string _packagesDirectory;
        private readonly string _stagingDirectory;

        public PluginPackageInstaller(string packagesDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(packagesDirectory);
            _packagesDirectory = Path.GetFullPath(packagesDirectory);
            _stagingDirectory = Path.Combine(_packagesDirectory, ".staging");
        }

        public Task<PluginArchiveResult> InspectAsync(
            string archivePath,
            string? expectedPluginId = null,
            CancellationToken cancellationToken = default) =>
            ReadArchiveAsync(archivePath, expectedPluginId, null, false, cancellationToken);

        public Task<PluginArchiveResult> StageAsync(
            string archivePath,
            string expectedPluginId,
            string expectedSha256,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(expectedPluginId);
            ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);
            return ReadArchiveAsync(
                archivePath, expectedPluginId, expectedSha256, true, cancellationToken);
        }

        public async Task<PluginArchiveResult> StageOfficialAsync(
            OfficialPluginListing listing,
            string archivePath,
            string manifestPath,
            string checksumsPath,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(listing);
            try
            {
                if (listing.PackageCandidates.Count != 1)
                    return PluginArchiveResult.Fail(
                        "package-ambiguous", "The release must contain exactly one plugin zip.");
                var package = listing.PackageCandidates[0];
                if (!package.Name.Equals(listing.PackageFileName, StringComparison.Ordinal)
                    || !package.Url.Equals(listing.PackageUrl, StringComparison.Ordinal)
                    || !IsHttps(package.Url) || !IsHttps(listing.ManifestUrl))
                {
                    return PluginArchiveResult.Fail(
                        "listing-invalid", "The selected release assets do not match the official listing.");
                }

                var checksums = await ReadChecksumsAsync(checksumsPath, cancellationToken).ConfigureAwait(false);
                if (!checksums.TryGetValue(package.Name, out var packageHash))
                    return PluginArchiveResult.Fail(
                        "checksum-missing", "SHA256SUMS does not contain the plugin zip.");
                if (!checksums.TryGetValue(PluginPackage.ManifestFileName, out var manifestHash))
                    return PluginArchiveResult.Fail(
                        "checksum-missing", $"SHA256SUMS does not contain {PluginPackage.ManifestFileName}.");

                await using var manifestStream = new FileStream(Path.GetFullPath(manifestPath), FileMode.Open,
                    FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (manifestStream.Length > MaximumReleaseMetadataBytes)
                    throw new InvalidDataException("The release manifest exceeds the metadata size limit.");
                var actualManifestHash = Convert.ToHexString(
                    await SHA256.HashDataAsync(manifestStream, cancellationToken).ConfigureAwait(false))
                    .ToLowerInvariant();
                if (!actualManifestHash.Equals(manifestHash, StringComparison.OrdinalIgnoreCase))
                    return PluginArchiveResult.Fail(
                        "checksum-mismatch", $"The {PluginPackage.ManifestFileName} SHA-256 does not match.");
                manifestStream.Position = 0;
                var releaseManifest = await JsonSerializer.DeserializeAsync<PluginManifest>(
                    manifestStream, PluginJson.Options, cancellationToken).ConfigureAwait(false);
                var manifestError = PluginPackage.ValidateManifest(releaseManifest, listing.Id);
                if (manifestError is not null || releaseManifest!.Version != listing.Version)
                    return PluginArchiveResult.Fail(
                        "listing-mismatch", "The release manifest does not match the listed plugin ID and version.");
                if (!releaseManifest.Resources!.Order(StringComparer.Ordinal)
                    .SequenceEqual(listing.Resources.Order(StringComparer.Ordinal), StringComparer.Ordinal))
                {
                    return PluginArchiveResult.Fail("listing-mismatch",
                        "The verified release manifest resources do not match the permissions shown for consent.");
                }

                var staged = await StageAsync(
                    archivePath, listing.Id, packageHash, cancellationToken).ConfigureAwait(false);
                if (!staged.IsSuccess) return staged;
                if (staged.ManifestSha256!.Equals(actualManifestHash, StringComparison.OrdinalIgnoreCase))
                    return staged;
                DeleteStagingWithoutThrowing(staged.StagedPackage!.Directory);
                return PluginArchiveResult.Fail(
                    "listing-mismatch", "The zip manifest does not match the verified release manifest.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or InvalidDataException or JsonException or NotSupportedException)
            {
                return PluginArchiveResult.Fail("release-invalid", exception.Message);
            }
        }

        /// <summary>Swaps a validated staged package.</summary>
        /// <remarks>The caller must stop the existing plugin process before invoking this method.</remarks>
        public PluginFileOperationResult Commit(StagedPluginPackage stagedPackage)
        {
            ArgumentNullException.ThrowIfNull(stagedPackage);
            var stagedDirectory = Path.GetFullPath(stagedPackage.Directory);
            if (!IsChildOf(stagedDirectory, _stagingDirectory) || !Directory.Exists(stagedDirectory))
                return PluginFileOperationResult.Fail("stage-invalid", "The staged package is missing or outside the staging directory.");

            Directory.CreateDirectory(_packagesDirectory);
            Directory.CreateDirectory(_stagingDirectory);
            var target = Path.Combine(_packagesDirectory, stagedPackage.Manifest.Id!);
            var backup = Path.Combine(_stagingDirectory,
                $".previous-{stagedPackage.Manifest.Id}-{Guid.NewGuid():N}");
            var movedPrevious = false;
            try
            {
                if (Directory.Exists(target))
                {
                    if (File.GetAttributes(target).HasFlag(FileAttributes.ReparsePoint))
                        return PluginFileOperationResult.Fail("package-unsafe", "The existing plugin directory is a reparse point.");
                    Directory.Move(target, backup);
                    movedPrevious = true;
                }
                Directory.Move(stagedDirectory, target);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                try
                {
                    if (movedPrevious && !Directory.Exists(target)) Directory.Move(backup, target);
                }
                catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException)
                {
                    return PluginFileOperationResult.Fail("swap-failed",
                        $"The package swap failed and the previous package could not be restored. "
                        + $"Reinstall the plugin before restarting FocusRGB: {rollback.Message}");
                }
                return PluginFileOperationResult.Fail("swap-failed", exception.Message);
            }

            if (!movedPrevious) return PluginFileOperationResult.Ok();
            try
            {
                DeleteTree(backup);
                return PluginFileOperationResult.Ok();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return PluginFileOperationResult.Ok(
                    $"The new package is installed, but the previous package could not be removed from '{backup}': {exception.Message}");
            }
        }

        public void Discard(StagedPluginPackage stagedPackage)
        {
            ArgumentNullException.ThrowIfNull(stagedPackage);
            var directory = Path.GetFullPath(stagedPackage.Directory);
            if (IsChildOf(directory, _stagingDirectory)) DeleteStagingWithoutThrowing(directory);
        }

        /// <summary>Removes one package without changing its saved settings.</summary>
        /// <remarks>The caller must stop the plugin process before invoking this method.</remarks>
        public PluginFileOperationResult Uninstall(string pluginId)
        {
            if (!IsPluginId(pluginId))
                return PluginFileOperationResult.Fail("plugin-invalid", "The plugin ID is invalid.");
            var target = Path.Combine(_packagesDirectory, pluginId);
            if (!Directory.Exists(target))
                return PluginFileOperationResult.Fail("plugin-absent", $"Package '{pluginId}' is not installed.");
            if (File.GetAttributes(target).HasFlag(FileAttributes.ReparsePoint))
                return PluginFileOperationResult.Fail("package-unsafe", "The plugin directory is a reparse point.");

            Directory.CreateDirectory(_stagingDirectory);
            var removed = Path.Combine(_stagingDirectory, $".removed-{pluginId}-{Guid.NewGuid():N}");
            try
            {
                Directory.Move(target, removed);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return PluginFileOperationResult.Fail("uninstall-failed", exception.Message);
            }

            try
            {
                DeleteTree(removed);
                return PluginFileOperationResult.Ok();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return PluginFileOperationResult.Ok(
                    $"The plugin is uninstalled, but staged cleanup failed at '{removed}': {exception.Message}");
            }
        }

        private async Task<PluginArchiveResult> ReadArchiveAsync(
            string archivePath,
            string? expectedPluginId,
            string? expectedSha256,
            bool extract,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
            string? stagedDirectory = null;
            try
            {
                await using var stream = new FileStream(Path.GetFullPath(archivePath), FileMode.Open, FileAccess.Read,
                    FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (stream.Length > MaximumExpandedBytes)
                    return PluginArchiveResult.Fail("package-too-large", "The plugin archive exceeds the size limit.");
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false))
                    .ToLowerInvariant();
                if (expectedSha256 is not null && !IsSha256(expectedSha256))
                    return PluginArchiveResult.Fail("checksum-invalid", "The expected SHA-256 is invalid.");
                if (expectedSha256 is not null && !hash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                    return PluginArchiveResult.Fail("checksum-mismatch", "The plugin archive SHA-256 does not match.");

                stream.Position = 0;
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read, true);
                var validatedManifest = await ValidateArchiveAsync(
                    archive, expectedPluginId, cancellationToken).ConfigureAwait(false);
                if (!extract)
                    return PluginArchiveResult.Ok(
                        validatedManifest.Manifest, hash, validatedManifest.Sha256);

                stagedDirectory = Path.Combine(
                    _stagingDirectory, $"{validatedManifest.Manifest.Id}-{Guid.NewGuid():N}");
                Directory.CreateDirectory(stagedDirectory);
                await ExtractAsync(archive, stagedDirectory, cancellationToken).ConfigureAwait(false);
                var loaded = PluginPackage.LoadDirectory(stagedDirectory, validatedManifest.Manifest.Id!);
                if (!loaded.IsSuccess)
                    throw new InvalidDataException(loaded.ErrorMessage);
                return PluginArchiveResult.Ok(validatedManifest.Manifest, hash, validatedManifest.Sha256,
                    new StagedPluginPackage(stagedDirectory, validatedManifest.Manifest));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                DeleteStagingWithoutThrowing(stagedDirectory);
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or InvalidDataException or JsonException or NotSupportedException)
            {
                DeleteStagingWithoutThrowing(stagedDirectory);
                return PluginArchiveResult.Fail("package-invalid", exception.Message);
            }
        }

        private static async Task<ValidatedManifest> ValidateArchiveAsync(
            ZipArchive archive,
            string? expectedPluginId,
            CancellationToken cancellationToken)
        {
            if (archive.Entries.Count > MaximumEntryCount)
                throw new InvalidDataException($"Plugin packages may contain at most {MaximumEntryCount} entries.");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long expandedBytes = 0;
            ZipArchiveEntry? manifestEntry = null;
            foreach (var entry in archive.Entries)
            {
                ValidateEntry(entry);
                var normalized = entry.FullName.Replace('\\', '/').TrimEnd('/');
                if (normalized.Length > 0 && !paths.Add(normalized))
                    throw new InvalidDataException($"The package contains the duplicate path '{entry.FullName}'.");
                if (entry.Length > MaximumExpandedBytes - expandedBytes)
                    throw new InvalidDataException("The expanded plugin package exceeds the size limit.");
                expandedBytes += entry.Length;
                if (entry.FullName.Equals(PluginPackage.ManifestFileName, StringComparison.Ordinal))
                    manifestEntry = entry;
            }
            if (manifestEntry is null)
                throw new InvalidDataException($"The package root must contain {PluginPackage.ManifestFileName}.");
            var manifestBytes = await ReadEntryAsync(
                manifestEntry, MaximumReleaseMetadataBytes, cancellationToken).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<PluginManifest>(manifestBytes, PluginJson.Options);
            var error = PluginPackage.ValidateManifest(manifest, expectedPluginId ?? manifest?.Id);
            if (error is not null) throw new InvalidDataException(error);
            if (!PluginPackage.SupportsCurrentPlatform(manifest!))
                throw new InvalidDataException("The package does not support this platform.");
            var hash = Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant();
            return new ValidatedManifest(manifest!, hash);
        }

        private static async Task<byte[]> ReadEntryAsync(
            ZipArchiveEntry entry,
            long maximumBytes,
            CancellationToken cancellationToken)
        {
            await using var input = entry.Open();
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (read > maximumBytes - output.Length)
                    throw new InvalidDataException($"{entry.FullName} exceeds the metadata size limit.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
            return output.ToArray();
        }

        private static async Task ExtractAsync(
            ZipArchive archive,
            string destination,
            CancellationToken cancellationToken)
        {
            var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
            long copiedBytes = 0;
            foreach (var entry in archive.Entries)
            {
                var relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar);
                var path = Path.GetFullPath(Path.Combine(destination, relativePath));
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"The package path '{entry.FullName}' leaves the staging directory.");
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    Directory.CreateDirectory(path);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await using var input = entry.Open();
                await using var output = new FileStream(
                    path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
                var buffer = new byte[81920];
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    if (read > MaximumExpandedBytes - copiedBytes)
                        throw new InvalidDataException("The expanded plugin package exceeds the size limit.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    copiedBytes += read;
                }
            }
        }

        private static void ValidateEntry(ZipArchiveEntry entry)
        {
            var path = entry.FullName.Replace('\\', '/');
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains(':')
                || Path.IsPathRooted(entry.FullName)
                || segments.Contains("..", StringComparer.Ordinal)
                || segments.Any(IsUnsafeWindowsSegment))
            {
                throw new InvalidDataException($"The package path '{entry.FullName}' is unsafe.");
            }
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType == 0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"The package entry '{entry.FullName}' is a link or reparse point.");
        }

        private static bool IsUnsafeWindowsSegment(string segment)
        {
            if (segment.EndsWith('.') || segment.EndsWith(' ')) return true;
            var separator = segment.IndexOf('.');
            var stem = (separator < 0 ? segment : segment[..separator]).TrimEnd(' ', '.');
            return WindowsReservedNames.Contains(stem);
        }

        private static bool IsSha256(string value) => value.Length == 64
            && value.All(character => character is >= '0' and <= '9'
                or >= 'a' and <= 'f' or >= 'A' and <= 'F');

        private static async Task<Dictionary<string, string>> ReadChecksumsAsync(
            string path,
            CancellationToken cancellationToken)
        {
            await using var stream = new FileStream(Path.GetFullPath(path), FileMode.Open, FileAccess.Read,
                FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > MaximumReleaseMetadataBytes)
                throw new InvalidDataException("SHA256SUMS exceeds the metadata size limit.");
            using var reader = new StreamReader(stream);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.Length < 67 || !IsSha256(line[..64]) || line[64] != ' ')
                    throw new InvalidDataException("SHA256SUMS contains an invalid line.");
                var name = line[65..].TrimStart(' ', '*');
                if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name
                    || !result.TryAdd(name, line[..64].ToLowerInvariant()))
                {
                    throw new InvalidDataException("SHA256SUMS contains an unsafe or duplicate file name.");
                }
            }
            return result;
        }

        private static bool IsHttps(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps;

        private static bool IsPluginId(string? value) => value is { Length: >= 1 and <= 64 }
            && value[0] is >= 'a' and <= 'z' or >= '0' and <= '9'
            && value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

        private static bool IsChildOf(string path, string parent)
        {
            var prefix = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static void DeleteStagingWithoutThrowing(string? path)
        {
            if (path is null) return;
            try { DeleteTree(path); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }

        private static void DeleteTree(string path)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.Directory)
                    && !attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    DeleteTree(entry);
                }
                else if (attributes.HasFlag(FileAttributes.Directory))
                {
                    Directory.Delete(entry);
                }
                else
                {
                    File.Delete(entry);
                }
            }
            Directory.Delete(path);
        }

        private sealed record ValidatedManifest(PluginManifest Manifest, string Sha256);
    }

    /// <summary>Reports archive identity and, after extraction, its validated staging directory.</summary>
    public sealed class PluginArchiveResult
    {
        public bool IsSuccess => Manifest is not null;
        public PluginManifest? Manifest { get; private init; }
        public string? Sha256 { get; private init; }
        public string? ManifestSha256 { get; private init; }
        public StagedPluginPackage? StagedPackage { get; private init; }
        public string? ErrorCode { get; private init; }
        public string? ErrorMessage { get; private init; }

        public static PluginArchiveResult Ok(
            PluginManifest manifest,
            string sha256,
            string manifestSha256,
            StagedPluginPackage? stagedPackage = null) => new()
            {
                Manifest = manifest,
                Sha256 = sha256,
                ManifestSha256 = manifestSha256,
                StagedPackage = stagedPackage
            };

        public static PluginArchiveResult Fail(string code, string message) => new()
        {
            ErrorCode = code,
            ErrorMessage = message
        };
    }

    /// <summary>Identifies a validated package that is ready to replace the installed package.</summary>
    public sealed class StagedPluginPackage
    {
        internal StagedPluginPackage(string directory, PluginManifest manifest)
        {
            Directory = directory;
            Manifest = manifest;
        }

        public string Directory { get; }
        public PluginManifest Manifest { get; }
    }

    /// <summary>Reports a completed file operation, including non-fatal cleanup warnings.</summary>
    public sealed class PluginFileOperationResult
    {
        public bool IsSuccess { get; private init; }
        public string? Warning { get; private init; }
        public string? ErrorCode { get; private init; }
        public string? ErrorMessage { get; private init; }

        public static PluginFileOperationResult Ok(string? warning = null) => new()
        {
            IsSuccess = true,
            Warning = warning
        };

        public static PluginFileOperationResult Fail(string code, string message) => new()
        {
            ErrorCode = code,
            ErrorMessage = message
        };
    }
}
