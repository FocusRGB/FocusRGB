using System.Text.Json;
using System.Text.Json.Serialization;

namespace FocusRgb.Core.Configuration
{
    /// <summary>Preserves fields introduced by other readers when a document is edited.</summary>
    public abstract class ExtensibleConfiguration
    {
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
    }

    /// <summary>User-owned profiles and rules, separate from runtime intent and native fallback.</summary>
    public sealed class SettingsDocument : ExtensibleConfiguration
    {
        public int? SchemaVersion { get; init; }
        public ApplicationSettings? Application { get; init; }
        public PluginSettings? Plugins { get; init; }
        public SelectionSettings? Selection { get; init; }
        public List<ProfileSettings>? Profiles { get; init; }
        public List<ApplicationGroupSettings>? ApplicationGroups { get; init; }
        public List<RuleSettings>? Rules { get; init; }
    }

    /// <summary>Records the local plugin packages the user explicitly allowed the app to launch.</summary>
    public sealed class PluginSettings : ExtensibleConfiguration
    {
        public List<string>? Enabled { get; init; }
    }

    /// <summary>Stores desktop lifecycle, appearance, and diagnostic preferences.</summary>
    public sealed class ApplicationSettings : ExtensibleConfiguration
    {
        public string? CloseButtonBehavior { get; init; }
        public bool? StartMinimized { get; init; }
        public string? Theme { get; init; }
        public bool? VerboseLogging { get; init; }
    }

    /// <summary>Controls unmatched applications, focus stability, and first-run profile selection.</summary>
    public sealed class SelectionSettings : ExtensibleConfiguration
    {
        public string? DefaultProfile { get; init; }
        public string? Unmatched { get; init; }
        public int? DebounceMs { get; init; }
        public List<string>? IgnoredExecutables { get; init; }
    }

    /// <summary>A reusable output choice whose fallback does not replace user intent.</summary>
    public sealed class ProfileSettings : ExtensibleConfiguration
    {
        public string? Id { get; init; }
        public string? DisplayName { get; init; }
        public List<BindingSettings>? Bindings { get; init; }
        public List<FallbackSettings>? Fallback { get; init; }
    }

    /// <summary>Delegates an effect and optional placement layout to a provider-neutral plugin.</summary>
    public sealed class BindingSettings : ExtensibleConfiguration
    {
        public string? Id { get; init; }
        public string? Plugin { get; init; }
        public string? RenderingAuthority { get; init; }
        public DeviceScopeSettings? DeviceScope { get; init; }
        public ResourceReference? Effect { get; init; }
        public ResourceReference? Layout { get; init; }
    }

    /// <summary>Stores opaque provider identity without deriving it from the display label.</summary>
    public sealed class ResourceReference : ExtensibleConfiguration
    {
        public string? ResourceId { get; init; }
        public string? DisplayName { get; init; }
    }

    /// <summary>Describes the provider-managed scope or reserved explicit device selection.</summary>
    public sealed class DeviceScopeSettings : ExtensibleConfiguration
    {
        public string? Kind { get; init; }
        public List<string>? DeviceIds { get; init; }
    }

    /// <summary>References saved native output or another profile when an integration fails.</summary>
    public sealed class FallbackSettings : ExtensibleConfiguration
    {
        public string? Kind { get; init; }
        public string? Profile { get; init; }
    }

    /// <summary>Stores a reusable named set of executable identities for application mappings.</summary>
    public sealed class ApplicationGroupSettings : ExtensibleConfiguration
    {
        public string? Id { get; init; }
        public string? DisplayName { get; init; }
        public List<string>? Executables { get; init; }
    }

    /// <summary>Maps an application group to a profile; array order is the sole priority.</summary>
    public sealed class RuleSettings : ExtensibleConfiguration
    {
        public string? Id { get; init; }
        public string? ApplicationGroup { get; init; }
        public string? Profile { get; init; }
        public ApplicationMatch? Match { get; init; }
        public string? Eligibility { get; init; }
        /// <summary>Legacy schema field. Readers ignore it; list order defines priority.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Priority { get; init; }
        public bool? Enabled { get; init; }
    }

    /// <summary>Holds executable matches and the reserved launcher-scoped game matcher.</summary>
    public sealed class ApplicationMatch : ExtensibleConfiguration
    {
        public List<string>? Executables { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public GameMatch? Game { get; init; }
    }

    /// <summary>Retains a game identity for future discovery without assuming foreground state.</summary>
    public sealed class GameMatch : ExtensibleConfiguration
    {
        public string? Launcher { get; init; }
        public string? Id { get; init; }
    }

    /// <summary>Independent native fallback snapshot; provider edits never write this document.</summary>
    public sealed class NativeSetupDocument : ExtensibleConfiguration
    {
        public int? SchemaVersion { get; init; }
        public string? Origin { get; init; }
        public DateTimeOffset? AppliedAt { get; init; }
        public List<BindingSettings>? Bindings { get; init; }
    }

    /// <summary>Persists desired selection intent without claiming that any output was applied.</summary>
    public sealed class SelectionStateDocument : ExtensibleConfiguration
    {
        public int? SchemaVersion { get; init; }
        public string? Mode { get; init; }
        public string? ManualProfile { get; init; }
        public string? LastSelectedProfile { get; init; }
        public DateTimeOffset? UpdatedAt { get; init; }
    }
}
