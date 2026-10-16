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

        [Theory]
        [InlineData("Code", "Code.exe")]
        [InlineData("Spotify.exe", "Spotify.exe")]
        public void ToExecutableName_AddsExtensionOnce(string processName, string expected) =>
            Assert.Equal(expected, WindowsProcessSource.ToExecutableName(processName));

        private sealed class FakeProcessSource : IWindowsProcessSource
        {
            public IReadOnlyCollection<string> Running { get; init; } = [];
            public string? Foreground { get; init; }
            public Exception? RunningError { get; init; }
            public Exception? ForegroundError { get; init; }

            public IReadOnlyCollection<string> GetRunningExecutables() =>
                RunningError is null ? Running : throw RunningError;

            public string? GetForegroundExecutable() =>
                ForegroundError is null ? Foreground : throw ForegroundError;
        }
    }
}
