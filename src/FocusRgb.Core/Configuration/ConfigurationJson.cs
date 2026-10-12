using System.Text.Json;

namespace FocusRgb.Core.Configuration
{
    internal static class ConfigurationJson
    {
        public static JsonSerializerOptions Options { get; } = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
    }
}
