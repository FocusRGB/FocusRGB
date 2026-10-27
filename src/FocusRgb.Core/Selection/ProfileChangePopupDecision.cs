using FocusRgb.Core.Commands;

namespace FocusRgb.Core.Selection
{
    /// <summary>Tracks selection transitions, independently of output success or desktop APIs.</summary>
    public sealed class ProfileChangePopupDecision
    {
        private string? _previousProfile;

        public string? Update(ApplicationStateSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            var state = snapshot.SelectionState;
            var profileId = state.Mode == "manual" ? state.ManualProfile : state.LastSelectedProfile;
            var previous = _previousProfile;
            _previousProfile = profileId;
            if (previous is null || previous == profileId || state.Mode != "automatic"
                || snapshot.Source != "observation"
                || snapshot.Settings.Application?.ShowProfileChangePopup == false
                || snapshot.SelectionReason.Kind is not ("rule" or "unmatched-default"))
                return null;

            var settings = snapshot.Settings;
            var profile = settings.Profiles!.First(item => item.Id == profileId);
            var rule = settings.Rules!.FirstOrDefault(item => item.Id == snapshot.SelectionReason.RuleId);
            var group = settings.ApplicationGroups!.FirstOrDefault(item => item.Id == rule?.ApplicationGroup);
            var game = snapshot.GameCatalog.Games?.FirstOrDefault(item =>
                item.Launcher == rule?.Match?.Game?.Launcher && item.Id == rule?.Match?.Game?.Id);
            var reason = snapshot.SelectionReason.Kind == "unmatched-default"
                ? "No matching app"
                : group?.DisplayName ?? game?.DisplayName ?? rule?.Id ?? "Automatic selection";
            var focusName = snapshot.SelectionReason.Kind == "unmatched-default" ? $"Default ({reason})" : reason;
            return $"Focus: {focusName}\nProfile: {profile.DisplayName}\n{snapshot.RuntimeStatus?.AppliedDetail ?? "Output unavailable"}";
        }
    }
}
