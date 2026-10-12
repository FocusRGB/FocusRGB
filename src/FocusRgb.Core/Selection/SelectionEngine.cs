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
        private string? _stableForeground;
        private string? _pendingForeground;
        private DateTimeOffset _pendingSince;
        private bool _hasPendingForeground;

        public SelectionEngine(SettingsDocument settings)
        {
            var errors = SettingsValidator.Validate(settings);
            if (errors.Count > 0)
            {
                throw new ArgumentException("Selection requires validated settings.", nameof(settings));
            }

            _settings = settings;
        }

        /// <summary>Evaluates one observation at the supplied time and returns only desired profile state.</summary>
        public SelectionDecision Select(
            SelectionMode mode,
            string manualProfileId,
            ApplicationObservation observation,
            DateTimeOffset observedAt)
        {
            ArgumentNullException.ThrowIfNull(observation);

            if (mode == SelectionMode.Manual)
            {
                EnsureKnownProfile(manualProfileId);
                return new SelectionDecision
                {
                    ProfileId = manualProfileId,
                    Reason = SelectionReason.Manual()
                };
            }

            UpdateStableForeground(observation.ForegroundExecutable, observedAt);

            var running = new HashSet<string>(
                observation.RunningExecutables.Where(name => !string.IsNullOrWhiteSpace(name)),
                ExecutableComparer);

            var winner = _settings.Rules!
                .Select((rule, index) => new { Rule = rule, Index = index })
                .Where(candidate => IsEligible(candidate.Rule, running))
                .OrderByDescending(candidate => candidate.Rule.Priority)
                .ThenBy(candidate => candidate.Index)
                .ThenBy(candidate => candidate.Rule.Id, StringComparer.Ordinal)
                .FirstOrDefault();

            if (winner is not null)
            {
                return new SelectionDecision
                {
                    ProfileId = winner.Rule.Profile!,
                    Reason = SelectionReason.Rule(winner.Rule.Id!)
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
            if (rule.Enabled != true || rule.Match?.Executables is not { Count: > 0 } executables)
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

        private void UpdateStableForeground(string? foreground, DateTimeOffset observedAt)
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
                _pendingSince = observedAt;
                _hasPendingForeground = true;
            }
            else if (observedAt < _pendingSince)
            {
                _pendingSince = observedAt;
            }

            if (observedAt - _pendingSince < TimeSpan.FromMilliseconds(selection.DebounceMs!.Value))
            {
                return;
            }

            _stableForeground = _pendingForeground;
            _hasPendingForeground = false;
        }
    }
}
