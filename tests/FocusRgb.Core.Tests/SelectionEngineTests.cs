using FocusRgb.Core.Configuration;
using FocusRgb.Core.Selection;

namespace FocusRgb.Core.Tests
{
    /// <summary>Checks deterministic profile selection without process, clock, or lighting dependencies.</summary>
    public sealed class SelectionEngineTests
    {
        private static readonly DateTimeOffset Start = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Select_ReturnsFirstSavedEligibleRule_RegardlessOfLegacyPriority()
        {
            var settings = Settings(
                Rule("programming", "programming", "while-focused", 100, "Code.exe"),
                Rule("music", "music", "while-running", 200, "Spotify.exe"));
            var clock = new TestTimeProvider();
            var engine = new SelectionEngine(settings, clock);

            engine.Select(SelectionMode.Automatic, "default",
                Observation("Code.exe", "Code.exe", "Spotify.exe"), Start);
            clock.Advance(TimeSpan.FromSeconds(2));
            var decision = engine.Select(SelectionMode.Automatic, "default",
                Observation("Code.exe", "Code.exe", "Spotify.exe"), Start.AddSeconds(2));

            Assert.Equal("programming", decision.ProfileId);
            Assert.Equal("rule", decision.Reason.Kind);
            Assert.Equal("programming", decision.Reason.RuleId);
        }

        [Fact]
        public void Select_YieldsFocusOnlyRule_WhenNextAppKeepsFocusPastDebounce()
        {
            var clock = new TestTimeProvider();
            var engine = new SelectionEngine(Settings(
                Rule("programming", "programming", "while-focused", 200, "Code.exe"),
                Rule("movie", "movie", "while-focused", 100, "stremio-shell-ng.exe")), clock);
            Stabilize(engine, clock, "Code.exe", "Code.exe", "stremio-shell-ng.exe");

            var duringDebounce = engine.Select(SelectionMode.Automatic, "default",
                Observation("stremio-shell-ng.exe", "Code.exe", "stremio-shell-ng.exe"), Start.AddSeconds(3));
            clock.Advance(TimeSpan.FromSeconds(2));
            var afterDebounce = engine.Select(SelectionMode.Automatic, "default",
                Observation("stremio-shell-ng.exe", "Code.exe", "stremio-shell-ng.exe"), Start.AddSeconds(5));

            Assert.Equal("programming", duringDebounce.ProfileId);
            Assert.Equal("movie", afterDebounce.ProfileId);
        }

        [Fact]
        public void Select_RemovesEligibilityImmediately_WhenProcessExits()
        {
            var engine = new SelectionEngine(Settings(
                Rule("music", "music", "while-running", 100, "Spotify.exe")));
            var running = engine.Select(SelectionMode.Automatic, "default",
                Observation("Spotify.exe", "Spotify.exe"), Start);

            var exited = engine.Select(SelectionMode.Automatic, "default",
                Observation(null), Start.AddMilliseconds(1));

            Assert.Equal("music", running.ProfileId);
            Assert.Equal("default", exited.ProfileId);
            Assert.Equal("unmatched-default", exited.Reason.Kind);
            Assert.Null(exited.Reason.RuleId);
        }

        [Fact]
        public void Select_ReturnsManualProfile_WhenAutomaticRuleIsEligible()
        {
            var engine = new SelectionEngine(Settings(
                Rule("music", "music", "while-running", 100, "Spotify.exe")));

            var decision = engine.Select(SelectionMode.Manual, "movie",
                Observation("Spotify.exe", "Spotify.exe"), Start);

            Assert.Equal("movie", decision.ProfileId);
            Assert.Equal("manual", decision.Reason.Kind);
            Assert.Null(decision.Reason.RuleId);
        }

        [Fact]
        public void Select_ReturnsEarlierSavedRule_WhenSeveralRulesAreEligible()
        {
            var engine = new SelectionEngine(Settings(
                Rule("music", "music", "while-running", 100, "Spotify.exe"),
                Rule("programming", "programming", "while-running", 100, "Code.exe")));

            var decision = engine.Select(SelectionMode.Automatic, "default",
                Observation("Code.exe", "Code.exe", "Spotify.exe"), Start);

            Assert.Equal("music", decision.ProfileId);
            Assert.Equal("music", decision.Reason.RuleId);
        }

        [Fact]
        public void Select_KeepsStableRule_WhenFocusChangeIsBrief()
        {
            var clock = new TestTimeProvider();
            var engine = new SelectionEngine(Settings(
                Rule("programming", "programming", "while-focused", 100, "Code.exe"),
                Rule("movie", "movie", "while-focused", 100, "stremio-shell-ng.exe")), clock);
            Stabilize(engine, clock, "Code.exe", "Code.exe", "stremio-shell-ng.exe");

            engine.Select(SelectionMode.Automatic, "default",
                Observation("stremio-shell-ng.exe", "Code.exe", "stremio-shell-ng.exe"), Start.AddSeconds(3));
            var decision = engine.Select(SelectionMode.Automatic, "default",
                Observation("Code.exe", "Code.exe", "stremio-shell-ng.exe"), Start.AddSeconds(3.5));

            Assert.Equal("programming", decision.ProfileId);
        }

        [Fact]
        public void Select_PreservesStableFocus_WhenIgnoredWindowIsForeground()
        {
            var clock = new TestTimeProvider();
            var engine = new SelectionEngine(Settings(
                Rule("programming", "programming", "while-focused", 100, "Code.exe")), clock);
            Stabilize(engine, clock, "Code.exe", "Code.exe", "SearchHost.exe");

            var decision = engine.Select(SelectionMode.Automatic, "default",
                Observation("searchhost.EXE", "Code.exe", "SearchHost.exe"), Start.AddSeconds(3));

            Assert.Equal("programming", decision.ProfileId);
        }

        [Fact]
        public void Select_ReturnsDefault_WhenNoRuleIsEligible()
        {
            var clock = new TestTimeProvider();
            var engine = new SelectionEngine(Settings(
                Rule("programming", "programming", "while-focused", 100, "Code.exe")), clock);

            var pending = engine.Select(SelectionMode.Automatic, "music",
                Observation("explorer.exe", "explorer.exe"), Start.AddSeconds(10));
            clock.Advance(TimeSpan.FromSeconds(2));
            var decision = engine.Select(SelectionMode.Automatic, "music",
                Observation("explorer.exe", "explorer.exe"), Start.AddSeconds(12));

            Assert.Equal("music", pending.ProfileId);
            Assert.Equal("observation-pending", pending.Reason.Kind);
            Assert.Equal("default", decision.ProfileId);
            Assert.Equal("unmatched-default", decision.Reason.Kind);
        }

        [Fact]
        public void Select_RetainsRestoredProfile_UntilFirstFocusObservationIsStable()
        {
            var clock = new TestTimeProvider();
            var engine = new SelectionEngine(Settings(
                Rule("programming", "programming", "while-focused", 100, "Code.exe")), clock);

            var pending = engine.Select(SelectionMode.Automatic, "music",
                Observation("Code.exe", "Code.exe"), Start);
            clock.Advance(TimeSpan.FromSeconds(2));
            var stable = engine.Select(SelectionMode.Automatic, "music",
                Observation("Code.exe", "Code.exe"), Start.AddSeconds(2));

            Assert.Equal("music", pending.ProfileId);
            Assert.Equal("observation-pending", pending.Reason.Kind);
            Assert.Equal("programming", stable.ProfileId);
            Assert.Equal("rule", stable.Reason.Kind);
        }

        [Fact]
        public void Select_DebounceUsesMonotonicTimeAcrossWallClockJumps()
        {
            var clock = new TestTimeProvider();
            var engine = new SelectionEngine(Settings(
                Rule("programming", "programming", "while-focused", 100, "Code.exe")), clock);

            engine.Select(SelectionMode.Automatic, "music", Observation("Code.exe", "Code.exe"), Start);
            var forwardJump = engine.Select(
                SelectionMode.Automatic, "music", Observation("Code.exe", "Code.exe"), Start.AddDays(1));
            clock.Advance(TimeSpan.FromSeconds(2));
            var backwardJump = engine.Select(
                SelectionMode.Automatic, "music", Observation("Code.exe", "Code.exe"), Start.AddDays(-1));

            Assert.Equal("observation-pending", forwardJump.Reason.Kind);
            Assert.Equal("programming", backwardJump.ProfileId);
        }

        [Fact]
        public void Select_DiscardsOldFocus_WhenAutomaticModeResumes()
        {
            var clock = new TestTimeProvider();
            var engine = new SelectionEngine(Settings(
                Rule("programming", "programming", "while-focused", 100, "Code.exe"),
                Rule("movie", "movie", "while-focused", 100, "stremio-shell-ng.exe")), clock);
            Stabilize(engine, clock, "Code.exe", "Code.exe", "stremio-shell-ng.exe");
            engine.Select(SelectionMode.Manual, "movie",
                Observation("stremio-shell-ng.exe", "Code.exe", "stremio-shell-ng.exe"), Start.AddSeconds(3));

            var pending = engine.Select(SelectionMode.Automatic, "movie",
                Observation("stremio-shell-ng.exe", "Code.exe", "stremio-shell-ng.exe"), Start.AddSeconds(4));
            clock.Advance(TimeSpan.FromSeconds(2));
            var stable = engine.Select(SelectionMode.Automatic, "movie",
                Observation("stremio-shell-ng.exe", "Code.exe", "stremio-shell-ng.exe"), Start.AddSeconds(6));

            Assert.Equal("movie", pending.ProfileId);
            Assert.Equal("observation-pending", pending.Reason.Kind);
            Assert.Equal("movie", stable.ProfileId);
            Assert.Equal("rule", stable.Reason.Kind);
        }

        [Fact]
        public void Select_MatchesExecutableNamesIgnoringCase()
        {
            var engine = new SelectionEngine(Settings(
                Rule("music", "music", "while-running", 100, "Spotify.exe")));

            var decision = engine.Select(SelectionMode.Automatic, "default",
                Observation("SPOTIFY.EXE", "spotify.EXE"), Start);

            Assert.Equal("music", decision.ProfileId);
        }

        [Fact]
        public void Select_ThrowsForUnknownManualProfile_WhenCommandWasNotValidated()
        {
            var engine = new SelectionEngine(Settings());

            var error = Assert.Throws<ArgumentException>(() => engine.Select(
                SelectionMode.Manual, "missing", Observation(null), Start));

            Assert.Equal("profileId", error.ParamName);
        }

        [Fact]
        public void Constructor_SnapshotsValidatedSettings()
        {
            var settings = Settings(Rule("music", "music", "while-running", 100, "Spotify.exe"));
            var engine = new SelectionEngine(settings);
            settings.Rules!.Clear();

            var decision = engine.Select(SelectionMode.Automatic, "default",
                Observation("Spotify.exe", "Spotify.exe"), Start);

            Assert.Equal("music", decision.ProfileId);
        }

        private static SettingsDocument Settings(params RuleSettings[] rules)
        {
            var settings = SettingsJson.LoadDefaults().Settings!;
            settings.Rules!.Clear();
            settings.ApplicationGroups!.Clear();
            settings.ApplicationGroups.AddRange(rules.Select(rule => new ApplicationGroupSettings
            {
                Id = rule.Id,
                DisplayName = rule.Id,
                Executables = rule.Match!.Executables
            }));
            settings.Rules.AddRange(rules.Select(rule => new RuleSettings
            {
                Id = rule.Id,
                ApplicationGroup = rule.Id,
                Profile = rule.Profile,
                Eligibility = rule.Eligibility,
                Priority = rule.Priority,
                Enabled = rule.Enabled
            }));
            return settings;
        }

        private static RuleSettings Rule(
            string id,
            string profile,
            string eligibility,
            int priority,
            params string[] executables) => new()
            {
                Id = id,
                Profile = profile,
                Match = new ApplicationMatch { Executables = [.. executables] },
                Eligibility = eligibility,
                Priority = priority,
                Enabled = true
            };

        private static ApplicationObservation Observation(string? foreground, params string[] running) => new()
        {
            ForegroundExecutable = foreground,
            RunningExecutables = running
        };

        private static void Stabilize(
            SelectionEngine engine,
            TestTimeProvider clock,
            string foreground,
            params string[] running)
        {
            engine.Select(SelectionMode.Automatic, "default", Observation(foreground, running), Start);
            clock.Advance(TimeSpan.FromSeconds(2));
            engine.Select(SelectionMode.Automatic, "default", Observation(foreground, running), Start.AddSeconds(2));
        }

        private sealed class TestTimeProvider : TimeProvider
        {
            private long _timestamp;
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;
            public override long GetTimestamp() => _timestamp;
            public void Advance(TimeSpan elapsed) => _timestamp += elapsed.Ticks;
        }
    }
}
