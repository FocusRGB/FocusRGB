using System.Text.Json;
using FocusRgb.Core.Protocol;

namespace FocusRgb.App.Plugins
{
    /// <summary>Lists local packages and records which ones were installed through verified official discovery.</summary>
    public sealed class PluginInventory : IDisposable
    {
        private readonly string _packagesDirectory;
        private readonly string _originsPath;
        private readonly SemaphoreSlim _writeGate = new(1, 1);

        public PluginInventory(string packagesDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(packagesDirectory);
            _packagesDirectory = Path.GetFullPath(packagesDirectory);
            _originsPath = Path.Combine(_packagesDirectory, ".install-origins.json");
        }

        public void Dispose() => _writeGate.Dispose();

        public IReadOnlyList<InstalledPluginInfo> List(
            IEnumerable<string> enabledPluginIds,
            IEnumerable<PluginRuntimeEntry> runtimeEntries)
        {
            var enabled = enabledPluginIds.ToHashSet(StringComparer.Ordinal);
            var running = runtimeEntries.Where(entry => entry.IsRunning)
                .Select(entry => entry.PluginId).ToHashSet(StringComparer.Ordinal);
            var official = ReadOfficialIds();
            if (!Directory.Exists(_packagesDirectory)) return [];
            return Directory.EnumerateDirectories(_packagesDirectory)
                .Where(directory => !Path.GetFileName(directory).StartsWith('.'))
                .Select(directory => CreateInfo(Path.GetFileName(directory), enabled, running, official))
                .OrderBy(plugin => plugin.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<string?> SetOfficialAsync(
            string pluginId,
            bool isOfficial,
            CancellationToken cancellationToken = default)
        {
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var ids = ReadOfficialIds();
                if (isOfficial) ids.Add(pluginId); else ids.Remove(pluginId);
                Directory.CreateDirectory(_packagesDirectory);
                var temporary = _originsPath + ".tmp";
                try
                {
                    await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(ids.Order()),
                        cancellationToken).ConfigureAwait(false);
                    File.Move(temporary, _originsPath, true);
                    return null;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    try { File.Delete(temporary); }
                    catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
                    return exception.Message;
                }
            }
            finally
            {
                _writeGate.Release();
            }
        }

        private InstalledPluginInfo CreateInfo(
            string pluginId,
            HashSet<string> enabled,
            HashSet<string> running,
            HashSet<string> official)
        {
            var package = PluginPackage.Load(_packagesDirectory, pluginId);
            return new InstalledPluginInfo
            {
                Id = pluginId,
                DisplayName = package.Manifest?.Id ?? pluginId,
                Version = package.Manifest?.Version,
                Manifest = package.Manifest,
                IsOfficial = official.Contains(pluginId),
                IsEnabled = enabled.Contains(pluginId),
                IsRunning = running.Contains(pluginId),
                Error = package.IsSuccess ? null : package.ErrorMessage
            };
        }

        private HashSet<string> ReadOfficialIds()
        {
            try
            {
                if (!File.Exists(_originsPath)) return new HashSet<string>(StringComparer.Ordinal);
                var ids = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_originsPath)) ?? [];
                return ids.ToHashSet(StringComparer.Ordinal);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }
        }
    }

    /// <summary>Describes one package folder without treating manifest claims as proof of official origin.</summary>
    public sealed class InstalledPluginInfo
    {
        public required string Id { get; init; }
        public required string DisplayName { get; init; }
        public string? Version { get; init; }
        public PluginManifest? Manifest { get; init; }
        public bool IsOfficial { get; init; }
        public bool IsEnabled { get; init; }
        public bool IsRunning { get; init; }
        public string? Error { get; init; }
    }

    /// <summary>Reports one user-requested plugin package or lifecycle operation.</summary>
    public sealed class PluginManagementResult
    {
        public bool IsSuccess { get; private init; }
        public required string Message { get; init; }

        public static PluginManagementResult Ok(string message) => new() { IsSuccess = true, Message = message };
        public static PluginManagementResult Fail(string message) => new() { Message = message };
    }
}
