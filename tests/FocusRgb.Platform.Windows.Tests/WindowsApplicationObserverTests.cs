using FocusRgb.Platform.Windows;

namespace FocusRgb.Platform.Windows.Tests
{
    /// <summary>Checks deterministic process/focus snapshots without reading the test machine.</summary>
    public sealed class WindowsApplicationObserverTests
    {
        [Fact]
        public void Observe_ReportsWindowOwner_NotStremioHelper()
        {
            var observer = new WindowsApplicationObserver(new FakeProcessSource
            {
                Running = ["stremio-runtime.exe", "stremio-shell-ng.exe"],
                Foreground = "stremio-shell-ng.exe"
            });

            var observation = observer.Observe();

            Assert.Equal("stremio-shell-ng.exe", observation.ForegroundExecutable);
            Assert.Contains("stremio-runtime.exe", observation.RunningExecutables);
            Assert.Contains("stremio-shell-ng.exe", observation.RunningExecutables);
        }

        [Fact]
        public void Observe_OmitsForeground_WhenProcessDisappears()
        {
            var observer = new WindowsApplicationObserver(new FakeProcessSource
            {
                Running = ["Code.exe"],
                ForegroundError = new ArgumentException("Process has exited.")
            });

            var observation = observer.Observe();

            Assert.Null(observation.ForegroundExecutable);
            Assert.Equal(["Code.exe"], observation.RunningExecutables);
        }

        [Fact]
        public void Observe_ReturnsEmptyObservation_WhenApplicationCloses()
        {
            var observer = new WindowsApplicationObserver(new FakeProcessSource());

            var observation = observer.Observe();

            Assert.Null(observation.ForegroundExecutable);
            Assert.Empty(observation.RunningExecutables);
        }

        [Fact]
        public void Observe_KeepsForeground_WhenProcessListIsDenied()
        {
            var observer = new WindowsApplicationObserver(new FakeProcessSource
            {
                RunningError = new UnauthorizedAccessException(),
                Foreground = "Spotify.exe"
            });

            var observation = observer.Observe();

            Assert.Equal("Spotify.exe", observation.ForegroundExecutable);
            Assert.Equal(["Spotify.exe"], observation.RunningExecutables);
        }

        [Fact]
        public void Observe_ReportsIgnoredWindow_ForCoreToHandle()
        {
            var observer = new WindowsApplicationObserver(new FakeProcessSource
            {
                Running = ["Code.exe", "SearchHost.exe"],
                Foreground = "SearchHost.exe"
            });

            var observation = observer.Observe();

            Assert.Equal("SearchHost.exe", observation.ForegroundExecutable);
            Assert.Contains("Code.exe", observation.RunningExecutables);
        }

        [Fact]
        public void Observe_PreservesPreviousForeground_WhenHostOrShellHasFocus()
        {
            var processSource = new FakeProcessSource
            {
                Running = ["Code.exe"],
                Foreground = "Code.exe"
            };
            var observer = new WindowsApplicationObserver(processSource);
            _ = observer.Observe();
            processSource.PreservePrevious = true;
            processSource.Foreground = null;

            var observation = observer.Observe();

            Assert.Equal("Code.exe", observation.ForegroundExecutable);
            Assert.Equal(["Code.exe"], observation.RunningExecutables);
        }

        [Fact]
        public void SelectForegroundProcessId_UsesHostedCoreWindowProcess()
        {
            var processId = WindowsProcessSource.SelectForegroundProcessId(
                "ApplicationFrameHost.exe",
                10,
                [("OtherWindow", 11), ("Windows.UI.Core.CoreWindow", 12)]);

            Assert.Equal(12U, processId);
        }

        [Fact]
        public void SelectForegroundProcessId_KeepsDesktopProcess()
        {
            var processId = WindowsProcessSource.SelectForegroundProcessId(
                "Code.exe",
                10,
                [("Windows.UI.Core.CoreWindow", 12)]);

            Assert.Equal(10U, processId);
        }

        [Theory]
        [InlineData("Shell_TrayWnd", 10, 20, true)]
        [InlineData("Progman", 10, 20, true)]
        [InlineData("ApplicationFrameWindow", 20, 20, true)]
        [InlineData("ApplicationFrameWindow", 10, 20, false)]
        public void ShouldPreserveForeground_ReturnsExpectedResult(
            string windowClass,
            uint processId,
            uint currentProcessId,
            bool expected) =>
            Assert.Equal(
                expected,
                WindowsProcessSource.ShouldPreserveForeground(windowClass, processId, currentProcessId));

        [Theory]
        [InlineData("Code", "Code.exe")]
        [InlineData("Microsoft.Media.Player", "Microsoft.Media.Player.exe")]
        [InlineData("Spotify.exe", "Spotify.exe")]
        public void ToExecutableName_AddsExtensionOnce(string processName, string expected) =>
            Assert.Equal(expected, WindowsProcessSource.ToExecutableName(processName));

        private sealed class FakeProcessSource : IWindowsProcessSource
        {
            public IReadOnlyCollection<string> Running { get; set; } = [];
            public string? Foreground { get; set; }
            public bool PreservePrevious { get; set; }
            public Exception? RunningError { get; init; }
            public Exception? ForegroundError { get; init; }

            public IReadOnlyCollection<string> GetRunningExecutables() =>
                RunningError is null ? Running : throw RunningError;

            public ForegroundProcess GetForegroundProcess() =>
                ForegroundError is null
                    ? new ForegroundProcess(Foreground, PreservePrevious)
                    : throw ForegroundError;
        }
    }
}
