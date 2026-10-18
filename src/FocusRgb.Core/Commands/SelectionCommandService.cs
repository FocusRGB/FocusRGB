using System.Diagnostics.CodeAnalysis;
using System.Collections.Concurrent;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Coordination;
using FocusRgb.Core.Persistence;
using FocusRgb.Core.Selection;

namespace FocusRgb.Core.Commands
{
    /// <summary>Runs local selection commands and exposes status without claiming output was applied.</summary>
    [SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
        Justification = "SemaphoreSlim owns no native handle because AvailableWaitHandle is never requested.")]
    public sealed class SelectionCommandService
    {
        private readonly ISelectionStateStore _store;
        private readonly OutputCoordinator? _outputCoordinator;
        private readonly SemaphoreSlim _commandGate = new(1, 1);
        private SelectionStateDocument? _latestState;
        private SelectionReason? _latestReason;
        private long _latestApplyRequest;
        private readonly ConcurrentDictionary<long, CancellationToken> _applyRequests = new();

        public SelectionCommandService(ISelectionStateStore store, OutputCoordinator? outputCoordinator = null)
        {
            _store = store;
            _outputCoordinator = outputCoordinator;
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

            var request = Interlocked.Increment(ref _latestApplyRequest);
            _applyRequests[request] = cancellationToken;
            return SaveAsync(
                settings, current, state => Copy(state, "manual", profileId, profileId, now),
                SelectionReason.Manual(), applyOutput: true,
                approveRecovery: true, request, cancellationToken);
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

            var request = Interlocked.Increment(ref _latestApplyRequest);
            _applyRequests[request] = cancellationToken;
            var reason = mode == "manual" ? SelectionReason.Manual() : SelectionReason.ObservationPending();
            return SaveAsync(settings, current, state =>
            {
                var manualProfile = state.ManualProfile!;
                var desired = mode == "manual" ? manualProfile : state.LastSelectedProfile!;
                return Copy(state, mode, manualProfile, desired, now);
            }, reason, applyOutput: true,
                approveRecovery: false, request, cancellationToken);
        }

        /// <summary>Evaluates a platform observation and persists a changed automatic selection.</summary>
        public async Task<SelectionCommandResult> ObserveAsync(
            SettingsDocument settings,
            SelectionStateDocument current,
            SelectionEngine engine,
            ApplicationObservation observation,
            DateTimeOffset observedAt,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(engine);
            ArgumentNullException.ThrowIfNull(observation);
            await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var state = _latestState ?? current;
                if (state.Mode != "automatic")
                    return SelectionCommandResult.Ok(state, GetStatus(state, SelectionReason.Manual()));

                var decision = engine.Select(
                    SelectionMode.Automatic, state.LastSelectedProfile!, observation, observedAt);
                if (decision.ProfileId == state.LastSelectedProfile)
                {
                    _latestReason = decision.Reason;
                    return SelectionCommandResult.Ok(
                        state, ProjectStatus(state, decision.Reason, _outputCoordinator?.CurrentStatus));
                }

                var next = Copy(
                    state, state.Mode!, state.ManualProfile!, decision.ProfileId, observedAt);
                var save = await _store.SaveStateAsync(next, settings, cancellationToken).ConfigureAwait(false);
                if (!save.IsSuccess)
                    return SelectionCommandResult.Fail(
                        "persistence-failed", $"{save.Error!.FileName}: {save.Error.Message}");

                _latestState = next;
                _latestReason = decision.Reason;
                var status = _outputCoordinator is null
                    ? GetStatus(next, decision.Reason)
                    : await _outputCoordinator.ApplyAsync(
                        settings, next, decision.Reason, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                return SelectionCommandResult.Ok(next, status);
            }
            finally
            {
                _commandGate.Release();
            }
        }

        /// <summary>Flushes the current valid state before an explicit application shutdown.</summary>
        public Task<SelectionCommandResult> SaveCurrentAsync(
            SettingsDocument settings,
            SelectionStateDocument current,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            var reason = current.Mode == "manual"
                ? SelectionReason.Manual()
                : SelectionReason.ObservationPending();
            return SaveAsync(settings, current, state => Copy(
                state, state.Mode!, state.ManualProfile!, state.LastSelectedProfile!, now),
                reason, applyOutput: false,
                approveRecovery: false, 0, cancellationToken);
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
                Effective = new EffectiveSelection
                {
                    ProfileId = state.Mode == "manual" ? state.ManualProfile! : state.LastSelectedProfile!,
                    Source = "desired"
                },
                Pending = [],
                Notifications = [],
                AppliedDetail = "No output integration is connected."
            };

        /// <summary>Responds to the current recovery prompt without changing persisted selection intent.</summary>
        public async Task<SelectionCommandResult> RespondToNotificationAsync(
            SettingsDocument settings,
            SelectionStateDocument current,
            string notificationId,
            string response,
            CancellationToken cancellationToken = default)
        {
            if (_outputCoordinator is null)
                return SelectionCommandResult.Fail("output-not-connected", "No output coordinator is connected.");
            if (_outputCoordinator.IsDisposed)
                return SelectionCommandResult.Fail("output-not-connected", "The output coordinator has stopped.");
            if (response is not ("accept" or "decline"))
                return SelectionCommandResult.Fail("invalid-response", "Response must be accept or decline.");

            var status = await _outputCoordinator.RespondToNotificationAsync(
                notificationId, response, cancellationToken).ConfigureAwait(false);
            return status is null
                ? SelectionCommandResult.Fail("notification-not-found", "The recovery notification no longer exists.")
                : SelectionCommandResult.Ok(current, status);
        }

        /// <summary>Reevaluates already-persisted settings against the current selection intent.</summary>
        public async Task<SelectionCommandResult> ReapplyAsync(
            SettingsDocument settings,
            SelectionStateDocument current,
            SelectionReason reason,
            CancellationToken cancellationToken = default)
        {
            var request = Interlocked.Increment(ref _latestApplyRequest);
            _applyRequests[request] = cancellationToken;
            try
            {
                await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                _applyRequests.TryRemove(request, out _);
                throw;
            }
            try
            {
                if (_outputCoordinator?.IsDisposed == true)
                    return SelectionCommandResult.Fail("output-not-connected", "The output coordinator has stopped.");
                var useLatest = _latestState is not null;
                var state = useLatest ? _latestState! : current;
                var effectiveReason = useLatest ? _latestReason! : reason;
                var status = _outputCoordinator is null
                    ? GetStatus(state, effectiveReason)
                    : !IsLatestActiveRequest(request)
                        ? GetStatus(state, effectiveReason)
                    : await _outputCoordinator.ApplyAsync(
                        settings, state, effectiveReason, cancellationToken: cancellationToken).ConfigureAwait(false);
                _latestState = state;
                _latestReason = effectiveReason;
                return SelectionCommandResult.Ok(state, status);
            }
            finally
            {
                _applyRequests.TryRemove(request, out _);
                _commandGate.Release();
            }
        }

        private async Task<SelectionCommandResult> SaveAsync(
            SettingsDocument settings,
            SelectionStateDocument current,
            Func<SelectionStateDocument, SelectionStateDocument> createNext,
            SelectionReason reason,
            bool applyOutput,
            bool approveRecovery,
            long request,
            CancellationToken cancellationToken)
        {
            try
            {
                await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                _applyRequests.TryRemove(request, out _);
                throw;
            }
            try
            {
                if (applyOutput && _outputCoordinator?.IsDisposed == true)
                    return SelectionCommandResult.Fail("output-not-connected", "The output coordinator has stopped.");
                var next = createNext(_latestState ?? current);
                SaveResult save;
                try
                {
                    save = await _store.SaveStateAsync(next, settings, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await RestoreLatestOutputAsync(settings).ConfigureAwait(false);
                    throw;
                }
                if (!save.IsSuccess)
                {
                    if (applyOutput && IsLatestActiveRequest(request)
                        && _latestState is not null && _latestReason is not null
                        && _outputCoordinator is { IsDisposed: false })
                    {
                        await RestoreLatestOutputAsync(settings).ConfigureAwait(false);
                    }
                    return SelectionCommandResult.Fail(
                        "persistence-failed",
                        $"{save.Error!.FileName}: {save.Error.Message}");
                }

                _latestState = next;
                _latestReason = reason;
                var status = applyOutput && _outputCoordinator is not null
                    && IsLatestActiveRequest(request)
                    ? await _outputCoordinator.ApplyAsync(
                        settings, next, reason, approveRecovery, cancellationToken).ConfigureAwait(false)
                    : _outputCoordinator?.CurrentStatus ?? GetStatus(next, reason);
                return SelectionCommandResult.Ok(next, status);
            }
            finally
            {
                _applyRequests.TryRemove(request, out _);
                _commandGate.Release();
            }
        }

        private bool IsLatestActiveRequest(long request) => !_applyRequests.Any(candidate =>
            candidate.Key > request && !candidate.Value.IsCancellationRequested);

        private static SelectionRuntimeStatus ProjectStatus(
            SelectionStateDocument state,
            SelectionReason reason,
            SelectionRuntimeStatus? current)
        {
            if (current is null) return GetStatus(state, reason);
            return new SelectionRuntimeStatus
            {
                Sequence = current.Sequence,
                Mode = state.Mode!,
                Desired = new SelectionDecision { ProfileId = state.LastSelectedProfile!, Reason = reason },
                Effective = current.Effective,
                Applied = current.Applied,
                Pending = current.Pending,
                Notifications = current.Notifications,
                AppliedDetail = current.AppliedDetail
            };
        }

        private async Task RestoreLatestOutputAsync(SettingsDocument settings)
        {
            if (_latestState is not null && _latestReason is not null
                && _outputCoordinator is { IsDisposed: false })
            {
                await _outputCoordinator.ApplyAsync(
                    settings, _latestState, _latestReason, cancellationToken: CancellationToken.None)
                    .ConfigureAwait(false);
            }
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
                UpdatedAt = updatedAt.ToUniversalTime(),
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
        public long Sequence { get; init; }
        public required string Mode { get; init; }
        public required SelectionDecision Desired { get; init; }
        public required EffectiveSelection Effective { get; init; }
        public AppliedSelection? Applied { get; init; }
        public IReadOnlyList<PendingBinding> Pending { get; init; } = [];
        public IReadOnlyList<RuntimeNotification> Notifications { get; init; } = [];
        public required string AppliedDetail { get; init; }
    }
}
