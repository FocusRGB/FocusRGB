using System.Text.Json;
using FocusRgb.App.Plugins;
using FocusRgb.Core.Commands;
using FocusRgb.LocalMcp;

namespace FocusRgb.App.Mcp
{
    /// <summary>Projects immutable application-owned state for the read-only local MCP boundary.</summary>
    public sealed class McpReadQueryService
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly ApplicationStateService _state;
        private readonly Func<IReadOnlyList<InstalledPluginInfo>> _plugins;

        public McpReadQueryService(
            ApplicationStateService state,
            Func<IReadOnlyList<InstalledPluginInfo>> plugins)
        {
            _state = state;
            _plugins = plugins;
        }

        public Task<JsonElement> QueryAsync(string method, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
    }
}
