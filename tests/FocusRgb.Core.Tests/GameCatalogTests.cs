using FocusRgb.Core.Configuration;

namespace FocusRgb.Core.Tests
{
    public sealed class GameCatalogTests
    {
        private static readonly DateTimeOffset RefreshedAt =
            new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Validate_AllowsDuplicateTitlesAcrossLauncherScopedIdentities()
        {
            var catalog = Catalog(
                Game("steam", "570", "Same title", "Dota.exe"),
                Game("epic", "570", "Same title", "Fortnite.exe"));

            Assert.Empty(GameCatalogJson.Validate(catalog));
        }

        [Fact]
        public void Validate_RejectsDuplicateIdentityPathsAndExecutableNames()
        {
            var catalog = Catalog(
                Game("steam", "570", "First", @"C:\Games\Dota.exe", "DOTA.EXE"),
                Game("steam", "570", "Second", "Other.exe"));

            var errors = GameCatalogJson.Validate(catalog);

            Assert.Contains(errors, error => error.Message == "duplicate ordinal game identity");
            Assert.Contains(errors, error => error.Message == "must be a file name, not a path");
        }

        [Theory]
        [InlineData("*", "570")]
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
