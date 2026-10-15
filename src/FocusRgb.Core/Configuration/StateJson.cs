using System.Text.Json;

namespace FocusRgb.Core.Configuration
{
    /// <summary>Serializes and validates versioned selection intent independently of applied output.</summary>
    public static class StateJson
    {
        public static StateLoadResult Load(string json)
        {
            ArgumentNullException.ThrowIfNull(json);
            try
            {
                var state = JsonSerializer.Deserialize<SelectionStateDocument>(json, ConfigurationJson.Options);
                var errors = Validate(state);
                return errors.Count == 0 ? StateLoadResult.Ok(state!) : StateLoadResult.Fail(errors);
            }
            catch (JsonException exception)
            {
                return StateLoadResult.Fail([new ConfigurationError
                {
                    Path = exception.Path ?? "$",
                    Message = exception.Message
                }]);
            }
        }

        public static string Serialize(SelectionStateDocument state)
        {
            ArgumentNullException.ThrowIfNull(state);
            return JsonSerializer.Serialize(state, ConfigurationJson.Options);
        }

        public static SelectionStateDocument CreateFirstRun(string defaultProfile, DateTimeOffset updatedAt) => new()
        {
            SchemaVersion = 1,
            Mode = "manual",
            ManualProfile = defaultProfile,
            LastSelectedProfile = defaultProfile,
            UpdatedAt = updatedAt
        };

        public static IReadOnlyList<ConfigurationError> Validate(SelectionStateDocument? state)
        {
            var errors = new List<ConfigurationError>();
            if (state is null)
            {
                errors.Add(new ConfigurationError { Path = "$", Message = "required state object" });
                return errors;
            }

            if (state.SchemaVersion != 1)
            {
                errors.Add(new ConfigurationError
                {
                    Path = "schemaVersion",
                    Message = "only schema version 1 is supported; document must not be overwritten"
                });
            }

            if (state.Mode is not ("manual" or "automatic"))
            {
                errors.Add(new ConfigurationError { Path = "mode", Message = "must be one of: manual, automatic" });
            }

            Required(state.ManualProfile, "manualProfile", errors);
            Required(state.LastSelectedProfile, "lastSelectedProfile", errors);
            if (state.UpdatedAt is null)
            {
                errors.Add(new ConfigurationError { Path = "updatedAt", Message = "required timestamp" });
            }

            return errors;
        }

        public static IReadOnlyList<ConfigurationError> ValidateProfileReferences(
            SelectionStateDocument state,
            SettingsDocument settings)
        {
            ArgumentNullException.ThrowIfNull(state);
            ArgumentNullException.ThrowIfNull(settings);
            var profileIds = settings.Profiles!
                .Select(profile => profile.Id!)
                .ToHashSet(StringComparer.Ordinal);
            var errors = new List<ConfigurationError>();
            ProfileReference(state.ManualProfile, "manualProfile", profileIds, errors);
            ProfileReference(state.LastSelectedProfile, "lastSelectedProfile", profileIds, errors);
            return errors;
        }

        private static void Required(string? value, string path, List<ConfigurationError> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add(new ConfigurationError { Path = path, Message = "required non-empty string" });
            }
        }

        private static void ProfileReference(
            string? value,
            string path,
            HashSet<string> profileIds,
            List<ConfigurationError> errors)
        {
            if (value is not null && !profileIds.Contains(value))
            {
                errors.Add(new ConfigurationError { Path = path, Message = $"unknown profile '{value}'" });
            }
        }
    }
}
