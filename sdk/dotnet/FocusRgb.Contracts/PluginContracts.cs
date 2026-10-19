using System.Text.Json.Serialization;

namespace FocusRgb.Contracts
{
    /// <summary>Names the JSON-RPC methods in the stable 1.0 plugin protocol.</summary>
    public static class PluginMethods
    {
        public const string Initialize = "initialize";
        public const string StatusGet = "status/get";
        public const string CatalogList = "catalog/list";
        public const string BindingApply = "binding/apply";
        public const string BindingRelease = "binding/release";
        public const string Shutdown = "shutdown";
        public const string StatusChanged = "status/changed";
        public const string Cancel = "$/cancel";
    }

    /// <summary>Describes an installed plugin package before any process is launched.</summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class PluginManifest
    {
        public int SchemaVersion { get; init; }
        public string? Id { get; init; }
        public string? Version { get; init; }
        public string? ContractVersion { get; init; }
        public string? EntryPoint { get; init; }
        public List<string>? OperatingSystems { get; init; }
        public List<string>? Architectures { get; init; }
        public List<string>? Roles { get; init; }
        public List<string>? Resources { get; init; }
        public string? License { get; init; }
        public string? Source { get; init; }
    }

    /// <summary>Identifies the host during the version handshake.</summary>
    public sealed class PluginHostInfo
    {
        public required string Name { get; init; }
        public required string Version { get; init; }
    }

    /// <summary>Starts a plugin session with the host's current contract version.</summary>
    public sealed class InitializeParameters
    {
        public required string ContractVersion { get; init; }
        public required PluginHostInfo Host { get; init; }
    }

    /// <summary>Reports plugin identity, negotiated contract version, and declared capabilities.</summary>
    public sealed class InitializeResult
    {
        public required string PluginId { get; init; }
        public required string PluginVersion { get; init; }
        public required string ContractVersion { get; init; }
        public IReadOnlyList<PluginCapability> Capabilities { get; init; } = [];
        public PluginSdkInfo? Sdk { get; init; }
    }

    /// <summary>Identifies the optional SDK implementation for compatibility diagnostics only.</summary>
    public sealed class PluginSdkInfo
    {
        public required string Name { get; init; }
        public required string Language { get; init; }
        public required string Version { get; init; }
    }

    /// <summary>Reports support for one provider-neutral capability.</summary>
    public sealed class PluginCapability
    {
        public required string Id { get; init; }
        public required string Support { get; init; }
        public PluginReason? Reason { get; init; }
    }

    /// <summary>Provides one stable machine code plus an actionable human explanation.</summary>
    public sealed class PluginReason
    {
        public required string Code { get; init; }
        public required string Message { get; init; }
    }

    /// <summary>Reports current provider availability and observable selection.</summary>
    public sealed class PluginStatusResult
    {
        public required string Availability { get; init; }
        public PluginReason? Reason { get; init; }
        public PluginSelection? Current { get; init; }
    }

    /// <summary>Reports the effect and optional layout currently observed by a provider.</summary>
    public sealed class PluginSelection
    {
        public PluginResourceReference? Effect { get; init; }
        public PluginResourceReference? Layout { get; init; }
    }

    /// <summary>References one provider-owned resource without interpreting its opaque ID.</summary>
    public sealed class PluginResourceReference
    {
        public string? ResourceId { get; init; }
        public required string DisplayName { get; init; }
    }

    /// <summary>Requests either an effect or layout catalog.</summary>
    public sealed class CatalogListParameters
    {
        public required string Kind { get; init; }
    }

    /// <summary>Reports a supported or unsupported provider catalog.</summary>
    public sealed class PluginCatalogResult
    {
        public required string Support { get; init; }
        public DateTimeOffset? RefreshedAt { get; init; }
        public IReadOnlyList<PluginCatalogItem> Items { get; init; } = [];
        public PluginReason? Reason { get; init; }
    }

    /// <summary>Describes one catalog resource and its present availability.</summary>
    public sealed class PluginCatalogItem
    {
        public required string ResourceId { get; init; }
        public required string DisplayName { get; init; }
        public required bool Available { get; init; }
        public PluginReason? Reason { get; init; }
    }

    /// <summary>Describes the provider-managed or explicit device scope for one binding.</summary>
    public sealed class PluginDeviceScope
    {
        public required string Kind { get; init; }
        public IReadOnlyList<string>? DeviceIds { get; init; }
    }

    /// <summary>Requests application of one fully resolved profile binding.</summary>
    public sealed class BindingApplyParameters
    {
        public required string Profile { get; init; }
        public required string BindingId { get; init; }
        public required string RenderingAuthority { get; init; }
        public required PluginDeviceScope DeviceScope { get; init; }
        public PluginResourceReference? Effect { get; init; }
        public PluginResourceReference? Layout { get; init; }
    }

    /// <summary>Reports whether a binding was applied, rejected, unsupported, or unavailable.</summary>
    public sealed class BindingApplyResult
    {
        public required string Outcome { get; init; }
        public PluginReason? Reason { get; init; }
        public PluginSelection? Actual { get; init; }
    }

    /// <summary>Identifies one binding whose app-owned changes should be released.</summary>
    public sealed class BindingReleaseParameters
    {
        public required string Profile { get; init; }
        public required string BindingId { get; init; }
    }

    /// <summary>Reports whether a release changed anything.</summary>
    public sealed class BindingReleaseResult
    {
        public required string Outcome { get; init; }
    }

    /// <summary>Identifies a request the host no longer waits for.</summary>
    public sealed class CancelParameters
    {
        public long Id { get; init; }
    }
}
