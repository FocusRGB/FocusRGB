using System.Security.Cryptography;
using System.Text.Json;

namespace FocusRgb.LocalMcp
{
    /// <summary>Owns the separate, UI-controlled local MCP switch and authentication credential.</summary>
    public sealed class McpConfigurationStore
    {
        public const string FileName = "mcp.json";
        private readonly string _path;

        public McpConfigurationStore(string dataDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
            DataDirectory = ApplicationDataDirectory.Normalize(dataDirectory);
            _path = Path.Combine(DataDirectory, FileName);
        }

        public string DataDirectory { get; }

        public async Task<McpConfigurationResult> LoadAsync(CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_path)) return McpConfigurationResult.Ok(McpConfiguration.Disabled());
            try
            {
                var json = await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false);
                var configuration = JsonSerializer.Deserialize<McpConfiguration>(json, JsonOptions);
                return Validate(configuration);
            }
            catch (JsonException exception)
            {
                return McpConfigurationResult.Fail($"Invalid {FileName}: {exception.Message}");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return McpConfigurationResult.Fail($"Could not read {FileName}: {exception.Message}");
            }
        }

        public async Task<McpConfigurationResult> SetEnabledAsync(
            bool enabled,
            string? applicationPath = null,
            CancellationToken cancellationToken = default)
        {
            var loaded = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var credential = loaded.Configuration?.Credential;
            if (enabled && string.IsNullOrWhiteSpace(credential))
                credential = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var configuration = new McpConfiguration
            {
                SchemaVersion = 1,
                Enabled = enabled,
                Credential = credential,
                ApplicationPath = applicationPath ?? loaded.Configuration?.ApplicationPath
            };
            var validation = Validate(configuration);
            if (!validation.IsSuccess) return validation;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temporary = _path + ".tmp";
                await File.WriteAllTextAsync(
                    temporary,
                    JsonSerializer.Serialize(configuration, JsonOptions),
                    cancellationToken).ConfigureAwait(false);
                File.Move(temporary, _path, true);
                return McpConfigurationResult.Ok(configuration);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return McpConfigurationResult.Fail($"Could not write {FileName}: {exception.Message}");
            }
        }

        private static McpConfigurationResult Validate(McpConfiguration? configuration)
        {
            if (configuration?.SchemaVersion != 1)
                return McpConfigurationResult.Fail($"{FileName} must use schemaVersion 1.");
            if (configuration.Enabled && !IsCredential(configuration.Credential))
                return McpConfigurationResult.Fail($"Enabled {FileName} requires a valid credential.");
            if (configuration.Enabled && (string.IsNullOrWhiteSpace(configuration.ApplicationPath)
                || !Path.IsPathFullyQualified(configuration.ApplicationPath)))
                return McpConfigurationResult.Fail($"Enabled {FileName} requires the full FocusRGB executable path.");
            return McpConfigurationResult.Ok(configuration);
        }

        private static bool IsCredential(string? value)
        {
            try
            {
                return value is not null && Convert.FromBase64String(value).Length == 32;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
    }

    public sealed class McpConfiguration
    {
        public int SchemaVersion { get; init; }
        public bool Enabled { get; init; }
        public string? Credential { get; init; }
        public string? ApplicationPath { get; init; }

        public static McpConfiguration Disabled() => new() { SchemaVersion = 1 };
    }

    public sealed class McpConfigurationResult
    {
        public bool IsSuccess => Configuration is not null;
        public McpConfiguration? Configuration { get; private init; }
        public string? ErrorMessage { get; private init; }

        public static McpConfigurationResult Ok(McpConfiguration configuration) => new()
        {
            Configuration = configuration
        };

        public static McpConfigurationResult Fail(string message) => new() { ErrorMessage = message };
    }
}
