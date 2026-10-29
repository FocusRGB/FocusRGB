namespace FocusRgb.App.Diagnostics
{
    internal sealed record DiagnosticLogLine(DateTimeOffset At, string Level, string Source, string Message);

    internal sealed class RecentLogBuffer
    {
        private readonly object _gate = new();
        private readonly Queue<DiagnosticLogLine> _lines = new();
        public const int Capacity = 200;
        private bool _enabled;

        public void SetEnabled(bool enabled)
        {
            lock (_gate)
            {
                _enabled = enabled;
                if (!enabled)
                {
                    _lines.Clear();
                    _lines.TrimExcess();
                }
            }
        }

        public void Add(string message, string level, string source, bool retainMessage = true)
        {
            lock (_gate)
            {
                if (!_enabled) return;
                var line = new DiagnosticLogLine(DateTimeOffset.UtcNow, DiagnosticText.Clean(level, 16)!,
                    DiagnosticText.Clean(source, 64)!, retainMessage ? DiagnosticText.Clean(message)! : "Plugin log event (payload omitted).");
                if (_lines.Count == Capacity)
                {
                    _lines.Dequeue();
                }

                _lines.Enqueue(line);
            }
        }

        public IReadOnlyList<DiagnosticLogLine> Read(int limit, string? level, string? source)
        {
            lock (_gate)
            {
                return _lines.Where(line => (level is null || line.Level.Equals(level, StringComparison.OrdinalIgnoreCase))
                    && (source is null || line.Source.Equals(source, StringComparison.Ordinal)))
                    .TakeLast(Math.Clamp(limit, 1, 51)).ToArray();
            }
        }
    }
}
