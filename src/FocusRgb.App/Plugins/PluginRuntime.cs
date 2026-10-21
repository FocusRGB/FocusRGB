using FocusRgb.Core.Protocol;

namespace FocusRgb.App.Plugins
{
    /// <summary>Starts only settings-enabled local packages and isolates individual startup failures.</summary>
    public sealed class PluginRuntime : IAsyncDisposable
    {
        private readonly List<PluginRuntimeEntry> _entries;
        private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
        private bool _disposed;

        private PluginRuntime(List<PluginRuntimeEntry> entries)
        {
            _entries = entries;
        }

        public IReadOnlyList<PluginRuntimeEntry> Entries
        {
            get
            {
                lock (_entries) return _entries.ToArray();
            }
        }

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
                return new PluginRuntime(entries.ToList());
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
            await _lifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed) return;
                _disposed = true;
                foreach (var client in Entries
                    .Where(entry => entry.Client is not null)
                    .Select(entry => entry.Client!))
                {
                    await DisposeWithoutThrowingAsync(client).ConfigureAwait(false);
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public async Task<PluginRuntimeEntry> RestartAsync(
            string packagesDirectory,
            string pluginId,
            CancellationToken cancellationToken = default)
        {
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                int index;
                lock (_entries)
                {
                    index = _entries.FindIndex(entry => entry.PluginId == pluginId);
                }
                if (index < 0) throw new InvalidOperationException($"Plugin '{pluginId}' is not enabled.");
                var replacement = await StartOneAsync(
                    packagesDirectory, pluginId, cancellationToken).ConfigureAwait(false);
                PluginProcessClient? previous;
                lock (_entries)
                {
                    previous = _entries[index].Client;
                    _entries[index] = replacement;
                }
                if (previous is not null)
                    await DisposeWithoutThrowingAsync(previous).ConfigureAwait(false);
                return replacement;
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public async Task<bool> StopAsync(
            string pluginId,
            CancellationToken cancellationToken = default)
        {
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                PluginProcessClient? client;
                lock (_entries)
                {
                    var index = _entries.FindIndex(entry => entry.PluginId == pluginId);
                    if (index < 0) return false;
                    client = _entries[index].Client;
                    _entries.RemoveAt(index);
                }
                if (client is not null)
                    await DisposeWithoutThrowingAsync(client).ConfigureAwait(false);
                return true;
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public async Task<PluginRuntimeEntry> StartPluginAsync(
            string packagesDirectory,
            string pluginId,
            CancellationToken cancellationToken = default)
        {
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                lock (_entries)
                {
                    if (_entries.Any(entry => entry.PluginId == pluginId && entry.IsRunning))
                        throw new InvalidOperationException(
                            $"Plugin '{pluginId}' must be stopped before it can be started again.");
                }
                var entry = await StartOneAsync(
                    packagesDirectory, pluginId, cancellationToken).ConfigureAwait(false);
                PluginProcessClient? previous = null;
                lock (_entries)
                {
                    var index = _entries.FindIndex(current => current.PluginId == pluginId);
                    if (index < 0)
                        _entries.Add(entry);
                    else
                    {
                        previous = _entries[index].Client;
                        _entries[index] = entry;
                    }
                }
                if (previous is not null)
                    await DisposeWithoutThrowingAsync(previous).ConfigureAwait(false);
                return entry;
            }
            finally
            {
                _lifecycleGate.Release();
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
        public bool IsRunning => Client?.Health is "starting" or "healthy";

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
