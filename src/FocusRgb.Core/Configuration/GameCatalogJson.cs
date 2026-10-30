using System.Text.Json;
using System.Text.Json.Serialization;

namespace FocusRgb.Core.Configuration
{
    /// <summary>Serializes the app-owned cache of discovered and manually entered games.</summary>
    public static class GameCatalogJson
    {
        public const int CurrentSchemaVersion = 1;
        public const string ManualLauncher = "manual";

        public static GameCatalogLoadResult Load(string json)
        {
            ArgumentNullException.ThrowIfNull(json);
            try
            {
                var document = JsonSerializer.Deserialize<GameCatalogDocument>(json, ConfigurationJson.Options);
                var errors = Validate(document);
                return errors.Count == 0
                    ? GameCatalogLoadResult.Ok(document!)
                    : GameCatalogLoadResult.Fail(errors);
            }
            catch (JsonException exception)
            {
                return GameCatalogLoadResult.Fail([new ConfigurationError
                {
                    Path = exception.Path ?? "$",
                    Message = exception.Message
                }]);
            }
        }

        public static string Serialize(GameCatalogDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);
            return JsonSerializer.Serialize(document, ConfigurationJson.Options);
        }

        public static GameCatalogDocument CreateDefault() => new()
        {
            SchemaVersion = CurrentSchemaVersion,
            Games = []
        };

        /// <summary>Compares selection-relevant cache content, ignoring discovery timestamps and item order.</summary>
        public static bool HasSameContent(GameCatalogDocument left, GameCatalogDocument right) =>
            Content(left) == Content(right);

        private static string Content(GameCatalogDocument document) => JsonSerializer.Serialize(new
        {
            document.SchemaVersion,
            document.AdditionalProperties,
            Games = document.Games!.OrderBy(game => game.Launcher, StringComparer.Ordinal)
                .ThenBy(game => game.Id, StringComparer.Ordinal).Select(game => new
                {
                    game.Launcher, game.Id, game.DisplayName, game.Available,
                    game.UnavailableReason, game.AdditionalProperties,
                    Executables = game.Executables!.Order(StringComparer.OrdinalIgnoreCase),
                    SelectedExecutables = game.SelectedExecutables?.Order(StringComparer.OrdinalIgnoreCase)
                })
        }, ConfigurationJson.Options);

        public static IReadOnlyList<ConfigurationError> Validate(GameCatalogDocument? document)
        {
            var errors = new List<ConfigurationError>();
            if (document is null)
            {
                errors.Add(new ConfigurationError { Path = "$", Message = "required game catalog object" });
                return errors;
            }
            if (document.SchemaVersion != CurrentSchemaVersion)
            {
                errors.Add(new ConfigurationError
                {
                    Path = "schemaVersion",
                    Message = $"only schema version {CurrentSchemaVersion} is supported; document must not be overwritten"
                });
            }
            if (document.Games is null)
            {
                errors.Add(new ConfigurationError { Path = "games", Message = "required array" });
                return errors;
            }

            var identities = new HashSet<GameIdentity>();
            for (var index = 0; index < document.Games.Count; index++)
            {
                var game = document.Games[index];
                var path = $"games[{index}]";
                if (game is null)
                {
                    errors.Add(new ConfigurationError { Path = path, Message = "required game object" });
                    continue;
                }
                Required(game.Launcher, path + ".launcher", errors);
                Required(game.Id, path + ".id", errors);
                Required(game.DisplayName, path + ".displayName", errors);
                if (game.Launcher == "*" || game.Id == "*")
                {
                    errors.Add(new ConfigurationError { Path = path, Message = "wildcards are reserved for rules" });
                }
                if (game.Launcher is not null && game.Id is not null
                    && !identities.Add(new GameIdentity(game.Launcher, game.Id)))
                {
                    errors.Add(new ConfigurationError { Path = path, Message = "duplicate ordinal game identity" });
                }
                if (game.Executables is null)
                {
                    errors.Add(new ConfigurationError { Path = path + ".executables", Message = "required array" });
                }
                else
                {
                    if (game.Launcher == ManualLauncher && game.Executables.Count == 0)
                    {
                        errors.Add(new ConfigurationError
                        {
                            Path = path + ".executables",
                            Message = "manual games require at least one executable"
                        });
                    }
                    ValidateExecutables(game.Executables, path + ".executables", errors);
                }
                if (game.SelectedExecutables is { } selected)
                {
                    if (selected.Count == 0)
                        errors.Add(new ConfigurationError { Path = path + ".selectedExecutables", Message = "select at least one executable" });
                    ValidateExecutables(selected, path + ".selectedExecutables", errors);
                }
                if (game.Available is null)
                {
                    errors.Add(new ConfigurationError { Path = path + ".available", Message = "required boolean" });
                }
                if (game.Launcher == ManualLauncher && game.LastRefreshedAt is not null)
                {
                    errors.Add(new ConfigurationError { Path = path + ".lastRefreshedAt", Message = "must be null for manual games" });
                }
                if (game.Launcher != ManualLauncher && game.LastRefreshedAt is null)
                {
                    errors.Add(new ConfigurationError { Path = path + ".lastRefreshedAt", Message = "required for discovered games" });
                }
            }
            return errors;
        }

        private static void Required(string? value, string path, List<ConfigurationError> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add(new ConfigurationError { Path = path, Message = "required non-empty string" });
            }
        }

        private static void ValidateExecutables(List<string> executables, string path, List<ConfigurationError> errors)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < executables.Count; index++)
            {
                var name = executables[index];
                var error = string.IsNullOrWhiteSpace(name) ? "required non-empty file name"
                    : name.Contains('/') || name.Contains('\\') ? "must be a file name, not a path"
                    : !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? "must end in .exe"
                    : !names.Add(name) ? "duplicate executable name" : null;
                if (error is not null) errors.Add(new ConfigurationError { Path = $"{path}[{index}]", Message = error });
            }
        }
    }

    /// <summary>Stores cached discovery results and owner-created games outside settings.json.</summary>
    public sealed class GameCatalogDocument : ExtensibleConfiguration
    {
        public int? SchemaVersion { get; init; }
        public List<GameCatalogEntry>? Games { get; init; }
    }

    /// <summary>Identifies one game by an ordinal launcher and opaque ID, never by its title.</summary>
    public sealed class GameCatalogEntry : ExtensibleConfiguration
    {
        public string? Launcher { get; init; }
        public string? Id { get; init; }
        public string? DisplayName { get; init; }
        public List<string>? Executables { get; init; }
        public List<string>? SelectedExecutables { get; init; }
        [JsonIgnore]
        public IReadOnlyList<string> MatchingExecutables => SelectedExecutables ?? Executables ?? [];
        public bool? Available { get; init; }
        public string? UnavailableReason { get; init; }
        public DateTimeOffset? LastRefreshedAt { get; init; }
    }

    /// <summary>Compares launcher and opaque ID ordinally so same-titled games never collide.</summary>
    public readonly record struct GameIdentity(string Launcher, string Id);

    public sealed class GameCatalogLoadResult
    {
        public bool IsSuccess => Catalog is not null;
        public GameCatalogDocument? Catalog { get; private init; }
        public IReadOnlyList<ConfigurationError> Errors { get; private init; } = [];
        public static GameCatalogLoadResult Ok(GameCatalogDocument catalog) => new() { Catalog = catalog };
        public static GameCatalogLoadResult Fail(IReadOnlyList<ConfigurationError> errors) => new() { Errors = errors };
    }
}
