using System.ComponentModel;
using FocusRgb.Core.Selection;

namespace FocusRgb.Platform.Windows
{
    /// <summary>Reads running executable names and the foreground window owner on Windows.</summary>
    public sealed class WindowsApplicationObserver
    {
        private static readonly StringComparer ExecutableComparer = StringComparer.OrdinalIgnoreCase;

        private readonly IWindowsProcessSource _processSource;
        private string? _lastForegroundExecutable;
        private string[] _runningExecutables = [];

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
        public ApplicationObservation Observe(bool refreshRunningExecutables = true)
        {
            var running = new HashSet<string>(_runningExecutables, ExecutableComparer);
            try
            {
                if (refreshRunningExecutables)
                {
                    running.Clear();
                    foreach (var executable in _processSource.GetRunningExecutables())
                    {
                        if (!string.IsNullOrWhiteSpace(executable))
                        {
                            running.Add(executable);
                        }
                    }
                    _runningExecutables = running.ToArray();
                }
            }
            catch (Exception exception) when (IsExpectedProcessFailure(exception))
            {
                // A process-list race must not stop later observations or foreground detection.
            }

            string? foreground = null;
            ForegroundProcess foregroundProcess = default;
            try
            {
                foregroundProcess = _processSource.GetForegroundProcess();
                if (foregroundProcess.PreservePrevious)
                {
                    foreground = _lastForegroundExecutable;
                }
                else if (!string.IsNullOrWhiteSpace(foregroundProcess.Executable))
                {
                    foreground = foregroundProcess.Executable;
                    _lastForegroundExecutable = foreground;
                    running.Add(foreground);
                }
                else
                {
                    _lastForegroundExecutable = null;
                }
            }
            catch (Exception exception) when (IsExpectedProcessFailure(exception))
            {
                // The foreground process can exit or deny access between the window and process reads.
            }

            return new ApplicationObservation
            {
                ForegroundExecutable = foreground,
                ObservedForegroundExecutable = foregroundProcess.Executable,
                ForegroundWindowClass = foregroundProcess.WindowClass,
                IgnoredAsTransient = foregroundProcess.PreservePrevious,
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
