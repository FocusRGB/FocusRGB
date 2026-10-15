using System.Text.Json;

namespace FocusRgb.Core.Configuration
{
    internal static class ConfigurationJson
    {
        public static JsonSerializerOptions Options { get; } = new()
        {
            AllowTrailingCommas = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true
        };
    }
}
