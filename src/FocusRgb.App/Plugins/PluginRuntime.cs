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
            var tasks = enabledPluginIds.Select(pluginId => StartOneAsync(
                packagesDirectory, pluginId, cancellationToken)).ToList();
            try
            {
                var entries = await Task.WhenAll(tasks).ConfigureAwait(false);
                return new PluginRuntime(entries.ToList(), entries
                    .Where(entry => entry.Client is not null)
                    .Select(entry => entry.Client!)
                    .ToList());
            }
            catch
            {
                foreach (var task in tasks.Where(task => task.IsCompletedSuccessfully))
                {
                    if (task.Result.Client is { } client) await DisposeWithoutThrowingAsync(client).ConfigureAwait(false);
                }
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var client in _clients)
            {
                await DisposeWithoutThrowingAsync(client).ConfigureAwait(false);
            }
        }

        private static async Task<PluginRuntimeEntry> StartOneAsync(
            string packagesDirectory,
            string pluginId,
            CancellationToken cancellationToken)
        {
            var package = PluginPackage.Load(packagesDirectory, pluginId);
            if (!package.IsSuccess)
                return PluginRuntimeEntry.Fail(pluginId, package.ErrorCode!, package.ErrorMessage!);
            var started = await PluginProcessClient.StartAsync(
                package.Launch!, package.Manifest!, cancellationToken).ConfigureAwait(false);
            return started.IsSuccess
                ? PluginRuntimeEntry.Ok(pluginId, started.Client!, started.Handshake!)
                : PluginRuntimeEntry.Fail(pluginId, started.ErrorCode!, started.ErrorMessage!);
        }

        private static async Task DisposeWithoutThrowingAsync(PluginProcessClient client)
        {
            try
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException
                or InvalidOperationException or ObjectDisposedException)
            {
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
