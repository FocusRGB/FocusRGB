using System.Collections.Concurrent;
using FocusRgb.Core.Protocol;
using FocusRgb.Core.Coordination;

namespace FocusRgb.App.Plugins
{
    /// <summary>Adapts supervised plugin processes to the portable output coordinator.</summary>
    public sealed class PluginRuntimeGateway : IPluginOutputGateway
    {
        private readonly ConcurrentDictionary<string, PluginRuntimeEntry> _entries;

        public PluginRuntimeGateway(PluginRuntime runtime)
            : this(runtime?.Entries ?? throw new ArgumentNullException(nameof(runtime)))
        {
        }

        public PluginRuntimeGateway(IEnumerable<PluginRuntimeEntry> entries)
        {
            ArgumentNullException.ThrowIfNull(entries);
            _entries = new ConcurrentDictionary<string, PluginRuntimeEntry>(StringComparer.Ordinal);
            Attach(entries);
        }

        public void Attach(IEnumerable<PluginRuntimeEntry> entries)
        {
            ArgumentNullException.ThrowIfNull(entries);
            foreach (var entry in entries)
            {
                _entries[entry.PluginId] = entry;
                if (entry.Client is not null)
                    entry.Client.StatusChanged += (_, _) => _ = RefreshAvailabilityAsync(entry);
            }
        }

        public void Detach(string pluginId) => _entries.TryRemove(pluginId, out _);

        public event EventHandler<PluginAvailabilityChangedEventArgs>? AvailabilityChanged;

        public IReadOnlyList<PluginCapability>? GetCapabilities(string pluginId) =>
            _entries.TryGetValue(pluginId, out var entry) && entry.IsRunning
                ? entry.Handshake!.Capabilities
                : null;

        public async Task<PluginGatewayResult<PluginCatalogResult>> ListCatalogAsync(
            string pluginId,
            string kind,
            CancellationToken cancellationToken = default)
        {
            var client = Client(pluginId);
            if (client is null) return Missing<PluginCatalogResult>(pluginId);
            var result = await client.ListCatalogAsync(kind, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess
                ? new PluginGatewayResult<PluginCatalogResult>(result.Value!)
                : new PluginGatewayResult<PluginCatalogResult>(result.ErrorCode!, result.ErrorMessage!);
        }

        public Task<PluginGatewayResult<PluginCatalogResult>> ListGameCatalogAsync(
            string pluginId,
            CancellationToken cancellationToken = default)
        {
            var capabilities = GetCapabilities(pluginId);
            if (capabilities is null) return Task.FromResult(Missing<PluginCatalogResult>(pluginId));
            var capability = capabilities.SingleOrDefault(item => item.Id == "game.catalog");
            if (capability?.Support is not ("supported" or "unverified"))
            {
                return Task.FromResult(new PluginGatewayResult<PluginCatalogResult>(
                    capability?.Reason?.Code ?? "capability-unsupported",
                    capability?.Reason?.Message ?? $"Plugin '{pluginId}' does not provide a game catalog."));
            }
            return ListCatalogAsync(pluginId, "game", cancellationToken);
        }

        public async Task<PluginGatewayResult<BindingApplyResult>> ApplyBindingAsync(
            string pluginId,
            BindingApplyParameters parameters,
            CancellationToken cancellationToken = default)
        {
            var client = Client(pluginId);
            if (client is null) return Missing<BindingApplyResult>(pluginId);
            var result = await client.ApplyBindingAsync(parameters, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess
                ? new PluginGatewayResult<BindingApplyResult>(result.Value!)
                : new PluginGatewayResult<BindingApplyResult>(result.ErrorCode!, result.ErrorMessage!);
        }

        public async Task<PluginGatewayResult<BindingReleaseResult>> ReleaseBindingAsync(
            string pluginId,
            BindingReleaseParameters parameters,
            CancellationToken cancellationToken = default)
        {
            var client = Client(pluginId);
            if (client is null) return Missing<BindingReleaseResult>(pluginId);
            var result = await client.ReleaseBindingAsync(parameters, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess
                ? new PluginGatewayResult<BindingReleaseResult>(result.Value!)
                : new PluginGatewayResult<BindingReleaseResult>(result.ErrorCode!, result.ErrorMessage!);
        }

        private PluginProcessClient? Client(string pluginId) =>
            _entries.TryGetValue(pluginId, out var entry) && entry.IsRunning ? entry.Client : null;

        private static PluginGatewayResult<T> Missing<T>(string pluginId) =>
            new("plugin-absent", $"Plugin '{pluginId}' is not running.");

        private async Task RefreshAvailabilityAsync(PluginRuntimeEntry entry)
        {
            if (!_entries.TryGetValue(entry.PluginId, out var current) || !ReferenceEquals(current, entry)) return;
            try
            {
                var status = await entry.Client!.GetStatusAsync().ConfigureAwait(false);
                if (status.IsSuccess)
                {
                    var value = status.Value!;
                    var available = entry.Client.Health == "healthy"
                        && value.Availability is "available" or "degraded";
                    AvailabilityChanged?.Invoke(this, new PluginAvailabilityChangedEventArgs(
                        entry.PluginId,
                        available,
                        available ? null : entry.Client.Health == "unhealthy"
                            ? "timeout"
                            : value.Reason?.Code ?? "provider-not-running",
                        available ? null : entry.Client.Health == "unhealthy"
                            ? "The plugin stopped responding."
                            : value.Reason?.Message ?? "The plugin is unavailable."));
                }
                else if (status.ErrorCode is "plugin-absent" or "plugin-exited")
                {
                    AvailabilityChanged?.Invoke(this, new PluginAvailabilityChangedEventArgs(
                        entry.PluginId, false, status.ErrorCode, status.ErrorMessage));
                }
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                AvailabilityChanged?.Invoke(this, new PluginAvailabilityChangedEventArgs(
                    entry.PluginId, false, "plugin-exited", exception.Message));
            }
        }
    }

    /// <summary>Reports a provider availability transition observed through its process.</summary>
    public sealed class PluginAvailabilityChangedEventArgs(
        string pluginId,
        bool isAvailable,
        string? reasonCode = null,
        string? message = null) : EventArgs
    {
        public string PluginId { get; } = pluginId;
        public bool IsAvailable { get; } = isAvailable;
        public string? ReasonCode { get; } = reasonCode;
        public string? Message { get; } = message;
    }
}
