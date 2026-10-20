using System.Text.Json;
using System.Text.Json.Serialization;

namespace FocusRgb.Core.Protocol
{
    /// <summary>Provides the shared, provider-neutral JSON settings used on the plugin wire.</summary>
    public static class PluginJson
    {
        public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
}
