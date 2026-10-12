using System.Text.Json;

namespace FocusRgb.Core.Configuration
{
    /// <summary>Serializes and validates the native fallback snapshot separately from provider settings.</summary>
    public static class NativeSetupJson
    {
        public static NativeSetupLoadResult Load(string json)
        {
            ArgumentNullException.ThrowIfNull(json);
            try
            {
                var document = JsonSerializer.Deserialize<NativeSetupDocument>(json, ConfigurationJson.Options);
                var errors = Validate(document);
                return errors.Count == 0
                    ? NativeSetupLoadResult.Ok(document!)
                    : NativeSetupLoadResult.Fail(errors);
            }
            catch (JsonException exception)
            {
                return NativeSetupLoadResult.Fail([new ConfigurationError
                {
                    Path = exception.Path ?? "$",
                    Message = exception.Message
                }]);
            }
        }

        public static string Serialize(NativeSetupDocument nativeSetup)
        {
            ArgumentNullException.ThrowIfNull(nativeSetup);
            return JsonSerializer.Serialize(nativeSetup, ConfigurationJson.Options);
        }

        public static NativeSetupDocument CreateDefault() => new()
        {
            SchemaVersion = 1,
            Origin = "app-default",
            Bindings = []
        };

        public static IReadOnlyList<ConfigurationError> Validate(NativeSetupDocument? nativeSetup)
        {
            var errors = new List<ConfigurationError>();
            if (nativeSetup is null)
            {
                errors.Add(new ConfigurationError { Path = "$", Message = "required native setup object" });
                return errors;
            }

            if (nativeSetup.SchemaVersion != 1)
            {
                errors.Add(new ConfigurationError
                {
                    Path = "schemaVersion",
                    Message = "only schema version 1 is supported; document must not be overwritten"
                });
            }

            if (nativeSetup.Origin is not ("app-default" or "user-applied"))
            {
                errors.Add(new ConfigurationError
                {
                    Path = "origin",
                    Message = "must be one of: app-default, user-applied"
                });
            }
            else if (nativeSetup.Origin == "app-default" && nativeSetup.AppliedAt is not null)
            {
                errors.Add(new ConfigurationError { Path = "appliedAt", Message = "must be null for app-default" });
            }
            else if (nativeSetup.Origin == "user-applied" && nativeSetup.AppliedAt is null)
            {
                errors.Add(new ConfigurationError { Path = "appliedAt", Message = "required for user-applied" });
            }

            if (nativeSetup.Bindings is null)
            {
                errors.Add(new ConfigurationError { Path = "bindings", Message = "required array" });
                return errors;
            }

            var bindingIds = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < nativeSetup.Bindings.Count; i++)
            {
                var binding = nativeSetup.Bindings[i];
                var path = $"bindings[{i}]";
                if (binding is null)
                {
                    errors.Add(new ConfigurationError { Path = path, Message = "required binding object" });
                    continue;
                }

                Required(binding.Id, path + ".id", errors);
                Required(binding.Plugin, path + ".plugin", errors);
                if (binding.Id is not null && !bindingIds.Add(binding.Id))
                {
                    errors.Add(new ConfigurationError { Path = path + ".id", Message = $"duplicate binding ID '{binding.Id}'" });
                }
                if (binding.RenderingAuthority != "application")
                {
                    errors.Add(new ConfigurationError
                    {
                        Path = path + ".renderingAuthority",
                        Message = "must be application for native setup"
                    });
                }
                if (binding.DeviceScope is null)
                {
                    errors.Add(new ConfigurationError { Path = path + ".deviceScope", Message = "required object" });
                }
            }

            return errors;
        }

        private static void Required(string? value, string path, List<ConfigurationError> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add(new ConfigurationError { Path = path, Message = "required non-empty string" });
            }
        }
    }
}
