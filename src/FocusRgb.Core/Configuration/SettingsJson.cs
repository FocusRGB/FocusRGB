using System.Text.Json;

namespace FocusRgb.Core.Configuration
{
    /// <summary>Reads and validates settings in memory; file persistence belongs to slice 06.</summary>
    public static class SettingsJson
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public static SettingsLoadResult Load(string json)
        {
            ArgumentNullException.ThrowIfNull(json);
            try
            {
                var document = JsonSerializer.Deserialize<SettingsDocument>(json, Options);
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
            return JsonSerializer.Serialize(settings, Options);
        }

        public static SettingsLoadResult LoadDefaults()
        {
            using var stream = typeof(SettingsJson).Assembly.GetManifestResourceStream(
                "FocusRgb.Core.Configuration.default-settings.json")
                ?? throw new InvalidOperationException("Default settings resource is missing.");
            using var reader = new StreamReader(stream);
            return Load(reader.ReadToEnd());
        }

        public static NativeSetupDocument CreateDefaultNativeSetup() => new()
        {
            SchemaVersion = 1,
            Origin = "app-default",
            Bindings = []
        };
    }
}
