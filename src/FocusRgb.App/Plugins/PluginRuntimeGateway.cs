using FocusRgb.Contracts;
using FocusRgb.Core.Coordination;

namespace FocusRgb.App.Plugins
{
    /// <summary>Adapts supervised plugin processes to the portable output coordinator.</summary>
    public sealed class PluginRuntimeGateway : IPluginOutputGateway
    {
        private readonly Dictionary<string, PluginRuntimeEntry> _entries;

        public PluginRuntimeGateway(PluginRuntime runtime)
            : this(runtime?.Entries ?? throw new ArgumentNullException(nameof(runtime)))
        {
        }

        public PluginRuntimeGateway(IEnumerable<PluginRuntimeEntry> entries)
        {
            ArgumentNullException.ThrowIfNull(entries);
            _entries = new Dictionary<string, PluginRuntimeEntry>(StringComparer.Ordinal);
            Attach(entries);
        }

        public void Attach(IEnumerable<PluginRuntimeEntry> entries)
        {
            ArgumentNullException.ThrowIfNull(entries);
            foreach (var entry in entries)
            {
                _entries.Add(entry.PluginId, entry);
                if (entry.Client is not null)
                    entry.Client.StatusChanged += (_, _) => _ = RefreshAvailabilityAsync(entry);
            }
        }

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
            _entries.TryGetValue(pluginId, out var entry) ? entry.Client : null;

        private static PluginGatewayResult<T> Missing<T>(string pluginId) =>
            new("plugin-absent", $"Plugin '{pluginId}' is not running.");

        private async Task RefreshAvailabilityAsync(PluginRuntimeEntry entry)
        {
            try
            {
                var status = await entry.Client!.GetStatusAsync().ConfigureAwait(false);
                AvailabilityChanged?.Invoke(this, new PluginAvailabilityChangedEventArgs(
                    entry.PluginId,
                    status.IsSuccess && status.Value!.Availability == "available"));
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                AvailabilityChanged?.Invoke(this, new PluginAvailabilityChangedEventArgs(entry.PluginId, false));
            }
        }
    }

    /// <summary>Reports a provider availability transition observed through its process.</summary>
    public sealed class PluginAvailabilityChangedEventArgs(string pluginId, bool isAvailable) : EventArgs
    {
        public string PluginId { get; } = pluginId;
        public bool IsAvailable { get; } = isAvailable;
    }
}
