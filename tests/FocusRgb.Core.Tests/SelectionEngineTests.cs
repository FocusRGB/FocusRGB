using FocusRgb.Core.Configuration;
using FocusRgb.Core.Selection;

namespace FocusRgb.Core.Tests
{
    /// <summary>Checks deterministic profile selection without process, clock, or lighting dependencies.</summary>
    public sealed class SelectionEngineTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Select_UsesChosenExecutablesForSpecificAndWildcardRulesWhileOffline(bool wildcard)
        {
            var game = new GameCatalogEntry
            {
                Launcher = "example", Id = "game", DisplayName = "Game", Available = false,
                LastRefreshedAt = Start, Executables = ["Main.exe", "Tool.exe"], SelectedExecutables = ["Main.exe"]
            };
            var engine = new SelectionEngine(GameSettings(GameRule("game-focus", "movie", "while-running",
                wildcard ? "*" : "example", wildcard ? "*" : "game")), Catalog(game));
            Assert.Equal("default", engine.Select(SelectionMode.Automatic, "default", Observation("Tool.exe", "Tool.exe"), Start).ProfileId);
            Assert.Equal("movie", engine.Select(SelectionMode.Automatic, "default", Observation("MAIN.EXE", "MAIN.EXE"), Start).ProfileId);
        }

        [Fact]
        public void Select_AmbiguityOnlyIncludesChosenExecutables()
        {
            var first = new GameCatalogEntry
            {
                Launcher = "example", Id = "first", DisplayName = "First", Available = true,
                LastRefreshedAt = Start, Executables = ["Shared.exe", "Other.exe"], SelectedExecutables = ["Shared.exe"]
            };
            var second = new GameCatalogEntry
            {
                Launcher = "example", Id = "second", DisplayName = "Second", Available = true,
                LastRefreshedAt = Start, Executables = ["Shared.exe", "Other.exe"], SelectedExecutables = ["Other.exe"]
            };
            var engine = new SelectionEngine(GameSettings(GameRule("first-focus", "movie", "while-running", "example", "first")), Catalog(first, second));
            Assert.Equal("movie", engine.Select(SelectionMode.Automatic, "default", Observation("Shared.exe", "Shared.exe"), Start).ProfileId);
        }

        private static readonly DateTimeOffset Start = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

        [Theory]
        [InlineData("chrome.exe")]
        [InlineData("explorer.exe")]
        [InlineData(null)]
        public void Select_UnmappedForegroundRetainsTaskUntilItsOriginatingExecutableExits(string? foreground)
        {
            var clock = new TestTimeProvider();
            var engine = new SelectionEngine(Settings(
                Rule("code-focus", "programming", "while-focused", 100, "Code.exe"),
                Rule("background-music", "music", "while-running", 100, "Spotify.exe")), clock);
            Stabilize(engine, clock, "Code.exe", "Code.exe", "Spotify.exe");
            for (var index = 0; index < 5; index++)
            {
                clock.Advance(TimeSpan.FromSeconds(2));
                var retained = engine.Select(SelectionMode.Automatic, "programming",
                    Observation(index % 2 == 0 ? foreground : "Code.exe", "Code.exe", "Spotify.exe"), Start);
                Assert.Equal("programming", retained.ProfileId);
                Assert.Equal("code-focus", retained.Reason.RuleId);
                clock.Advance(TimeSpan.FromSeconds(2));
                var stable = engine.Select(SelectionMode.Automatic, "programming",
                    Observation(index % 2 == 0 ? foreground : "Code.exe", "Code.exe", "Spotify.exe"), Start);
                Assert.Equal("programming", stable.ProfileId);
            }
            var fallback = engine.Select(SelectionMode.Automatic, "programming", Observation(foreground, "Spotify.exe"), Start);
            Assert.Equal("music", fallback.ProfileId);
            Assert.Equal("background-music", fallback.Reason.RuleId);
            Assert.Equal("default", engine.Select(SelectionMode.Automatic, "music", Observation(foreground), Start).ProfileId);
        }

        [Fact]
        public void Select_UnmappedInterruptsPendingMappedTransitionWithoutBackgroundPreemption()
        {
            var clock = new TestTimeProvider();
            var engine = new SelectionEngine(Settings(
                Rule("code-focus", "programming", "while-focused", 100, "Code.exe"),
                Rule("movie-focus", "movie", "while-focused", 100, "Movie.exe"),
                Rule("music", "music", "while-running", 100, "Spotify.exe")), clock);
            Stabilize(engine, clock, "Code.exe", "Code.exe");
            var current = "programming";
            foreach (var foreground in new[] { "Movie.exe", "chrome.exe", "Movie.exe" })
            {
                var decision = engine.Select(SelectionMode.Automatic, current,
                    Observation(foreground, "Code.exe", "Movie.exe", "Spotify.exe"), Start);
                Assert.Equal(current, decision.ProfileId);
                clock.Advance(TimeSpan.FromMilliseconds(500));
            }
            var before = engine.Select(SelectionMode.Automatic, current, Observation("Movie.exe", "Code.exe", "Movie.exe", "Spotify.exe"), Start);
            Assert.Equal(current, before.ProfileId);
            clock.Advance(TimeSpan.FromMilliseconds(300));
            var after = engine.Select(SelectionMode.Automatic, current, Observation("Movie.exe", "Code.exe", "Movie.exe", "Spotify.exe"), Start);
            Assert.Equal("movie", after.ProfileId);
        }

        [Theory]
        [InlineData("default")]
        [InlineData("programming")]
        public void Select_UnmappedStartupDoesNotActivateBackgroundRules(string savedProfile)
        {
            var clock = new TestTimeProvider();
            var engine = new SelectionEngine(Settings(Rule("music", "music", "while-running", 100, "Spotify.exe")), clock);
            for (var index = 0; index < 4; index++)
            {
                var decision = engine.Select(SelectionMode.Automatic, savedProfile, Observation("chrome.exe", "Spotify.exe"), Start);
                Assert.Equal(savedProfile, decision.ProfileId);
                clock.Advance(TimeSpan.FromSeconds(5));
            }
        }

        [Fact]
        public void Select_NewHigherPriorityBackgroundRuleCannotPreemptUnmappedFocus()
        {
            var clock = new TestTimeProvider();
            var engine = new SelectionEngine(Settings(
                Rule("music", "music", "while-running", 100, "Spotify.exe"),
                Rule("code", "programming", "while-focused", 100, "Code.exe")), clock);
            Stabilize(engine, clock, "Code.exe", "Code.exe");
            for (var index = 0; index < 3; index++)
            {
                Assert.Equal("programming", engine.Select(SelectionMode.Automatic, "programming",
                    Observation("chrome.exe", "CODE.EXE", "Spotify.exe"), Start).ProfileId);
                clock.Advance(TimeSpan.FromSeconds(3));
            }
            Assert.Equal("music", engine.Select(SelectionMode.Automatic, "programming", Observation("chrome.exe", "Spotify.exe"), Start).ProfileId);
        }

        [Fact]
        public void Select_RetainedOwnerIsTheFocusedExecutableNotAnotherMemberOfItsGroup()
        {
            var clock = new TestTimeProvider();
            var engine = new SelectionEngine(Settings(Rule("code", "programming", "while-focused", 100, "Code.exe", "devenv.exe")), clock);
            Stabilize(engine, clock, "devenv.exe", "Code.exe", "devenv.exe");
            Assert.Equal("programming", engine.Select(SelectionMode.Automatic, "programming", Observation("chrome.exe", "Code.exe", "devenv.exe"), Start).ProfileId);
            Assert.Equal("default", engine.Select(SelectionMode.Automatic, "programming", Observation("chrome.exe", "Code.exe"), Start).ProfileId);
            Assert.Equal("default", engine.Select(SelectionMode.Automatic, "default", Observation("Code.exe", "Code.exe"), Start).ProfileId);
            clock.Advance(TimeSpan.FromSeconds(2));
            Assert.Equal("programming", engine.Select(SelectionMode.Automatic, "default", Observation("Code.exe", "Code.exe"), Start).ProfileId);
        }

        [Fact]
        public void Select_RetainsKnownGameButInvalidatesItWhenCatalogMatchIsRemoved()
        {
            var clock = new TestTimeProvider();
            var engine = new SelectionEngine(GameSettings(GameRule("game", "movie", "while-focused", "example", "first")),
                Catalog(Game("example", "first", "First", true, "Game.exe")), clock);
            Stabilize(engine, clock, "Game.exe", "Game.exe");
            Assert.Equal("movie", engine.Select(SelectionMode.Automatic, "movie", Observation("chrome.exe", "Game.exe"), Start).ProfileId);
            engine.UpdateGameCatalog(Catalog());
            Assert.Equal("default", engine.Select(SelectionMode.Automatic, "movie", Observation("chrome.exe", "Game.exe"), Start).ProfileId);
        }

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

        [Theory]
        [InlineData("SearchHost.exe")]
        [InlineData("StartMenuExperienceHost.exe")]
        public void Select_PreservesStableFocus_WhenIgnoredWindowIsForeground(string ignoredExecutable)
        {
            var clock = new TestTimeProvider();
            var engine = new SelectionEngine(Settings(
                Rule("programming", "programming", "while-focused", 100, "Code.exe")), clock);
            Stabilize(engine, clock, "Code.exe", "Code.exe", ignoredExecutable);

            var decision = engine.Select(SelectionMode.Automatic, "default",
                Observation(ignoredExecutable.ToUpperInvariant(), "Code.exe", ignoredExecutable), Start.AddSeconds(3));

            Assert.Equal("programming", decision.ProfileId);
        }

        [Fact]
        public void Select_RetainsSavedProfile_WhenStartupForegroundIsUnmapped()
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
            Assert.Equal("music", decision.ProfileId);
            Assert.Equal("observation-pending", decision.Reason.Kind);
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

        [Fact]
        public void Select_UsesOrdinalGameIdentityAndKeepsGenericRuleBelowExactRule()
        {
            var settings = GameSettings(
                GameRule("launcher-b-game", "movie", "while-running", "launcher-b", "shared-id"),
                GameRule("any-game", "music", "while-running", "*", "*"));
            var catalog = Catalog(
                Game("launcher-a", "shared-id", "Same title", false, "Alpha.exe"),
                Game("launcher-b", "shared-id", "Same title", true, "Beta.exe"));
            var engine = new SelectionEngine(settings, catalog);

            var alpha = engine.Select(SelectionMode.Automatic, "default",
                Observation("Alpha.exe", "Alpha.exe"), Start);
            var beta = engine.Select(SelectionMode.Automatic, "default",
                Observation("Beta.exe", "Beta.exe"), Start.AddSeconds(1));

            Assert.Equal("music", alpha.ProfileId);
            Assert.Equal("any-game", alpha.Reason.RuleId);
            Assert.Equal("movie", beta.ProfileId);
            Assert.Equal("launcher-b-game", beta.Reason.RuleId);
        }

        [Fact]
        public void Select_UsesCachedGameExecutables_WhenDiscoveryIsUnavailable()
        {
            var engine = new SelectionEngine(
                GameSettings(GameRule("offline-game", "movie", "while-running", "example", "sample-game")),
                Catalog(Game("example", "sample-game", "Offline game", false, "Sample.exe")));

            var decision = engine.Select(SelectionMode.Automatic, "default",
                Observation("SAMPLE.EXE", "sample.exe"), Start);

            Assert.Equal("movie", decision.ProfileId);
        }

        [Fact]
        public void Select_GameFocusRequiresObservedForegroundAndHonorsIgnoredExecutables()
        {
            var settings = GameSettings(GameRule("focused-game", "movie", "while-focused", "example", "sample-game"));
            settings = new SettingsDocument
            {
                SchemaVersion = settings.SchemaVersion,
                Application = settings.Application,
                Plugins = settings.Plugins,
                Selection = new SelectionSettings
                {
                    DefaultProfile = settings.Selection!.DefaultProfile,
                    Unmatched = settings.Selection.Unmatched,
                    DebounceMs = settings.Selection.DebounceMs,
                    IgnoredExecutables = [.. settings.Selection.IgnoredExecutables!, "IgnoredGame.exe"]
                },
                Profiles = settings.Profiles,
                ApplicationGroups = settings.ApplicationGroups,
                Rules = settings.Rules
            };
            var clock = new TestTimeProvider();
            var engine = new SelectionEngine(settings, Catalog(
                Game("example", "sample-game", "Game", true, "Game.exe", "IgnoredGame.exe")), clock);

            engine.Select(SelectionMode.Automatic, "default",
                Observation("explorer.exe", "Game.exe", "explorer.exe"), Start);
            clock.Advance(TimeSpan.FromSeconds(2));
            var background = engine.Select(SelectionMode.Automatic, "default",
                Observation("explorer.exe", "Game.exe", "explorer.exe"), Start.AddSeconds(2));
            var ignored = engine.Select(SelectionMode.Automatic, "default",
                Observation("IgnoredGame.exe", "IgnoredGame.exe"), Start.AddSeconds(3));

            Assert.Equal("default", background.ProfileId);
            Assert.Equal("default", ignored.ProfileId);
        }

        [Fact]
        public void Select_DoesNotUseSharedOrHelperExecutablesForSpecificGameRules()
        {
            var engine = new SelectionEngine(GameSettings(
                GameRule("first", "movie", "while-running", "example", "first"),
                GameRule("second", "music", "while-running", "example", "second")),
                Catalog(Game("example", "first", "First", true, "shared.exe", "UnityCrashHandler64.exe", "First.exe"),
                    Game("example", "second", "Second", true, "shared.exe", "Second.exe")));

            var shared = engine.Select(SelectionMode.Automatic, "default",
                Observation(null, "shared.exe", "UnityCrashHandler64.exe"), Start);
            var second = engine.Select(SelectionMode.Automatic, "default",
                Observation("Second.exe", "shared.exe", "Second.exe"), Start);

            Assert.Equal("default", shared.ProfileId);
            Assert.Equal("music", second.ProfileId);
        }

        [Fact]
        public void UpdateGameCatalog_PreservesPendingFocusDebounce()
        {
            var clock = new TestTimeProvider();
            var catalog = Catalog(Game("example", "sample", "Gáme", true, "Gáme.exe"));
            var engine = new SelectionEngine(GameSettings(
                GameRule("focused", "movie", "while-focused", "example", "sample")), catalog, clock);
            var observation = Observation("Gáme.exe", "Gáme.exe");
            engine.Select(SelectionMode.Automatic, "default", observation, Start);
            clock.Advance(TimeSpan.FromSeconds(1));

            engine.UpdateGameCatalog(catalog);
            clock.Advance(TimeSpan.FromSeconds(1));
            var selected = engine.Select(SelectionMode.Automatic, "default", observation, Start.AddSeconds(2));

            Assert.Equal("movie", selected.ProfileId);
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

        private static SettingsDocument GameSettings(params RuleSettings[] rules)
        {
            var settings = SettingsJson.LoadDefaults().Settings!;
            settings.Rules!.Clear();
            settings.Rules.AddRange(rules);
            return settings;
        }

        private static RuleSettings GameRule(
            string id,
            string profile,
            string eligibility,
            string launcher,
            string gameId) => new()
            {
                Id = id,
                Profile = profile,
                Match = new ApplicationMatch { Game = new GameMatch { Launcher = launcher, Id = gameId } },
                Eligibility = eligibility,
                Enabled = true
            };

        private static GameCatalogDocument Catalog(params GameCatalogEntry[] games) => new()
        {
            SchemaVersion = GameCatalogJson.CurrentSchemaVersion,
            Games = [.. games]
        };

        private static GameCatalogEntry Game(
            string launcher,
            string id,
            string displayName,
            bool available,
            params string[] executables) => new()
            {
                Launcher = launcher,
                Id = id,
                DisplayName = displayName,
                Executables = [.. executables],
                Available = available,
                UnavailableReason = available ? null : "Discovery plugin offline.",
                LastRefreshedAt = Start
            };

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
