using System.ComponentModel;
using System.Text.Json;
using FocusRgb.LocalMcp;
using ModelContextProtocol.Server;

namespace FocusRgb.Mcp
{
    /// <summary>Exposes read-only application-owned queries over MCP 2026-07-28.</summary>
    [McpServerToolType]
    public sealed class FocusRgbReadTools(LocalMcpPipeClient client)
    {
        [McpServerTool(Name = "listProfiles")]
        [Description("Lists the saved FocusRGB profiles and their bindings.")]
        public Task<JsonElement> ListProfilesAsync(CancellationToken cancellationToken) =>
            client.QueryAsync("listProfiles", cancellationToken);

        [McpServerTool(Name = "listApplicationGroups")]
        [Description("Lists saved application groups and executable identities.")]
        public Task<JsonElement> ListApplicationGroupsAsync(CancellationToken cancellationToken) =>
            client.QueryAsync("listApplicationGroups", cancellationToken);

        [McpServerTool(Name = "listMappings")]
        [Description("Lists application mappings in their effective priority order.")]
        public Task<JsonElement> ListMappingsAsync(CancellationToken cancellationToken) =>
            client.QueryAsync("listMappings", cancellationToken);

        [McpServerTool(Name = "listPlugins")]
        [Description("Lists installed plugins and their official, enabled, and running state.")]
        public Task<JsonElement> ListPluginsAsync(CancellationToken cancellationToken) =>
            client.QueryAsync("listPlugins", cancellationToken);

        [McpServerTool(Name = "listDevices")]
        [Description("Lists saved provider-managed or explicit device scopes; it does not invent device discovery.")]
        public Task<JsonElement> ListDevicesAsync(CancellationToken cancellationToken) =>
            client.QueryAsync("listDevices", cancellationToken);

        [McpServerTool(Name = "getStatus")]
        [Description("Gets the current settings revision, selection state, and runtime output status.")]
        public Task<JsonElement> GetStatusAsync(CancellationToken cancellationToken) =>
            client.QueryAsync("getStatus", cancellationToken);

        [McpServerTool(Name = "explainActiveSelection")]
        [Description("Explains why the current profile was selected and what output is effective.")]
        public Task<JsonElement> ExplainActiveSelectionAsync(CancellationToken cancellationToken) =>
            client.QueryAsync("explainActiveSelection", cancellationToken);
    }
}
