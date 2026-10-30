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
        private IReadOnlyDictionary<GameIdentity, GameCatalogEntry> _games = new Dictionary<GameIdentity, GameCatalogEntry>();
        private HashSet<string> _ambiguousExecutables = new(ExecutableComparer);
        private readonly TimeProvider _timeProvider;
        private string? _stableForeground;
        private string? _pendingForeground;
        private long _pendingSince;
        private bool _hasPendingForeground;
        private bool _hasStableObservation;
        private SelectionMode? _lastMode;
        private RetainedFocus? _activeFocus;

        private sealed record RetainedFocus(string RuleId, string ProfileId, string Executable);

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
            UpdateGameCatalog(gameCatalog);
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>Updates cached identities while retaining the current foreground debounce.</summary>
        public void UpdateGameCatalog(GameCatalogDocument gameCatalog)
        {
            if (GameCatalogJson.Validate(gameCatalog).Count > 0)
            {
                throw new ArgumentException("Selection requires a validated game catalog.", nameof(gameCatalog));
            }
            _games = GameCatalogJson.Load(GameCatalogJson.Serialize(gameCatalog)).Catalog!.Games!
                .ToDictionary(game => new GameIdentity(game.Launcher!, game.Id!));
            _ambiguousExecutables = _games.Values.SelectMany(game => game.MatchingExecutables)
                .GroupBy(name => name, ExecutableComparer).Where(group => group.Count() > 1)
                .Select(group => group.Key).ToHashSet(ExecutableComparer);
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

            var running = new HashSet<string>(
                observation.RunningExecutables.Where(name => !string.IsNullOrWhiteSpace(name)
                    && !_settings.Selection!.IgnoredExecutables!.Contains(name, ExecutableComparer)),
                ExecutableComparer);

            if (_stableForeground is not null && !running.Contains(_stableForeground))
            {
                _stableForeground = null;
            }
            var foreground = observation.ForegroundExecutable;
            var ignored = foreground is not null && _settings.Selection!.IgnoredExecutables!.Contains(foreground, ExecutableComparer);
            var mapped = IsMappedForeground(ignored ? _stableForeground : foreground);
            RuleSettings? winner;
            if (!mapped)
            {
                // Utility windows neither replace the last task nor carry a pending mapped-app timer.
                _hasPendingForeground = false;
                _pendingForeground = null;
                if (_activeFocus is null)
                {
                    return new SelectionDecision { ProfileId = currentProfileId, Reason = SelectionReason.ObservationPending() };
                }
                if (SourceIsRunning(running))
                {
                    if (_activeFocus.ProfileId == currentProfileId)
                    {
                        return new SelectionDecision { ProfileId = currentProfileId, Reason = SelectionReason.Rule(_activeFocus.RuleId) };
                    }
                    _activeFocus = null;
                    return new SelectionDecision { ProfileId = currentProfileId, Reason = SelectionReason.ObservationPending() };
                }
                winner = _settings.Rules!.FirstOrDefault(rule => rule.Eligibility == "while-running" && IsEligible(rule, running));
            }
            else
            {
                UpdateStableForeground(foreground);
                winner = _settings.Rules!.FirstOrDefault(rule => IsEligible(rule, running));
            }

            if (winner is not null)
            {
                _hasStableObservation = true;
                var executables = ResolveExecutables(winner)!;
                var source = winner.Eligibility == "while-focused" ? _stableForeground!
                    : foreground is not null && running.Contains(foreground) && executables.Contains(foreground, ExecutableComparer)
                        ? foreground : executables.First(running.Contains);
                _activeFocus = new RetainedFocus(winner.Id!, winner.Profile!, source);
                return new SelectionDecision
                {
                    ProfileId = winner.Profile!,
                    Reason = SelectionReason.Rule(winner.Id!)
                };
            }

            if (SourceIsRunning(running) && _activeFocus!.ProfileId == currentProfileId)
            {
                return new SelectionDecision { ProfileId = currentProfileId, Reason = SelectionReason.Rule(_activeFocus.RuleId) };
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

        private bool IsMappedForeground(string? executable) => executable is not null
            && _settings.Rules!.Any(rule => rule.Enabled == true && ResolveExecutables(rule)?.Contains(executable, ExecutableComparer) == true);

        private bool SourceIsRunning(HashSet<string> running) => _activeFocus is { } active
            && running.Contains(active.Executable)
            && _settings.Rules!.Any(rule => rule.Id == active.RuleId && rule.Profile == active.ProfileId && rule.Enabled == true
                && ResolveExecutables(rule)?.Contains(active.Executable, ExecutableComparer) == true);

        internal void CopyRetainedFocusFrom(SelectionEngine previous)
        {
            _activeFocus = previous._activeFocus;
            _stableForeground = previous._stableForeground;
            _lastMode = previous._lastMode;
            _hasStableObservation = previous._hasStableObservation;
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
            if (game is null)
            {
                return null;
            }
            if (game.Launcher == "*" && game.Id == "*")
            {
                return _games.Values
                    .SelectMany(entry => entry.MatchingExecutables)
                    .Where(IsGameExecutable)
                    .Distinct(ExecutableComparer)
                    .ToArray();
            }
            return _games.TryGetValue(new GameIdentity(game.Launcher!, game.Id!), out var entry)
                ? entry.MatchingExecutables.Where(name => IsGameExecutable(name) && !_ambiguousExecutables.Contains(name)).ToArray()
                : null;
        }

        private static bool IsGameExecutable(string name) =>
            !name.StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("CrashReportClient", StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("vc_redist", StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("vcredist", StringComparison.OrdinalIgnoreCase)
            && !name.Equals("crashpad_handler.exe", StringComparison.OrdinalIgnoreCase)
            && !name.Equals("setup.exe", StringComparison.OrdinalIgnoreCase)
            && !name.Equals("dxsetup.exe", StringComparison.OrdinalIgnoreCase);

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

            if (_timeProvider.GetElapsedTime(_pendingSince) < TimeSpan.FromMilliseconds(
                SwitchingSpeedPresets.Milliseconds(_settings.Application!.SwitchingSpeed)))
            {
                return;
            }

            _stableForeground = _pendingForeground;
            _hasPendingForeground = false;
            _hasStableObservation = true;
        }

        public SelectionDebounceState GetDebounceState()
        {
            var delay = SwitchingSpeedPresets.Milliseconds(_settings.Application!.SwitchingSpeed);
            var pending = _lastMode == SelectionMode.Automatic && _hasPendingForeground;
            return new SelectionDebounceState(_stableForeground, pending ? _pendingForeground : null,
                pending, pending ? Math.Max(0, delay - (int)Math.Min(delay, _timeProvider.GetElapsedTime(_pendingSince).TotalMilliseconds)) : 0, delay);
        }

        private void ResetFocusTracking()
        {
            _stableForeground = null;
            _pendingForeground = null;
            _pendingSince = default;
            _hasPendingForeground = false;
            _hasStableObservation = false;
            _activeFocus = null;
        }
    }
}
