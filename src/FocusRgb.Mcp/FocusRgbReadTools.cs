using System.ComponentModel;
using System.Text.Json;
using FocusRgb.LocalMcp;
using ModelContextProtocol.Server;

namespace FocusRgb.Mcp
{
    /// <summary>Exposes application-owned queries and approval-gated commands over MCP 2026-07-28.</summary>
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

        [McpServerTool(Name = "selectProfile")]
        [Description("Requests approval to select a saved profile and enter manual mode.")]
        public Task<JsonElement> SelectProfileAsync(
            string profile,
            McpServer server,
            CancellationToken cancellationToken) =>
            client.RequestAsync("selectProfile", new { profile }, ClientName(server), cancellationToken);

        [McpServerTool(Name = "setMode")]
        [Description("Requests approval to switch FocusRGB between manual and automatic selection.")]
        public Task<JsonElement> SetModeAsync(
            string mode,
            McpServer server,
            CancellationToken cancellationToken) =>
            client.RequestAsync("setMode", new { mode }, ClientName(server), cancellationToken);

        [McpServerTool(Name = "applySettings")]
        [Description("Requests approval to validate and apply a complete settings document at the expected revision.")]
        public Task<JsonElement> ApplySettingsAsync(
            JsonElement settings,
            long expectedRevision,
            McpServer server,
            CancellationToken cancellationToken) =>
            client.RequestAsync("applySettings", new { settings, expectedRevision }, ClientName(server), cancellationToken);

        [McpServerTool(Name = "respondToNotification")]
        [Description("Requests approval to accept or decline a runtime recovery notification.")]
        public Task<JsonElement> RespondToNotificationAsync(
            string id,
            string response,
            McpServer server,
            CancellationToken cancellationToken) =>
            client.RequestAsync("respondToNotification", new { id, response }, ClientName(server), cancellationToken);

        [McpServerTool(Name = "upsertProfile")]
        [Description("Requests approval to add or replace one complete profile at the expected settings revision.")]
        public Task<JsonElement> UpsertProfileAsync(
            JsonElement profile,
            long expectedRevision,
            McpServer server,
            CancellationToken cancellationToken) =>
            client.RequestAsync("upsertProfile", new { profile, expectedRevision }, ClientName(server), cancellationToken);

        [McpServerTool(Name = "upsertApplicationGroup")]
        [Description("Requests approval to add or replace one application group at the expected settings revision.")]
        public Task<JsonElement> UpsertApplicationGroupAsync(
            JsonElement applicationGroup,
            long expectedRevision,
            McpServer server,
            CancellationToken cancellationToken) =>
            client.RequestAsync(
                "upsertApplicationGroup", new { applicationGroup, expectedRevision }, ClientName(server), cancellationToken);

        [McpServerTool(Name = "reorderMappings")]
        [Description("Requests approval to replace mapping priority order using every mapping ID exactly once.")]
        public Task<JsonElement> ReorderMappingsAsync(
            string[] mappingIds,
            long expectedRevision,
            McpServer server,
            CancellationToken cancellationToken) =>
            client.RequestAsync("reorderMappings", new { mappingIds, expectedRevision }, ClientName(server), cancellationToken);

        private static string ClientName(McpServer server) => server.ClientInfo is { } clientInfo
            ? $"{clientInfo.Name} {clientInfo.Version}"
            : "Unknown MCP client";
    }
}
