using FocusRgb.Core.Configuration;

namespace FocusRgb.Core.Selection
{
    /// <summary>
    /// Selects profiles from validated settings and caller-supplied observations without OS or output calls.
    /// </summary>
    public sealed class SelectionEngine
    {
        private static readonly StringComparer ExecutableComparer = StringComparer.OrdinalIgnoreCase;

        private readonly SettingsDocument _settings;
        private readonly IReadOnlyDictionary<GameIdentity, GameCatalogEntry> _games;
        private readonly TimeProvider _timeProvider;
        private string? _stableForeground;
        private string? _pendingForeground;
        private long _pendingSince;
        private bool _hasPendingForeground;
        private bool _hasStableObservation;
        private SelectionMode? _lastMode;

        public SelectionEngine(SettingsDocument settings, TimeProvider? timeProvider = null)
            : this(settings, GameCatalogJson.CreateDefault(), timeProvider)
        {
        }

        public SelectionEngine(
            SettingsDocument settings,
            GameCatalogDocument gameCatalog,
            TimeProvider? timeProvider = null)
        {
            var errors = SettingsValidator.Validate(settings);
            if (errors.Count > 0)
            {
                throw new ArgumentException("Selection requires validated settings.", nameof(settings));
            }

            _settings = SettingsJson.Load(SettingsJson.Serialize(settings)).Settings!;
            if (GameCatalogJson.Validate(gameCatalog).Count > 0)
                throw new ArgumentException("Selection requires a validated game catalog.", nameof(gameCatalog));
            _games = GameCatalogJson.Load(GameCatalogJson.Serialize(gameCatalog)).Catalog!.Games!
                .ToDictionary(game => new GameIdentity(game.Launcher!, game.Id!));
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>Evaluates one observation at the supplied time and returns only desired profile state.</summary>
        public SelectionDecision Select(
            SelectionMode mode,
            string currentProfileId,
            ApplicationObservation observation,
            DateTimeOffset observedAt)
        {
            ArgumentNullException.ThrowIfNull(observation);
            EnsureKnownProfile(currentProfileId);

            if (mode == SelectionMode.Manual)
            {
                _lastMode = SelectionMode.Manual;
                return new SelectionDecision
                {
                    ProfileId = currentProfileId,
                    Reason = SelectionReason.Manual()
                };
            }

            if (_lastMode != SelectionMode.Automatic)
            {
                ResetFocusTracking();
            }
            _lastMode = SelectionMode.Automatic;

            UpdateStableForeground(observation.ForegroundExecutable);

            var running = new HashSet<string>(
                observation.RunningExecutables.Where(name => !string.IsNullOrWhiteSpace(name)
                    && !_settings.Selection!.IgnoredExecutables!.Contains(name, ExecutableComparer)),
                ExecutableComparer);

            var winner = _settings.Rules!
                .Where(rule => IsEligible(rule, running))
                .FirstOrDefault();

            if (winner is not null)
            {
                _hasStableObservation = true;
                return new SelectionDecision
                {
                    ProfileId = winner.Profile!,
                    Reason = SelectionReason.Rule(winner.Id!)
                };
            }

            if (!_hasStableObservation)
            {
                return new SelectionDecision
                {
                    ProfileId = currentProfileId,
                    Reason = SelectionReason.ObservationPending()
                };
            }

            return new SelectionDecision
            {
                ProfileId = _settings.Selection!.DefaultProfile!,
                Reason = SelectionReason.UnmatchedDefault()
            };
        }

        private void EnsureKnownProfile(string profileId)
        {
            if (!_settings.Profiles!.Any(profile => string.Equals(profile.Id, profileId, StringComparison.Ordinal)))
            {
                throw new ArgumentException($"Unknown manual profile '{profileId}'.", nameof(profileId));
            }
        }

        private bool IsEligible(RuleSettings rule, HashSet<string> running)
        {
            var executables = ResolveExecutables(rule);
            if (rule.Enabled != true || executables is not { Count: > 0 })
            {
                return false;
            }

            if (rule.Eligibility == "while-running")
            {
                return executables.Any(running.Contains);
            }

            return _stableForeground is not null
                && running.Contains(_stableForeground)
                && executables.Contains(_stableForeground, ExecutableComparer);
        }

        private IReadOnlyCollection<string>? ResolveExecutables(RuleSettings rule)
        {
            if (rule.ApplicationGroup is not null)
            {
                return _settings.ApplicationGroups!
                    .FirstOrDefault(group => group.Id == rule.ApplicationGroup)?.Executables;
            }

            var game = rule.Match?.Game;
            if (game is null) return null;
            if (game.Launcher == "*" && game.Id == "*")
            {
                return _games.Values
                    .SelectMany(entry => entry.Executables ?? [])
                    .Distinct(ExecutableComparer)
                    .ToArray();
            }
            return _games.TryGetValue(new GameIdentity(game.Launcher!, game.Id!), out var entry)
                ? entry.Executables
                : null;
        }

        private void UpdateStableForeground(string? foreground)
        {
            var selection = _settings.Selection!;
            if (foreground is not null
                && selection.IgnoredExecutables!.Contains(foreground, ExecutableComparer))
            {
                return;
            }

            if (ExecutableComparer.Equals(foreground, _stableForeground))
            {
                _hasPendingForeground = false;
                return;
            }

            if (!_hasPendingForeground || !ExecutableComparer.Equals(foreground, _pendingForeground))
            {
                _pendingForeground = foreground;
                _pendingSince = _timeProvider.GetTimestamp();
                _hasPendingForeground = true;
            }

            if (_timeProvider.GetElapsedTime(_pendingSince) < TimeSpan.FromMilliseconds(selection.DebounceMs!.Value))
            {
                return;
            }

            _stableForeground = _pendingForeground;
            _hasPendingForeground = false;
            _hasStableObservation = true;
        }

        private void ResetFocusTracking()
        {
            _stableForeground = null;
            _pendingForeground = null;
            _pendingSince = default;
            _hasPendingForeground = false;
            _hasStableObservation = false;
        }
    }
}
