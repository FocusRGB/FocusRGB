using System.ComponentModel;
using FocusRgb.Core.Selection;

namespace FocusRgb.Platform.Windows
{
    /// <summary>Reads running executable names and the foreground window owner on Windows.</summary>
    public sealed class WindowsApplicationObserver
    {
        private static readonly StringComparer ExecutableComparer = StringComparer.OrdinalIgnoreCase;

        private readonly IWindowsProcessSource _processSource;

        public WindowsApplicationObserver()
            : this(new WindowsProcessSource())
        {
        }

        internal WindowsApplicationObserver(IWindowsProcessSource processSource)
        {
            _processSource = processSource;
        }

        /// <summary>
        /// Captures one best-effort observation; inaccessible or disappearing processes are omitted.
        /// </summary>
        public ApplicationObservation Observe()
        {
            var running = new HashSet<string>(ExecutableComparer);
            try
            {
                foreach (var executable in _processSource.GetRunningExecutables())
                {
                    if (!string.IsNullOrWhiteSpace(executable))
                    {
                        running.Add(executable);
                    }
                }
            }
            catch (Exception exception) when (IsExpectedProcessFailure(exception))
            {
                // A process-list race must not stop later observations or foreground detection.
            }

            string? foreground = null;
            try
            {
                foreground = _processSource.GetForegroundExecutable();
                if (!string.IsNullOrWhiteSpace(foreground))
                {
                    running.Add(foreground);
                }
                else
                {
                    foreground = null;
                }
            }
            catch (Exception exception) when (IsExpectedProcessFailure(exception))
            {
                // The foreground process can exit or deny access between the window and process reads.
            }

            return new ApplicationObservation
            {
                ForegroundExecutable = foreground,
                RunningExecutables = running.OrderBy(name => name, ExecutableComparer).ToArray()
            };
        }

        private static bool IsExpectedProcessFailure(Exception exception) =>
            exception is ArgumentException
                or InvalidOperationException
                or UnauthorizedAccessException
                or Win32Exception;
    }
}
