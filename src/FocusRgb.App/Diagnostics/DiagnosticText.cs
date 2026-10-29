using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FocusRgb.App.Diagnostics
{
    /// <summary>Redacts before retaining diagnostic text; raw settings and log files are never projected.</summary>
    internal static partial class DiagnosticText
    {
        internal const int MaximumResponseBytes = 32 * 1024;

        public static string? Clean(string? value, int maximumLength = 256)
        {
            if (value is null)
            {
                return null;
            }

            if (value.Length > 16 * 1024)
            {
                return "[oversized text omitted]";
            }

            value = string.Concat(value.EnumerateRunes()
                .Where(rune => Rune.GetUnicodeCategory(rune) is not (UnicodeCategory.Control or UnicodeCategory.Format))
                .Select(rune => rune.ToString()));
            if (SensitiveMarker().IsMatch(value))
            {
                return "[redacted sensitive message]";
            }

            if (PathOrUrl().IsMatch(value))
            {
                return "[redacted path-bearing message]";
            }

            value = OpaqueToken().Replace(value, match => match.Value.Any(char.IsDigit)
                || match.Value.Any(character => character is '+' or '/' or '=' or '_') ? "[redacted token]" : match.Value);
            var clean = new StringBuilder(Math.Min(value.Length, maximumLength));
            foreach (var rune in value.EnumerateRunes())
            {
                if (clean.Length + rune.Utf16SequenceLength > maximumLength)
                {
                    break;
                }

                clean.Append(rune.ToString());
            }
            return clean.ToString();
        }

        public static JsonElement Serialize(object value)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonSerializerOptions.Web);
            return bytes.Length <= MaximumResponseBytes
                ? JsonSerializer.Deserialize<JsonElement>(bytes)
                : JsonSerializer.SerializeToElement(new { status = "truncated", message = "Diagnostic output limit reached; request fewer items." });
        }

        [GeneratedRegex(@"token|credential|password|passwd|secret|authorization|bearer|api[-_ ]?key|access[-_ ]?key|cookie|connection[-_ ]?string", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
        private static partial Regex SensitiveMarker();
        [GeneratedRegex(@"[/\\]|(?<![A-Za-z0-9_])[A-Za-z]:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
        private static partial Regex PathOrUrl();
        [GeneratedRegex(@"[A-Za-z0-9_+/=-]{24,}", RegexOptions.CultureInvariant, 100)]
        private static partial Regex OpaqueToken();
    }
}
