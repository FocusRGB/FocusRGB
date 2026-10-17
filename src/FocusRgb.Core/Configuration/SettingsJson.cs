using System.Text.Json;

namespace FocusRgb.Core.Configuration
{
    /// <summary>Reads and validates settings in memory before LocalStateStore persists them.</summary>
    public static class SettingsJson
    {
        public const int CurrentSchemaVersion = 4;

        public static SettingsLoadResult Load(string json)
        {
            ArgumentNullException.ThrowIfNull(json);
            try
            {
                var document = JsonSerializer.Deserialize<SettingsDocument>(json, ConfigurationJson.Options);
                if (document?.SchemaVersion is 1 or 2 or 3)
                {
                    var groups = document.ApplicationGroups ?? document.Rules?.Where(rule => rule.Match?.Executables is not null).Select(rule =>
                        new ApplicationGroupSettings
                        {
                            Id = rule.Id,
                            DisplayName = document.Profiles?.FirstOrDefault(profile => profile.Id == rule.Profile)?.DisplayName
                                ?? rule.Id,
                            Executables = rule.Match!.Executables,
                            AdditionalProperties = rule.Match.AdditionalProperties
                        }).ToList();
                    document = new SettingsDocument
                    {
                        SchemaVersion = CurrentSchemaVersion,
                        Application = document.SchemaVersion == 1
                            ? CreateDefaultApplicationSettings()
                            : document.Application,
                        Plugins = document.SchemaVersion is 1 or 2
                            ? CreateDefaultPluginSettings()
                            : document.Plugins,
                        Selection = document.Selection,
                        Profiles = document.Profiles,
                        ApplicationGroups = groups,
                        Rules = document.Rules?.Select(rule => new RuleSettings
                        {
                            Id = rule.Id,
                            ApplicationGroup = rule.ApplicationGroup ?? (rule.Match?.Executables is null ? null : rule.Id),
                            Profile = rule.Profile,
                            Match = rule.Match?.Game is null ? null : new ApplicationMatch
                            {
                                Game = rule.Match.Game,
                                AdditionalProperties = rule.Match.AdditionalProperties
                            },
                            Eligibility = rule.Eligibility,
                            Enabled = rule.Enabled,
                            AdditionalProperties = rule.AdditionalProperties
                        }).ToList(),
                        AdditionalProperties = document.AdditionalProperties
                    };
                }
                var errors = SettingsValidator.Validate(document);
                return errors.Count == 0 ? SettingsLoadResult.Ok(document!) : SettingsLoadResult.Fail(errors);
            }
            catch (JsonException exception)
            {
                return SettingsLoadResult.Fail([new ConfigurationError
                {
                    Path = exception.Path ?? "$",
                    Message = exception.Message
                }]);
            }
        }

        public static string Serialize(SettingsDocument settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            return JsonSerializer.Serialize(settings, ConfigurationJson.Options);
        }

        public static SettingsLoadResult LoadDefaults()
        {
            using var stream = typeof(SettingsJson).Assembly.GetManifestResourceStream(
                "FocusRgb.Core.Configuration.default-settings.json")
                ?? throw new InvalidOperationException("Default settings resource is missing.");
            using var reader = new StreamReader(stream);
            return Load(reader.ReadToEnd());
        }

        public static NativeSetupDocument CreateDefaultNativeSetup() => NativeSetupJson.CreateDefault();

        public static ApplicationSettings CreateDefaultApplicationSettings() => new()
        {
            CloseButtonBehavior = "minimize-to-tray",
            StartMinimized = false,
            Theme = "system",
            VerboseLogging = false
        };

        public static PluginSettings CreateDefaultPluginSettings() => new() { Enabled = [] };
    }
}
