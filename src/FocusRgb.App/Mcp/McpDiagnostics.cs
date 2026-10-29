using System.Text.Json;
using FocusRgb.App.Diagnostics;
using FocusRgb.App.Plugins;
using FocusRgb.Core.Commands;
using FocusRgb.Core.Coordination;
using FocusRgb.Core.Selection;

namespace FocusRgb.App.Mcp
{
    public sealed record DiagnosticUiState(
        string SelectedTab, bool SettingsDirty, bool FocusLibraryDirty, bool ProfilesDirty,
        bool IsVisible, IReadOnlyList<string> Dialogs, IReadOnlyList<string> Toasts,
        IReadOnlyList<DiagnosticUiStatus> Statuses);
    public sealed record DiagnosticUiStatus(string Source, string Message);

    /// <summary>Owns bounded, already-redacted diagnostic history, without a command or approval path.</summary>
    public sealed class McpDiagnostics : IDisposable
    {
        public const int TraceCapacity = 200;
        private readonly object _gate = new();
        private readonly Queue<TraceItem> _trace = new();
        private readonly Dictionary<string, StartHistory> _starts = new(StringComparer.Ordinal);
        private readonly ApplicationStateService _state;
        private readonly Func<IReadOnlyList<InstalledPluginInfo>> _plugins;
        private readonly Func<IReadOnlyList<PluginRuntimeEntry>> _runtime;
        private readonly OutputCoordinator? _output;
        private readonly Func<Task<DiagnosticUiState>>? _ui;
        private readonly RecentLogBuffer _logs;
        private string _catalogStatus = "Not queried";
        private DateTimeOffset? _catalogRefreshedAt;
        private bool _catalogFromCache;

        public McpDiagnostics(ApplicationStateService state, Func<IReadOnlyList<InstalledPluginInfo>> plugins,
            Func<IReadOnlyList<PluginRuntimeEntry>>? runtime = null, OutputCoordinator? output = null,
            Func<Task<DiagnosticUiState>>? ui = null)
            : this(state, plugins, runtime, output, ui, AppLog.Recent) { }

        internal McpDiagnostics(ApplicationStateService state, Func<IReadOnlyList<InstalledPluginInfo>> plugins,
            Func<IReadOnlyList<PluginRuntimeEntry>>? runtime, OutputCoordinator? output,
            Func<Task<DiagnosticUiState>>? ui, RecentLogBuffer logs)
        {
            _state = state;
            _plugins = plugins;
            _runtime = runtime ?? (() => []);
            _output = output;
            _ui = ui;
            _logs = logs;
            state.ObservationEvaluated += OnObservationEvaluated;
        }

        public void Dispose() => _state.ObservationEvaluated -= OnObservationEvaluated;

        public void RecordCatalog(OfficialPluginCatalogResult catalog)
        {
            lock (_gate)
            {
                _catalogStatus = DiagnosticText.Clean(catalog.Status)!;
                _catalogRefreshedAt = catalog.RefreshedAt;
                _catalogFromCache = catalog.IsFromCache;
            }
        }

        public void RecordPluginStart(PluginRuntimeEntry entry)
        {
            if (entry.Client is not { } client)
            {
                return;
            }

            lock (_gate)
            {
                _starts.TryGetValue(entry.PluginId, out var previous);
                if (previous?.ProcessId == client.ProcessId && previous.StartedAt == client.StartedAt)
                {
                    return;
                }

                if (_starts.Count == 128 && previous is null)
                {
                    _starts.Remove(_starts.Keys.First());
                }

                _starts[entry.PluginId] = new StartHistory(client.ProcessId, client.StartedAt,
                    previous is null || previous.ProcessId == 0 ? 0 : previous.Restarts + 1, previous?.LastError);
            }
        }

        public void RecordPluginError(string pluginId, string? code)
        {
            lock (_gate)
            {
                _starts.TryGetValue(pluginId, out var previous);
                if (_starts.Count == 128 && previous is null)
                {
                    _starts.Remove(_starts.Keys.First());
                }
                _starts[pluginId] = (previous ?? new StartHistory(0, DateTimeOffset.MinValue, 0, null))
                    with { LastError = DiagnosticText.Clean(code, 64) };
            }
        }

        private void OnObservationEvaluated(object? sender, SelectionObservationTrace trace)
        {
            var item = new TraceItem(trace.ObservedAt,
                DiagnosticText.Clean(trace.Observation.ObservedForegroundExecutable, 128),
                DiagnosticText.Clean(trace.Observation.ForegroundExecutable, 128),
                DiagnosticText.Clean(trace.Observation.ForegroundWindowClass, 128), trace.Observation.IgnoredAsTransient,
                DiagnosticText.Clean(trace.Debounce.Stable, 128), DiagnosticText.Clean(trace.Debounce.Pending, 128),
                trace.Debounce.IsPending, trace.Debounce.RemainingMs, trace.Debounce.DelayMs,
                DiagnosticText.Clean(trace.Mode, 16), DiagnosticText.Clean(trace.ProfileId, 64),
                DiagnosticText.Clean(trace.RuleId, 64), DiagnosticText.Clean(trace.Reason, 64), DiagnosticText.Clean(trace.ErrorCode, 64));
            lock (_gate)
            {
                if (_trace.Count == TraceCapacity)
                {
                    _trace.Dequeue();
                }

                _trace.Enqueue(item);
            }
        }

        public async Task<JsonElement> QueryAsync(string method, JsonElement? parameters, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var limit = Limit(parameters);
            switch (method)
            {
                case "getSelectionTrace":
                    lock (_gate)
                    {
                        return DiagnosticText.Serialize(new { items = _trace.TakeLast(limit).ToArray(), retained = _trace.Count,
                            capacity = TraceCapacity, truncated = _trace.Count > limit });
                    }
                case "getRecentLogs":
                    var level = Filter(parameters, "level");
                    var source = Filter(parameters, "source");
                    var lines = _logs.Read(limit + 1, level, source);
                    return DiagnosticText.Serialize(new { items = lines.TakeLast(limit).ToArray(), capacity = RecentLogBuffer.Capacity,
                        limit, truncated = lines.Count > limit });
                case "getPluginRuntime": return PluginRuntime(limit);
                case "getUiState":
                    if (_ui is null)
                    {
                        return DiagnosticText.Serialize(new { status = "unavailable" });
                    }

                    var ui = await _ui().WaitAsync(cancellationToken).ConfigureAwait(false);
                    return DiagnosticText.Serialize(new
                    {
                        selectedTab = DiagnosticText.Clean(ui.SelectedTab, 64), ui.SettingsDirty, ui.FocusLibraryDirty, ui.ProfilesDirty, ui.IsVisible,
                        dialogs = ui.Dialogs.Take(8).Select(name => DiagnosticText.Clean(name, 64)).ToArray(),
                        toasts = ui.Toasts.Take(8).Select(name => DiagnosticText.Clean(name, 64)).ToArray(),
                        statuses = ui.Statuses.Take(10).Select(status => new { source = DiagnosticText.Clean(status.Source, 64),
                            message = DiagnosticText.Clean(status.Message) }).ToArray(),
                        truncated = ui.Dialogs.Count > 8 || ui.Toasts.Count > 8 || ui.Statuses.Count > 10
                    });
                case "getOutputState":
                    var output = _output?.CurrentOutput;
                    return DiagnosticText.Serialize(new
                    {
                        activeProfile = DiagnosticText.Clean(_state.Current.RuntimeStatus?.Effective.ProfileId, 64),
                        renderingAuthority = DiagnosticText.Clean(output?.RenderingAuthority, 32),
                        writerPlugin = DiagnosticText.Clean(output?.PluginId, 64),
                        bindingId = DiagnosticText.Clean(output?.BindingId, 64),
                        effect = DiagnosticText.Clean(output?.EffectName, 128),
                        effectResourceId = DiagnosticText.Clean(output?.EffectResourceId, 128),
                        outcome = DiagnosticText.Clean(output?.Outcome, 32),
                        writers = output?.Scopes.Take(limit).Select(scope => new { scope = DiagnosticText.Clean(scope, 128),
                            writer = DiagnosticText.Clean(output.PluginId, 64) }).ToArray(),
                        lastOutputAt = _output?.LastOutputAt,
                        evidence = "Binding acknowledgement only; no hardware readback. Provider-managed scopes do not enumerate physical devices.",
                        truncated = output?.Scopes.Count > limit
                    });
                default: throw new LocalMcp.LocalMcpMethodNotFoundException("Unknown diagnostic method.");
            }
        }

        private JsonElement PluginRuntime(int limit)
        {
            var installed = _plugins();
            var entries = _runtime();
            var snapshot = _state.Current;
            lock (_gate)
            {
                var plugins = installed.Take(limit).Select(plugin =>
                {
                    var entry = entries.FirstOrDefault(entry => entry.PluginId == plugin.Id);
                    _starts.TryGetValue(plugin.Id, out var history);
                    return new
                    {
                        id = DiagnosticText.Clean(plugin.Id, 64), version = DiagnosticText.Clean(plugin.Version, 32),
                        origin = plugin.IsOfficial ? "official" : "custom",
                        state = entry?.Client?.Health ?? (entry?.ErrorCode is not null ? "failed" : plugin.IsEnabled ? "not-running" : "disabled"),
                        processId = entry?.Client?.ProcessId, startTime = entry?.Client?.StartedAt,
                        restartCount = history?.Restarts ?? 0,
                        lastError = DiagnosticText.Clean(entry?.ErrorCode ?? entry?.Client?.LastErrorCode ?? history?.LastError ?? plugin.Error),
                        pendingRecoveryApproval = snapshot.RuntimeStatus?.Notifications.Any(notification => notification.PluginId == plugin.Id) == true
                    };
                }).ToArray();
                return DiagnosticText.Serialize(new { plugins, total = installed.Count, truncated = installed.Count > limit,
                    catalog = new { status = _catalogStatus, refreshedAt = _catalogRefreshedAt, fromCache = _catalogFromCache,
                        ageSeconds = _catalogRefreshedAt is { } at ? (double?)Math.Max(0, (DateTimeOffset.UtcNow - at).TotalSeconds) : null } });
            }
        }

        private static int Limit(JsonElement? parameters)
        {
            if (parameters is not null && parameters.Value.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Diagnostic parameters must be an object.");
            }

            if (parameters is null || !parameters.Value.TryGetProperty("limit", out var limit))
            {
                return 20;
            }

            if (limit.ValueKind != JsonValueKind.Number || !limit.TryGetInt32(out var count))
            {
                throw new ArgumentException("limit must be an integer.");
            }

            return Math.Clamp(count, 1, 50);
        }

        private static string? Filter(JsonElement? parameters, string name)
        {
            if (parameters is null || !parameters.Value.TryGetProperty(name, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.String || value.GetString()!.Length > 64)
            {
                throw new ArgumentException("Diagnostic filters must be strings of at most 64 characters.");
            }

            return value.GetString();
        }

        private sealed record StartHistory(int ProcessId, DateTimeOffset StartedAt, int Restarts, string? LastError);
        private sealed record TraceItem(DateTimeOffset At, string? Process, string? RetainedForeground, string? WindowClass,
            bool IgnoredAsTransient, string? StableForeground, string? PendingForeground, bool Debouncing,
            int RemainingMs, int DelayMs, string? Mode, string? ProfileId, string? RuleId, string? Reason, string? ErrorCode);
    }
}
