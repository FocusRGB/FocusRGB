using System.Globalization;

namespace FocusRgb.App.Diagnostics
{
    /// <summary>Writes the small local troubleshooting log enabled by the saved diagnostic setting.</summary>
    internal static class AppLog
    {
        // shortcut: synchronous unbounded writes suit the current three lifecycle events; use rolling async logs if event volume grows.
        private static string? _path;

        public static void Configure(string dataDirectory, bool enabled)
        {
            _path = enabled ? Path.Combine(dataDirectory, "logs", "focusrgb.log") : null;
        }

        public static void Write(string message)
        {
            if (_path is null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(
                    _path,
                    $"{DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)} {message}{Environment.NewLine}");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Logging must never prevent startup or shutdown.
            }
        }
    }
}
