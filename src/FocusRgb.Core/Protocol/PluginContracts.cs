using System.Text.Json.Serialization;

namespace FocusRgb.Core.Protocol
{
    /// <summary>Names the JSON-RPC methods in the stable 1.x plugin protocol.</summary>
    public static class PluginMethods
    {
        public const string Initialize = "initialize";
        public const string StatusGet = "status/get";
        public const string CatalogList = "catalog/list";
        public const string BindingApply = "binding/apply";
        public const string BindingRelease = "binding/release";
        public const string EffectInput = "effect/input";
        public const string Shutdown = "shutdown";
        public const string StatusChanged = "status/changed";
        public const string GameTelemetry = "game/telemetry";
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
        public string? Runtime { get; init; }
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
    public sealed record InitializeResult
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

    /// <summary>Requests an effect, layout, installed-game, or telemetry descriptor catalog.</summary>
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
        public PluginGameCatalogMetadata? Game { get; init; }
        public PluginTelemetryCatalogMetadata? Telemetry { get; init; }
    }

    /// <summary>Lists executable file names that identify one installed game without exposing paths.</summary>
    public sealed class PluginGameCatalogMetadata
    {
        public required IReadOnlyList<string> Executables { get; init; }
    }

    /// <summary>Identifies one game without assigning meaning to its launcher-owned ID.</summary>
    public sealed class PluginGameReference
    {
        public required string Launcher { get; init; }
        public required string Id { get; init; }
    }

    /// <summary>Groups telemetry descriptors using plugin-owned display text and order.</summary>
    public sealed class PluginTelemetrySection
    {
        public required string Id { get; init; }
        public required string DisplayName { get; init; }
        public int Order { get; init; }
    }

    /// <summary>Describes one selectable string value without making it a host enum.</summary>
    public sealed class PluginTelemetryChoice
    {
        public required string Value { get; init; }
        public required string DisplayName { get; init; }
    }

    /// <summary>Lets any game plugin describe one input or event without host-specific assumptions.</summary>
    public sealed class PluginTelemetryCatalogMetadata
    {
        public required PluginGameReference Game { get; init; }
        public required PluginTelemetrySection Section { get; init; }
        public required string Kind { get; init; }
        public required string ValueType { get; init; }
        public required string Description { get; init; }
        public string? Unit { get; init; }
        public double? Minimum { get; init; }
        public double? Maximum { get; init; }
        public IReadOnlyList<PluginTelemetryChoice>? Choices { get; init; }
    }

    /// <summary>Identifies the game and adapter that produced telemetry.</summary>
    public sealed class PluginTelemetrySource
    {
        public required PluginGameReference Game { get; init; }
        public required string Adapter { get; init; }
    }

    /// <summary>Carries exactly one Boolean, finite number, or bounded string telemetry value.</summary>
    public sealed class PluginTelemetryValue
    {
        public required string ResourceId { get; init; }
        public bool? Boolean { get; init; }
        public double? Number { get; init; }
        [JsonPropertyName("string")]
        public string? StringValue { get; init; }
    }

    /// <summary>Reports one namespaced, one-shot event and its typed fields.</summary>
    public sealed class PluginTelemetryEvent
    {
        public required string ResourceId { get; init; }
        public required DateTimeOffset ObservedAt { get; init; }
        public IReadOnlyList<PluginTelemetryValue> Values { get; init; } = [];
    }

    /// <summary>Publishes a full telemetry snapshot plus events with bounded freshness.</summary>
    public sealed class GameTelemetryParameters
    {
        public required PluginTelemetrySource Source { get; init; }
        public long Sequence { get; init; }
        public required DateTimeOffset ObservedAt { get; init; }
        public int FreshForMs { get; init; }
        public int DisconnectAfterMs { get; init; }
        public IReadOnlyList<PluginTelemetryValue> Inputs { get; init; } = [];
        public IReadOnlyList<PluginTelemetryEvent> Events { get; init; } = [];
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

    /// <summary>Routes one mapped telemetry value or event to the active output binding.</summary>
    public sealed class EffectInputParameters
    {
        public required string Profile { get; init; }
        public required string BindingId { get; init; }
        public required string MappingId { get; init; }
        public required string SourcePluginId { get; init; }
        public required PluginTelemetrySource Source { get; init; }
        public required string SourceResourceId { get; init; }
        public long Sequence { get; init; }
        public required DateTimeOffset ReceivedAt { get; init; }
        public required string Freshness { get; init; }
        public bool ConditionMatched { get; init; }
        public required string TargetInputId { get; init; }
        public PluginTelemetryValue? Value { get; init; }
        public PluginTelemetryEvent? Event { get; init; }
    }

    /// <summary>Reports whether the active output accepted one mapped effect input.</summary>
    public sealed class EffectInputResult
    {
        public required string Outcome { get; init; }
        public PluginReason? Reason { get; init; }
    }

    /// <summary>Identifies a request the host no longer waits for.</summary>
    public sealed class CancelParameters
    {
        public long Id { get; init; }
    }
}
