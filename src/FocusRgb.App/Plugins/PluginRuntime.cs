using FocusRgb.Contracts;

namespace FocusRgb.App.Plugins
{
    /// <summary>Starts only settings-enabled local packages and isolates individual startup failures.</summary>
    public sealed class PluginRuntime : IAsyncDisposable
    {
        private readonly List<PluginProcessClient> _clients;

        private PluginRuntime(List<PluginRuntimeEntry> entries, List<PluginProcessClient> clients)
        {
            Entries = entries;
            _clients = clients;
        }

        public IReadOnlyList<PluginRuntimeEntry> Entries { get; }

        public static async Task<PluginRuntime> StartAsync(
            string packagesDirectory,
            IEnumerable<string> enabledPluginIds,
            CancellationToken cancellationToken = default)
        {
            var entries = new List<PluginRuntimeEntry>();
            var clients = new List<PluginProcessClient>();
            foreach (var pluginId in enabledPluginIds)
            {
                var package = PluginPackage.Load(packagesDirectory, pluginId);
                if (!package.IsSuccess)
                {
                    entries.Add(PluginRuntimeEntry.Fail(pluginId, package.ErrorCode!, package.ErrorMessage!));
                    continue;
                }
                var started = await PluginProcessClient.StartAsync(
                    package.Launch!, package.Manifest!, cancellationToken).ConfigureAwait(false);
                if (!started.IsSuccess)
                {
                    entries.Add(PluginRuntimeEntry.Fail(pluginId, started.ErrorCode!, started.ErrorMessage!));
                    continue;
                }
                clients.Add(started.Client!);
                entries.Add(PluginRuntimeEntry.Ok(pluginId, started.Client!, started.Handshake!));
            }
            return new PluginRuntime(entries, clients);
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var client in _clients)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>Reports one enabled package as started or failed without aborting other packages.</summary>
    public sealed class PluginRuntimeEntry
    {
        public required string PluginId { get; init; }
        public PluginProcessClient? Client { get; private init; }
        public InitializeResult? Handshake { get; private init; }
        public string? ErrorCode { get; private init; }
        public string? ErrorMessage { get; private init; }
        public bool IsRunning => Client is not null;

        public static PluginRuntimeEntry Ok(
            string pluginId,
            PluginProcessClient client,
            InitializeResult handshake) => new()
            {
                PluginId = pluginId,
                Client = client,
                Handshake = handshake
            };

        public static PluginRuntimeEntry Fail(string pluginId, string code, string message) => new()
        {
            PluginId = pluginId,
            ErrorCode = code,
            ErrorMessage = message
        };
    }
}
