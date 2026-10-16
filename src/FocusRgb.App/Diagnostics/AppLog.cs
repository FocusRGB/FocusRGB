using System.Globalization;

namespace FocusRgb.App.Diagnostics
{
    /// <summary>Writes the small local troubleshooting log enabled by the saved diagnostic setting.</summary>
    internal static class AppLog
    {
        private const long MaxLogBytes = 1024 * 1024;
        private static readonly object Gate = new();
        private static string? _path;

        public static void Configure(string dataDirectory, bool enabled)
        {
            lock (Gate)
                _path = enabled ? Path.Combine(dataDirectory, "logs", "focusrgb.log") : null;
        }

        public static void Write(string message)
        {
            lock (Gate)
            {
                if (_path is null) return;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    if (File.Exists(_path) && new FileInfo(_path).Length >= MaxLogBytes)
                        File.Move(_path, _path + ".1", overwrite: true);
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
}
