using System.Text.Json;
using FocusRgb.App.Plugins;
using FocusRgb.Core.Commands;
using FocusRgb.LocalMcp;

namespace FocusRgb.App.Mcp
{
    /// <summary>Projects immutable application-owned state for the read-only local MCP boundary.</summary>
    public sealed class McpReadQueryService : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly ApplicationStateService _state;
        private readonly Func<IReadOnlyList<InstalledPluginInfo>> _plugins;
        private readonly object _gate = new();
        private readonly Func<McpDiagnostics> _createDiagnostics;
        private McpDiagnostics? _diagnostics;
        private bool _disposed;
        private long _settingsRevision = -1;

        public McpReadQueryService(
            ApplicationStateService state,
            Func<IReadOnlyList<InstalledPluginInfo>> plugins,
            Func<McpDiagnostics>? createDiagnostics = null)
        {
            _state = state;
            _plugins = plugins;
            _createDiagnostics = createDiagnostics ?? (() => new McpDiagnostics(state, plugins));
            _state.SnapshotChanged += OnSnapshotChanged;
            OnSnapshotChanged(this, _state.Current);
        }

        public McpDiagnostics? Diagnostics { get { lock (_gate) return _diagnostics; } }

        private void OnSnapshotChanged(object? sender, ApplicationStateSnapshot snapshot)
        {
            lock (_gate)
            {
                if (_disposed || snapshot.SettingsRevision <= _settingsRevision)
                {
                    return;
                }
                var current = _state.Current;
                _settingsRevision = current.SettingsRevision;
                if (current.Settings.Application?.DebuggingMode == true)
                {
                    _diagnostics ??= _createDiagnostics();
                }
                else
                {
                    _diagnostics?.Dispose();
                    _diagnostics = null;
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _state.SnapshotChanged -= OnSnapshotChanged;
                _diagnostics?.Dispose();
                _diagnostics = null;
            }
        }

        private async Task<JsonElement> QueryDiagnosticsAsync(string method, JsonElement? parameters, CancellationToken cancellationToken)
        {
            var diagnostics = Diagnostics;
            if (diagnostics is null)
            {
                return JsonSerializer.SerializeToElement(new { status = "disabled" });
            }
            var result = await diagnostics.QueryAsync(method, parameters, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                return ReferenceEquals(diagnostics, _diagnostics) ? result
                    : JsonSerializer.SerializeToElement(new { status = "disabled" });
            }
        }

        public Task<JsonElement> QueryAsync(string method, CancellationToken cancellationToken)
            => QueryAsync(method, null, cancellationToken);

        public Task<JsonElement> QueryAsync(string method, JsonElement? parameters, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsDiagnosticMethod(method)) return QueryDiagnosticsAsync(method, parameters, cancellationToken);
            var snapshot = _state.Current;
            object value = method switch
            {
                "listProfiles" => snapshot.Settings.Profiles!,
                "listApplicationGroups" => snapshot.Settings.ApplicationGroups!,
                "listMappings" => snapshot.Settings.Rules!.Select((rule, index) => new { order = index, rule }),
                "listPlugins" => _plugins().Select(plugin => new
                {
                    plugin.Id,
                    plugin.DisplayName,
                    plugin.Version,
                    plugin.IsOfficial,
                    plugin.IsEnabled,
                    plugin.IsRunning,
                    plugin.Error
                }),
                "listDevices" => snapshot.Settings.Profiles!
                    .SelectMany(profile => profile.Bindings!.Select(binding => new
                    {
                        profileId = profile.Id,
                        bindingId = binding.Id,
                        pluginId = binding.Plugin,
                        scope = binding.DeviceScope
                    })),
                "getStatus" => new
                {
                    snapshot.SettingsRevision,
                    snapshot.SelectionState,
                    snapshot.Preview,
                    snapshot.RuntimeStatus
                },
                "explainActiveSelection" => new
                {
                    mode = snapshot.SelectionState.Mode,
                    snapshot.Preview,
                    desiredProfile = snapshot.RuntimeStatus!.Desired.ProfileId,
                    reason = snapshot.SelectionReason,
                    effective = snapshot.RuntimeStatus.Effective,
                    applied = snapshot.RuntimeStatus.Applied,
                    pending = snapshot.RuntimeStatus.Pending,
                    detail = snapshot.RuntimeStatus.AppliedDetail
                },
                _ => throw new LocalMcpMethodNotFoundException($"Unknown read-only method '{method}'.")
            };
            return Task.FromResult(JsonSerializer.SerializeToElement(value, JsonOptions));
        }

        public static bool IsDiagnosticMethod(string method) => method is
            "getSelectionTrace" or "getPluginRuntime" or "getRecentLogs" or "getUiState" or "getOutputState";
    }
}
