using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FocusRgb.Platform.Windows
{
    internal readonly record struct ForegroundProcess(string? Executable, bool PreservePrevious);

    internal interface IWindowsProcessSource
    {
        IReadOnlyCollection<string> GetRunningExecutables();
        ForegroundProcess GetForegroundProcess();
    }

    internal sealed class WindowsProcessSource : IWindowsProcessSource
    {
        private static readonly HashSet<string> IgnoredWindowClasses = new(StringComparer.Ordinal)
        {
            // Shell overlays are navigation, not app changes: keep the real app and its debounce.
            "MultitaskingViewFrame",
            "MultitaskingView",
            "XamlExplorerHostIslandWindow",
            "XamlExplorerHost",
            "ForegroundStaging",
            "TaskView",
            "Progman",
            "Shell_SecondaryTrayWnd",
            "Shell_TrayWnd",
            "WorkerW"
        };

        private static readonly HashSet<string> StartExecutables = new(StringComparer.OrdinalIgnoreCase)
        {
            "StartMenuExperienceHost.exe", "SearchHost.exe", "SearchApp.exe"
        };

        private readonly uint _currentProcessId = (uint)Environment.ProcessId;
        private nint _lastForegroundWindow;

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

        public ForegroundProcess GetForegroundProcess()
        {
            var window = GetForegroundWindow();
            if (window == nint.Zero)
            {
                return new ForegroundProcess(null, false);
            }

            _ = GetWindowThreadProcessId(window, out var processId);
            if (processId == 0 || processId > int.MaxValue)
            {
                return new ForegroundProcess(null, false);
            }

            var windowClass = GetWindowClass(window);
            if (window != _lastForegroundWindow)
            {
                Debug.WriteLine($"Foreground window: class={windowClass}, processId={processId}");
                _lastForegroundWindow = window;
            }
            if (ShouldPreserveForeground(windowClass, processId, _currentProcessId))
            {
                return new ForegroundProcess(null, true);
            }

            try
            {
                var executable = GetExecutableName(processId);
                if (ShouldPreserveForeground(windowClass, processId, _currentProcessId, executable))
                {
                    return new ForegroundProcess(null, true);
                }
                if (!executable.Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
                {
                    return new ForegroundProcess(executable, false);
                }

                processId = SelectForegroundProcessId(executable, processId, GetChildWindows(window));
                return processId == _currentProcessId
                    ? new ForegroundProcess(null, true)
                    : new ForegroundProcess(GetExecutableName(processId), false);
            }
            catch (Exception exception) when (IsExpectedProcessFailure(exception))
            {
                return new ForegroundProcess(null, false);
            }
        }

        internal static uint SelectForegroundProcessId(
            string ownerExecutable,
            uint ownerProcessId,
            IEnumerable<(string WindowClass, uint ProcessId)> childWindows)
        {
            if (!ownerExecutable.Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
            {
                return ownerProcessId;
            }

            foreach (var child in childWindows)
            {
                if (child.WindowClass.Equals("Windows.UI.Core.CoreWindow", StringComparison.Ordinal)
                    && child.ProcessId != 0
                    && child.ProcessId != ownerProcessId)
                {
                    return child.ProcessId;
                }
            }

            return ownerProcessId;
        }

        internal static bool ShouldPreserveForeground(
            string windowClass,
            uint processId,
            uint currentProcessId,
            string? executable = null) =>
            processId == currentProcessId || IgnoredWindowClasses.Contains(windowClass)
            || executable is not null && (StartExecutables.Contains(executable)
                || windowClass == "Windows.UI.Core.CoreWindow"
                    && (executable.Equals("ShellExperienceHost.exe", StringComparison.OrdinalIgnoreCase)
                        || executable.Equals("ShellHost.exe", StringComparison.OrdinalIgnoreCase)));

        internal static string ToExecutableName(string processName) =>
            processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName
                : processName + ".exe";

        private static string GetExecutableName(uint processId)
        {
            using var process = Process.GetProcessById((int)processId);
            return ToExecutableName(process.ProcessName);
        }

        private static List<(string WindowClass, uint ProcessId)> GetChildWindows(nint parent)
        {
            var children = new List<(string WindowClass, uint ProcessId)>();
            _ = EnumChildWindows(parent, (window, parameter) =>
            {
                _ = GetWindowThreadProcessId(window, out var processId);
                children.Add((GetWindowClass(window), processId));
                return true;
            }, nint.Zero);
            return children;
        }

        private static string GetWindowClass(nint window)
        {
            var className = new char[256];
            var length = GetClassName(window, className, className.Length);
            return length > 0
                ? new string(className, 0, length)
                : string.Empty;
        }

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

        private delegate bool EnumWindowsProc(nint window, nint parameter);

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumChildWindows(nint parent, EnumWindowsProc callback, nint parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int GetClassName(nint window, [Out] char[] className, int maximumCount);
    }
}
