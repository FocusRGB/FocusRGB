using System.Text.Json;

namespace FocusRgb.Core.Configuration
{
    /// <summary>Reads and validates settings in memory before LocalStateStore persists them.</summary>
    public static class SettingsJson
    {
        public const int CurrentSchemaVersion = 2;

        public static SettingsLoadResult Load(string json)
        {
            ArgumentNullException.ThrowIfNull(json);
            try
            {
                var document = JsonSerializer.Deserialize<SettingsDocument>(json, ConfigurationJson.Options);
                if (document?.SchemaVersion == 1)
                {
                    document = new SettingsDocument
                    {
                        SchemaVersion = CurrentSchemaVersion,
                        Application = CreateDefaultApplicationSettings(),
                        Selection = document.Selection,
                        Profiles = document.Profiles,
                        Rules = document.Rules,
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
    }
}
