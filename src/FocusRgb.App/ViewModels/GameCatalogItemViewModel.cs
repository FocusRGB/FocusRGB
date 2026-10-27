using System.Collections.ObjectModel;
using FocusRgb.Core.Configuration;

namespace FocusRgb.App.ViewModels
{
    /// <summary>Projects one cached or manually entered launcher-scoped game.</summary>
    public sealed class GameCatalogItemViewModel
    {
        public GameCatalogItemViewModel(GameCatalogEntry game, IReadOnlySet<string>? ambiguousExecutables = null)
        {
            Launcher = game.Launcher!;
            Id = game.Id!;
            DisplayName = game.DisplayName!;
            Executables = new ObservableCollection<string>(game.Executables!);
            MatchingExecutables = game.MatchingExecutables.ToArray();
            Available = game.Available!.Value;
            UnavailableReason = game.UnavailableReason;
            LastRefreshedAt = game.LastRefreshedAt;
            var ambiguous = game.MatchingExecutables.Where(name => ambiguousExecutables?.Contains(name) == true).ToArray();
            HasAmbiguousExecutables = ambiguous.Length > 0;
            MatchingNotice = HasAmbiguousExecutables
                ? $"Shared executable names do not trigger this game's specific rules: {string.Join(", ", ambiguous)}. "
                    + "Use a unique game executable or an explicit application group."
                : string.Empty;
        }

        public string Launcher { get; }
        public string Id { get; }
        public string DisplayName { get; }
        public ObservableCollection<string> Executables { get; }
        public IReadOnlyList<string> MatchingExecutables { get; }
        public bool Available { get; }
        public string? UnavailableReason { get; }
        public DateTimeOffset? LastRefreshedAt { get; }
        public bool HasAmbiguousExecutables { get; }
        public string MatchingNotice { get; }
        public bool IsManual => Launcher == GameCatalogJson.ManualLauncher;
        public string Identity => $"{Launcher}:{Id}";
        public string Status => IsManual
            ? "Manual entry"
            : Available
                ? $"Discovered {LastRefreshedAt?.UtcDateTime:yyyy-MM-dd HH:mm} UTC"
                : $"Cached; discovery unavailable{(string.IsNullOrWhiteSpace(UnavailableReason) ? "." : $": {UnavailableReason}")}";
        public string DisplayLabel => $"{DisplayName} ({Identity})";
        public override string ToString() => DisplayLabel;
    }
}
