using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FocusRgb.Platform.Windows
{
    internal interface IWindowsProcessSource
    {
        IReadOnlyCollection<string> GetRunningExecutables();
        string? GetForegroundExecutable();
    }

    internal sealed class WindowsProcessSource : IWindowsProcessSource
    {
        public IReadOnlyCollection<string> GetRunningExecutables()
        {
            var executables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        executables.Add(ToExecutableName(process.ProcessName));
                    }
                    catch (Exception exception) when (IsExpectedProcessFailure(exception))
                    {
                        // Processes can exit or deny access while the snapshot is being read.
                    }
                }
            }

            return executables.ToArray();
        }

        public string? GetForegroundExecutable()
        {
            var window = GetForegroundWindow();
            if (window == nint.Zero)
            {
                return null;
            }

            _ = GetWindowThreadProcessId(window, out var processId);
            if (processId == 0 || processId > int.MaxValue)
            {
                return null;
            }

            try
            {
                using var process = Process.GetProcessById((int)processId);
                return ToExecutableName(process.ProcessName);
            }
            catch (Exception exception) when (IsExpectedProcessFailure(exception))
            {
                return null;
            }
        }

        internal static string ToExecutableName(string processName) =>
            processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName
                : processName + ".exe";

        private static bool IsExpectedProcessFailure(Exception exception) =>
            exception is ArgumentException
                or InvalidOperationException
                or UnauthorizedAccessException
                or Win32Exception;

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern nint GetForegroundWindow();

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    }
}
