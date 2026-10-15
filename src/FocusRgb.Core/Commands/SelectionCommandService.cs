using FocusRgb.Core.Configuration;
using FocusRgb.Core.Persistence;
using FocusRgb.Core.Selection;

namespace FocusRgb.Core.Commands
{
    /// <summary>Runs local selection commands and exposes status without claiming output was applied.</summary>
    public sealed class SelectionCommandService
    {
        private readonly ISelectionStateStore _store;

        public SelectionCommandService(ISelectionStateStore store)
        {
            _store = store;
        }

        /// <summary>Enters manual mode with a validated profile after its state is safely persisted.</summary>
        public Task<SelectionCommandResult> SelectProfileAsync(
            SettingsDocument settings,
            SelectionStateDocument current,
            string profileId,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            if (settings.Profiles?.Any(profile => profile.Id == profileId) != true)
                return Task.FromResult(SelectionCommandResult.Fail("invalid-profile", $"Unknown profile '{profileId}'."));

            var next = Copy(current, "manual", profileId, profileId, now);
            return SaveAsync(settings, next, SelectionReason.Manual(), cancellationToken);
        }

        /// <summary>Changes selection mode while retaining the last desired profile until observations exist.</summary>
        public Task<SelectionCommandResult> SetModeAsync(
            SettingsDocument settings,
            SelectionStateDocument current,
            string mode,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            if (mode is not ("manual" or "automatic"))
                return Task.FromResult(SelectionCommandResult.Fail("invalid-mode", $"Unknown mode '{mode}'."));

            var manualProfile = current.ManualProfile!;
            var desired = mode == "manual" ? manualProfile : current.LastSelectedProfile!;
            var next = Copy(current, mode, manualProfile, desired, now);
            var reason = mode == "manual" ? SelectionReason.Manual() : SelectionReason.ObservationPending();
            return SaveAsync(settings, next, reason, cancellationToken);
        }

        /// <summary>Flushes the current valid state before an explicit application shutdown.</summary>
        public Task<SelectionCommandResult> SaveCurrentAsync(
            SettingsDocument settings,
            SelectionStateDocument current,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            var next = Copy(
                current,
                current.Mode!,
                current.ManualProfile!,
                current.LastSelectedProfile!,
                now);
            var reason = current.Mode == "manual"
                ? SelectionReason.Manual()
                : SelectionReason.ObservationPending();
            return SaveAsync(settings, next, reason, cancellationToken);
        }

        /// <summary>Projects desired state and explicitly reports that no output integration is connected.</summary>
        public static SelectionRuntimeStatus GetStatus(
            SelectionStateDocument state,
            SelectionReason reason) => new()
            {
                Mode = state.Mode!,
                Desired = new SelectionDecision
                {
                    ProfileId = state.Mode == "manual" ? state.ManualProfile! : state.LastSelectedProfile!,
                    Reason = reason
                },
                AppliedProfileId = null,
                AppliedDetail = "No output integration is connected."
            };

        private async Task<SelectionCommandResult> SaveAsync(
            SettingsDocument settings,
            SelectionStateDocument next,
            SelectionReason reason,
            CancellationToken cancellationToken)
        {
            var save = await _store.SaveStateAsync(next, settings, cancellationToken).ConfigureAwait(false);
            return save.IsSuccess
                ? SelectionCommandResult.Ok(next, GetStatus(next, reason))
                : SelectionCommandResult.Fail(
                    "persistence-failed",
                    $"{save.Error!.FileName}: {save.Error.Message}");
        }

        private static SelectionStateDocument Copy(
            SelectionStateDocument source,
            string mode,
            string manualProfile,
            string lastSelectedProfile,
            DateTimeOffset updatedAt) => new()
            {
                SchemaVersion = source.SchemaVersion,
                Mode = mode,
                ManualProfile = manualProfile,
                LastSelectedProfile = lastSelectedProfile,
                UpdatedAt = updatedAt,
                AdditionalProperties = source.AdditionalProperties
            };
    }

    /// <summary>Returns a completed selection command or one actionable error.</summary>
    public sealed class SelectionCommandResult
    {
        public bool IsSuccess => State is not null;
        public SelectionStateDocument? State { get; private init; }
        public SelectionRuntimeStatus? Status { get; private init; }
        public string? ErrorCode { get; private init; }
        public string? ErrorMessage { get; private init; }

        public static SelectionCommandResult Ok(
            SelectionStateDocument state,
            SelectionRuntimeStatus status) => new() { State = state, Status = status };

        public static SelectionCommandResult Fail(string code, string message) => new()
        {
            ErrorCode = code,
            ErrorMessage = message
        };
    }

    /// <summary>Reports local desired state separately from unavailable applied output.</summary>
    public sealed class SelectionRuntimeStatus
    {
        public required string Mode { get; init; }
        public required SelectionDecision Desired { get; init; }
        public string? AppliedProfileId { get; init; }
        public required string AppliedDetail { get; init; }
    }
}
