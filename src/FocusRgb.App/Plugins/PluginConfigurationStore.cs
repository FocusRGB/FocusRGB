using System.Text.Json;
using System.Text.Json.Serialization;

namespace FocusRgb.App.Plugins
{
    /// <summary>Loads a small package-declared field schema and persists plugin-owned values outside app settings.</summary>
    public sealed class PluginConfigurationStore : IDisposable
    {
        public const string SchemaFileName = "focusrgb-settings.schema.json";
        public const string SettingsFileName = "settings.json";
        private const int MaximumSchemaBytes = 256 * 1024;
        private const int MaximumSettingsBytes = 1024 * 1024;
        private readonly string _packagesDirectory;
        private readonly string _dataDirectory;
        private readonly SemaphoreSlim _writeGate = new(1, 1);

        public PluginConfigurationStore(string packagesDirectory, string dataDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(packagesDirectory);
            ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
            _packagesDirectory = Path.GetFullPath(packagesDirectory);
            _dataDirectory = Path.GetFullPath(dataDirectory);
        }

        public bool CanConfigure(string pluginId) => TryReadSchema(SchemaPath(pluginId), out _, out _);

        public void Dispose() => _writeGate.Dispose();

        public PluginConfigurationLoadResult Load(string pluginId)
        {
            if (!TryReadSchema(SchemaPath(pluginId), out var schema, out var error))
                return PluginConfigurationLoadResult.Fail(error!);
            try
            {
                var path = SettingsPath(pluginId);
                var values = Defaults(schema!).ToDictionary(
                    pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                if (File.Exists(path))
                {
                    if (new FileInfo(path).Length > MaximumSettingsBytes)
                        return PluginConfigurationLoadResult.Fail("The plugin settings file is too large.");
                    var saved = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                        File.ReadAllText(path), PluginJsonOptions) ?? [];
                    foreach (var value in saved) values[value.Key] = value.Value;
                }
                var validation = Validate(schema!, values);
                return validation is null
                    ? PluginConfigurationLoadResult.Ok(schema!, values)
                    : PluginConfigurationLoadResult.Fail(validation);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                return PluginConfigurationLoadResult.Fail(exception.Message);
            }
        }

        public async Task<string?> SaveAsync(
            string pluginId,
            PluginConfigurationSchema schema,
            IReadOnlyDictionary<string, JsonElement> values,
            CancellationToken cancellationToken = default)
        {
            _ = PackageDirectory(pluginId);
            var schemaError = ValidateSchema(schema);
            if (schemaError is not null) return schemaError;
            var validation = Validate(schema, values);
            if (validation is not null) return validation;
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var directory = Path.Combine(_dataDirectory, pluginId);
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, SettingsFileName);
                var temporary = path + ".tmp";
                try
                {
                    await File.WriteAllTextAsync(temporary,
                        JsonSerializer.Serialize(values, PluginJsonOptions), cancellationToken).ConfigureAwait(false);
                    File.Move(temporary, path, true);
                    return null;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return exception.Message;
                }
                finally
                {
                    try { File.Delete(temporary); }
                    catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
                }
            }
            finally
            {
                _writeGate.Release();
            }
        }

        public static IReadOnlyDictionary<string, JsonElement> Defaults(PluginConfigurationSchema schema) =>
            schema.Fields.ToDictionary(field => field.Id, field => field.Default.Clone(), StringComparer.Ordinal);

        internal static bool TryReadSchema(
            string path,
            out PluginConfigurationSchema? schema,
            out string? error)
        {
            schema = null;
            error = null;
            try
            {
                if (!File.Exists(path))
                {
                    error = $"The plugin does not contain {SchemaFileName}.";
                    return false;
                }
                if (new FileInfo(path).Length > MaximumSchemaBytes)
                {
                    error = "The plugin settings schema is too large.";
                    return false;
                }
                schema = JsonSerializer.Deserialize<PluginConfigurationSchema>(
                    File.ReadAllText(path), PluginJsonOptions);
                error = ValidateSchema(schema);
                return error is null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                error = exception.Message;
                return false;
            }
        }

        internal static string? Validate(
            PluginConfigurationSchema schema,
            IReadOnlyDictionary<string, JsonElement> values)
        {
            var unknown = values.Keys.Except(schema.Fields.Select(field => field.Id), StringComparer.Ordinal).FirstOrDefault();
            if (unknown is not null) return $"Unknown plugin setting '{unknown}'.";
            foreach (var field in schema.Fields)
            {
                if (!values.TryGetValue(field.Id, out var value)) return $"Plugin setting '{field.Id}' is required.";
                var error = ValidateValue(field, value);
                if (error is not null) return error;
            }
            return null;
        }

        private static string? ValidateSchema(PluginConfigurationSchema? schema)
        {
            if (schema?.SchemaVersion != 1) return "Only plugin settings schema version 1 is supported.";
            if (schema.Description?.Length > 512) return "The plugin settings description is too long.";
            if (schema.Fields is not { Count: >= 1 and <= 64 }) return "A plugin settings schema must contain 1 to 64 fields.";
            if (schema.Fields.Any(field => field is null)) return "Plugin settings fields must be objects.";
            if (schema.Fields.Select(field => field.Id).Distinct(StringComparer.Ordinal).Count() != schema.Fields.Count)
                return "Plugin setting IDs must be unique.";
            foreach (var field in schema.Fields)
            {
                if (!IsId(field.Id) || string.IsNullOrWhiteSpace(field.DisplayName) || field.DisplayName.Length > 128)
                    return "Plugin setting fields require a valid ID and display name.";
                if (field.Description?.Length > 512) return $"Description for '{field.Id}' is too long.";
                if (field.Type is not ("string" or "integer" or "number" or "boolean" or "json"))
                    return $"Plugin setting '{field.Id}' has an unsupported type.";
                if (field.Minimum > field.Maximum) return $"Plugin setting '{field.Id}' has an inverted range.";
                var error = ValidateValue(field, field.Default);
                if (error is not null) return $"Default {error}";
            }
            return null;
        }

        private static string? ValidateValue(PluginConfigurationField field, JsonElement value)
        {
            var validType = field.Type switch
            {
                "string" => value.ValueKind == JsonValueKind.String,
                "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
                "number" => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
                    && double.IsFinite(number),
                "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "json" => value.ValueKind is JsonValueKind.Object or JsonValueKind.Array,
                _ => false
            };
            if (!validType) return $"Plugin setting '{field.Id}' must be {field.Type}.";
            if (value.ValueKind == JsonValueKind.String && value.GetString()!.Length > 4096)
                return $"Plugin setting '{field.Id}' is too long.";
            if (value.GetRawText().Length > 8192)
                return $"Plugin setting '{field.Id}' exceeds its size limit.";
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var numeric)
                && (field.Minimum is { } minimum && numeric < minimum
                    || field.Maximum is { } maximum && numeric > maximum))
                return $"Plugin setting '{field.Id}' is outside its allowed range.";
            return null;
        }

        private string SchemaPath(string pluginId) => Path.Combine(PackageDirectory(pluginId), SchemaFileName);
        private string SettingsPath(string pluginId) => Path.Combine(_dataDirectory, pluginId, SettingsFileName);

        private string PackageDirectory(string pluginId)
        {
            if (!IsId(pluginId)) throw new ArgumentException("The plugin ID is invalid.", nameof(pluginId));
            return Path.Combine(_packagesDirectory, pluginId);
        }

        private static bool IsId(string? value) => value is { Length: >= 1 and <= 64 }
            && value[0] is >= 'a' and <= 'z' or >= '0' and <= '9'
            && value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

        private static JsonSerializerOptions PluginJsonOptions { get; } = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
    }

    public sealed class PluginConfigurationSchema
    {
        public int SchemaVersion { get; init; }
        public string? Description { get; init; }
        public List<PluginConfigurationField> Fields { get; init; } = [];
    }

    public sealed class PluginConfigurationField
    {
        public required string Id { get; init; }
        public required string DisplayName { get; init; }
        public string? Description { get; init; }
        public required string Type { get; init; }
        public required JsonElement Default { get; init; }
        public double? Minimum { get; init; }
        public double? Maximum { get; init; }
    }

    public sealed class PluginConfigurationLoadResult
    {
        public PluginConfigurationSchema? Schema { get; private init; }
        public IReadOnlyDictionary<string, JsonElement>? Values { get; private init; }
        public string? Error { get; private init; }
        public bool IsSuccess => Schema is not null && Values is not null;

        public static PluginConfigurationLoadResult Ok(
            PluginConfigurationSchema schema,
            IReadOnlyDictionary<string, JsonElement> values) => new() { Schema = schema, Values = values };

        public static PluginConfigurationLoadResult Fail(string error) => new() { Error = error };
    }
}
