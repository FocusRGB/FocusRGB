using FocusRgb.Platform.Windows;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Selection;

namespace FocusRgb.Platform.Windows.Tests
{
    /// <summary>Checks deterministic process/focus snapshots without reading the test machine.</summary>
    public sealed class WindowsApplicationObserverTests
    {
        [Theory]
        [InlineData("fast", 300)]
        [InlineData("normal", 750)]
        [InlineData("smooth", 2000)]
        public void Observe_TransientKeepsPendingDebounceAndNextRealAppUsesConfiguredDelay(string speed, int delay)
        {
            var document = System.Text.Json.Nodes.JsonNode.Parse(SettingsJson.Serialize(SettingsJson.LoadDefaults().Settings!))!;
            document["application"]!["switchingSpeed"] = speed;
            foreach (var rule in document["rules"]!.AsArray())
            {
                rule!["eligibility"] = "while-focused";
            }
            var settings = SettingsJson.Load(document.ToJsonString()).Settings!;
            var source = new FakeProcessSource { Running = ["Code.exe", "Spotify.exe"], Foreground = "Code.exe" };
            var observer = new WindowsApplicationObserver(source);
            var clock = new TestClock();
            var engine = new SelectionEngine(settings, clock);
            var now = DateTimeOffset.UtcNow;
            _ = engine.Select(SelectionMode.Automatic, "default", observer.Observe(), now);
            clock.Advance(delay / 2);
            source.PreservePrevious = true;
            Assert.Equal("Code.exe", observer.Observe().ForegroundExecutable);
            _ = engine.Select(SelectionMode.Automatic, "default", observer.Observe(), now);
            clock.Advance(delay - delay / 2);
            Assert.Equal("programming", engine.Select(SelectionMode.Automatic, "default", observer.Observe(), now).ProfileId);
            source.PreservePrevious = false;
            source.Foreground = "Spotify.exe";
            Assert.Equal("programming", engine.Select(SelectionMode.Automatic, "programming", observer.Observe(), now).ProfileId);
            clock.Advance(delay - 1);
            Assert.Equal("programming", engine.Select(SelectionMode.Automatic, "programming", observer.Observe(), now).ProfileId);
            clock.Advance(1);
            Assert.Equal("music", engine.Select(SelectionMode.Automatic, "programming", observer.Observe(), now).ProfileId);
        }

        [Theory]
        [InlineData("MultitaskingViewFrame", "explorer.exe", true)]
        [InlineData("XamlExplorerHostIslandWindow", "explorer.exe", true)]
        [InlineData("ForegroundStaging", "explorer.exe", true)]
        [InlineData("Windows.UI.Core.CoreWindow", "ShellExperienceHost.exe", true)]
        [InlineData("Windows.UI.Core.CoreWindow", "ShellHost.exe", true)]
        [InlineData("Windows.UI.Core.CoreWindow", "Microsoft.Media.Player.exe", false)]
        [InlineData("OtherWindow", "ShellHost.exe", false)]
        [InlineData("Windows.UI.Core.CoreWindow", "StartMenuExperienceHost.exe", true)]
        [InlineData("OtherWindow", "SearchHost.exe", true)]
        [InlineData("ControlCenterWindow", "ShellHost.exe", true)]
        [InlineData("ControlCenterWindow", "Example.exe", false)]
        [InlineData("TopLevelWindowForOverflowXamlIsland", "explorer.exe", true)]
        [InlineData("TopLevelWindowForOverflowXamlIsland", "Example.exe", false)]
        [InlineData("CabinetWClass", "explorer.exe", false)]
        public void ShouldPreserveForeground_FiltersShellWithoutIgnoringRealCoreWindowApps(string windowClass, string executable, bool expected)
        {
            Assert.Equal(expected, WindowsProcessSource.ShouldPreserveForeground(windowClass, 10, 20, executable));
        }

        private sealed class TestClock : TimeProvider
        {
            private long _timestamp;
            public override long TimestampFrequency => 1000;
            public override long GetTimestamp() => _timestamp;
            public void Advance(int milliseconds) => _timestamp += milliseconds;
        }

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
        [InlineData("Avalonia-popup", 20, 20, true)]
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
