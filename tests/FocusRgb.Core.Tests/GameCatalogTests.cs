using FocusRgb.Core.Configuration;

namespace FocusRgb.Core.Tests
{
    public sealed class GameCatalogTests
    {
        private static readonly DateTimeOffset RefreshedAt =
            new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

        [Theory]
        [InlineData("[]", false)]
        [InlineData("[\"../Bad.exe\"]", false)]
        [InlineData("[\"Main.exe\",\"MAIN.EXE\"]", false)]
        [InlineData("[\"Main.exe\"]", true)]
        public void SelectedExecutables_ValidateAndRoundTrip(string selectedJson, bool valid)
        {
            var game = new GameCatalogEntry
            {
                Launcher = "example", Id = "game", DisplayName = "Game", Available = true,
                Executables = ["Main.exe", "Tool.exe"], LastRefreshedAt = RefreshedAt,
                SelectedExecutables = System.Text.Json.JsonSerializer.Deserialize<List<string>>(selectedJson)
            };
            var loaded = GameCatalogJson.Load(GameCatalogJson.Serialize(Catalog(game)));
            Assert.Equal(valid, loaded.IsSuccess);
            if (valid)
            {
                var saved = Assert.Single(loaded.Catalog!.Games!);
                Assert.Equal(["Main.exe"], saved.MatchingExecutables);
                Assert.Equal(["Main.exe", "Tool.exe"], saved.Executables);
                Assert.False(GameCatalogJson.HasSameContent(Catalog(game), Catalog(Game("example", "game", "Game", "Main.exe", "Tool.exe"))));
            }
        }

        [Fact]
        public void Validate_AllowsDuplicateTitlesAcrossLauncherScopedIdentities()
        {
            var catalog = Catalog(
                Game("launcher-a", "shared-id", "Same title", "Alpha.exe"),
                Game("launcher-b", "shared-id", "Same title", "Beta.exe"));

            Assert.Empty(GameCatalogJson.Validate(catalog));
        }

        [Fact]
        public void Validate_RejectsDuplicateIdentityPathsAndExecutableNames()
        {
            var catalog = Catalog(
                Game("example", "sample-game", "First", @"C:\Games\Sample.exe", "SAMPLE.EXE"),
                Game("example", "sample-game", "Second", "Other.exe"));

            var errors = GameCatalogJson.Validate(catalog);

            Assert.Contains(errors, error => error.Message == "duplicate ordinal game identity");
            Assert.Contains(errors, error => error.Message == "must be a file name, not a path");
        }

        [Theory]
        [InlineData("*", "sample-game")]
        [InlineData("steam", "*")]
        public void SettingsValidator_RejectsPartialGameWildcard(string launcher, string id)
        {
            var settings = SettingsJson.LoadDefaults().Settings!;
            settings.Rules!.Add(new RuleSettings
            {
                Id = "invalid-game",
                Profile = "movie",
                Match = new ApplicationMatch { Game = new GameMatch { Launcher = launcher, Id = id } },
                Eligibility = "while-running",
                Enabled = true
            });

            Assert.Contains(SettingsValidator.Validate(settings), error =>
                error.Path == $"rules[{settings.Rules.Count - 1}].match.game"
                && error.Message.Contains("(*, *)", StringComparison.Ordinal));
        }

        [Fact]
        public void Serialize_RoundTripsManualEntryWithoutARefreshTimestamp()
        {
            var catalog = Catalog(new GameCatalogEntry
            {
                Launcher = GameCatalogJson.ManualLauncher,
                Id = "local-game",
                DisplayName = "Local game",
                Executables = ["Local.exe"],
                Available = true
            });

            var loaded = GameCatalogJson.Load(GameCatalogJson.Serialize(catalog));

            Assert.True(loaded.IsSuccess);
            Assert.Equal("local-game", Assert.Single(loaded.Catalog!.Games!).Id);
        }

        private static GameCatalogDocument Catalog(params GameCatalogEntry[] games) => new()
        {
            SchemaVersion = GameCatalogJson.CurrentSchemaVersion,
            Games = [.. games]
        };

        private static GameCatalogEntry Game(
            string launcher,
            string id,
            string displayName,
            params string[] executables) => new()
            {
                Launcher = launcher,
                Id = id,
                DisplayName = displayName,
                Executables = [.. executables],
                Available = true,
                LastRefreshedAt = RefreshedAt
            };
    }
}
