using System.Diagnostics.CodeAnalysis;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Coordination;
using FocusRgb.Core.Persistence;
using FocusRgb.Core.Selection;

namespace FocusRgb.Core.Commands
{
    /// <summary>Owns the current application documents and serializes every shared UI/MCP command.</summary>
    [SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
        Justification = "SemaphoreSlim owns no native handle because AvailableWaitHandle is never requested.")]
    public sealed class ApplicationStateService
    {
        private readonly IApplicationStateStore _store;
        private readonly OutputCoordinator? _outputCoordinator;
        private readonly SemaphoreSlim _commandGate = new(1, 1);
        private readonly object _snapshotGate = new();
        private SelectionCommandService _selectionCommands;
        private SettingsDocument _settings;
        private GameCatalogDocument _gameCatalog;
        private SelectionStateDocument _selectionState;
        private SelectionReason _selectionReason;
        private SelectionEngine _selectionEngine;
        private SelectionRuntimeStatus? _runtimeStatus;
        private long _settingsRevision = 1;
        private bool _commandActive;
        private bool _outputChangedDuringCommand;
        private ProfilePreview? _preview;
        private CancellationTokenSource? _previewCancellation;

        public ApplicationStateService(
            SettingsDocument settings,
            SelectionStateDocument selectionState,
            SelectionReason selectionReason,
            IApplicationStateStore store,
            OutputCoordinator? outputCoordinator = null,
            SelectionRuntimeStatus? runtimeStatus = null,
            GameCatalogDocument? gameCatalog = null)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(selectionState);
            ArgumentNullException.ThrowIfNull(selectionReason);
            ArgumentNullException.ThrowIfNull(store);
            var errors = SettingsValidator.Validate(settings)
                .Concat(StateJson.Validate(selectionState))
                .Concat(StateJson.ValidateProfileReferences(selectionState, settings))
                .ToList();
            gameCatalog ??= GameCatalogJson.CreateDefault();
            errors.AddRange(GameCatalogJson.Validate(gameCatalog));
            if (errors.Count > 0)
                throw new ArgumentException(FormatErrors(errors), nameof(settings));

            _settings = Clone(settings);
            _gameCatalog = Clone(gameCatalog);
            _selectionState = Clone(selectionState);
            _selectionReason = selectionReason;
            _selectionEngine = new SelectionEngine(_settings, _gameCatalog);
            _store = store;
            _outputCoordinator = outputCoordinator;
            _runtimeStatus = runtimeStatus
                ?? outputCoordinator?.CurrentStatus
                ?? SelectionCommandService.GetStatus(_selectionState, _selectionReason);
            _selectionCommands = new SelectionCommandService(store, outputCoordinator);
            if (outputCoordinator is not null) outputCoordinator.StatusChanged += OutputCoordinator_OnStatusChanged;
        }

        public event EventHandler<ApplicationStateSnapshot>? SnapshotChanged;
        public event EventHandler<SelectionObservationTrace>? ObservationEvaluated;

        public ApplicationStateSnapshot Current
        {
            get
            {
                lock (_snapshotGate) return CreateSnapshot("query");
            }
        }

        public Task<ApplicationCommandResult> SelectProfileAsync(
            string profileId,
            DateTimeOffset now,
            string source = "ui",
            CancellationToken cancellationToken = default) =>
            RunSelectionCommandAsync(
                (settings, state) => _selectionCommands.SelectProfileAsync(
                    settings, state, profileId, now, cancellationToken),
                source,
                cancellationToken);

        public Task<ApplicationCommandResult> SelectProfileAsync(
            string profileId,
            DateTimeOffset now,
            long expectedRevision,
            string source,
            CancellationToken cancellationToken = default) =>
            RunSelectionCommandAsync(
                (settings, state) => _selectionCommands.SelectProfileAsync(
                    settings, state, profileId, now, cancellationToken),
                source,
                cancellationToken,
                expectedRevision: expectedRevision);

        public Task<ApplicationCommandResult> SetModeAsync(
            string mode,
            DateTimeOffset now,
            string source = "ui",
            CancellationToken cancellationToken = default) =>
            RunSelectionCommandAsync(
                (settings, state) => _selectionCommands.SetModeAsync(
                    settings, state, mode, now, cancellationToken),
                source,
                cancellationToken);

        public Task<ApplicationCommandResult> SetModeAsync(
            string mode,
            DateTimeOffset now,
            long expectedRevision,
            string source,
            CancellationToken cancellationToken = default) =>
            RunSelectionCommandAsync(
                (settings, state) => _selectionCommands.SetModeAsync(
                    settings, state, mode, now, cancellationToken),
                source,
                cancellationToken,
                expectedRevision: expectedRevision);

        public Task<ApplicationCommandResult> SaveCurrentAsync(
            DateTimeOffset now,
            string source = "ui",
            CancellationToken cancellationToken = default) =>
            RunSelectionCommandAsync(
                (settings, state) => _selectionCommands.SaveCurrentAsync(
                    settings, state, now, cancellationToken),
                source,
                cancellationToken);

        public Task<ApplicationCommandResult> RespondToNotificationAsync(
            string notificationId,
            string response,
            string source = "ui",
            CancellationToken cancellationToken = default) =>
            RunSelectionCommandAsync(
                (settings, state) => _selectionCommands.RespondToNotificationAsync(
                    settings, state, notificationId, response, cancellationToken),
                source,
                cancellationToken);

        public Task<ApplicationCommandResult> RespondToNotificationAsync(
            string notificationId,
            string response,
            long expectedRevision,
            string source,
            CancellationToken cancellationToken = default) =>
            RunSelectionCommandAsync(
                (settings, state) => _selectionCommands.RespondToNotificationAsync(
                    settings, state, notificationId, response, cancellationToken),
                source,
                cancellationToken,
                expectedRevision: expectedRevision);

        public Task<ApplicationCommandResult> ReapplyAsync(
            string source = "ui",
            CancellationToken cancellationToken = default) =>
            RunSelectionCommandAsync(
                (settings, state) => _selectionCommands.ReapplyAsync(
                    settings, state, _selectionReason, cancellationToken),
                source,
                cancellationToken);

        public Task<ApplicationCommandResult> ObserveAsync(
            ApplicationObservation observation,
            DateTimeOffset observedAt,
            CancellationToken cancellationToken = default) =>
            RunSelectionCommandAsync(
                async (settings, state) =>
                {
                    var result = await _selectionCommands.ObserveAsync(
                        settings, state, _selectionEngine, observation, observedAt, cancellationToken).ConfigureAwait(false);
                    ObservationEvaluated?.Invoke(this, new SelectionObservationTrace(
                        observedAt, observation, _selectionEngine.GetDebounceState(), state.Mode!,
                        result.Status?.Desired.ProfileId, result.Status?.Desired.Reason.RuleId,
                        result.Status?.Desired.Reason.Kind ?? "failed", result.ErrorCode));
                    return result;
                },
                "observation",
                cancellationToken,
                publishUnchanged: false);

        /// <summary>Runs a bounded, already-approved temporary override without persisting preview intent.</summary>
        public async Task<ApplicationCommandResult> PreviewProfileAsync(
            string profileId, int durationSeconds, long expectedRevision, string source,
            CancellationToken cancellationToken = default)
        {
            if (durationSeconds is < 1 or > 30)
                return ApplicationCommandResult.Fail(Current, "invalid-duration", "Preview duration must be 1–30 seconds.");
            await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            ProfilePreview? preview = null;
            CancellationTokenSource? lifetime = null;
            var publish = false;
            BeginCommand();
            try
            {
                if (expectedRevision != _settingsRevision)
                    return ApplicationCommandResult.Fail(Current, "conflict", "Settings changed before preview approval completed.");
                if (_preview is not null)
                    return ApplicationCommandResult.Fail(Current, "busy", "An output preview is already active.");
                if (_outputCoordinator is null || _outputCoordinator.IsDisposed)
                    return ApplicationCommandResult.Fail(Current, "output-not-connected", "No output coordinator is connected.");
                if (!_settings.Profiles!.Any(profile => profile.Id == profileId))
                    return ApplicationCommandResult.Fail(Current, "invalid-profile", "The saved preview profile does not exist.");
                var journal = await _store.SavePreviewJournalAsync(profileId, cancellationToken).ConfigureAwait(false);
                if (!journal.IsSuccess)
                    return ApplicationCommandResult.Fail(Current, "persistence-failed", journal.Error!.Message);
                lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                lifetime.CancelAfter(TimeSpan.FromSeconds(durationSeconds));
                preview = new ProfilePreview(Guid.NewGuid().ToString("N"), profileId,
                    DateTimeOffset.UtcNow.AddSeconds(durationSeconds), source);
                lock (_snapshotGate) { _preview = preview; _previewCancellation = lifetime; }
                // Selection keeps tracking/persisting observations, but only the preview owns output.
                _selectionCommands = new SelectionCommandService(_store);
                var temporary = new SelectionStateDocument
                {
                    SchemaVersion = _selectionState.SchemaVersion, Mode = "manual",
                    ManualProfile = profileId, LastSelectedProfile = profileId, UpdatedAt = _selectionState.UpdatedAt
                };
                await _outputCoordinator.ApplyAsync(_settings, temporary,
                    new SelectionReason { Kind = "preview" }, cancellationToken: lifetime.Token).ConfigureAwait(false);
                publish = true;
            }
            catch
            {
                try { if (preview is not null) await EndPreviewCoreAsync().ConfigureAwait(false); }
                finally { lifetime?.Dispose(); }
                throw;
            }
            finally
            {
                var snapshot = EndCommand("preview", ref publish);
                _commandGate.Release();
                if (publish) SnapshotChanged?.Invoke(this, snapshot);
            }

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, lifetime!.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (lifetime!.IsCancellationRequested) { }
            finally
            {
                await _commandGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                BeginCommand();
                try
                {
                    if (_preview?.Id == preview.Id) await EndPreviewCoreAsync().ConfigureAwait(false);
                }
                finally
                {
                    publish = true;
                    var snapshot = EndCommand("preview-ended", ref publish);
                    _commandGate.Release();
                    SnapshotChanged?.Invoke(this, snapshot);
                    lifetime!.Dispose();
                }
            }
            return ApplicationCommandResult.Ok(Current);
        }

        public async Task CancelPreviewAsync(CancellationToken cancellationToken = default)
        {
            await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            BeginCommand();
            var publish = false;
            try
            {
                if (_preview is not null) { await EndPreviewCoreAsync().ConfigureAwait(false); publish = true; }
            }
            finally
            {
                var snapshot = EndCommand("preview-ended", ref publish);
                _commandGate.Release();
                if (publish) SnapshotChanged?.Invoke(this, snapshot);
            }
        }

        private async Task EndPreviewCoreAsync()
        {
            _previewCancellation?.Cancel();
            lock (_snapshotGate) { _preview = null; _previewCancellation = null; }
            _selectionCommands = new SelectionCommandService(_store, _outputCoordinator);
            // Never use the disconnected caller's token when restoring saved intent.
            var restored = await _selectionCommands.ReapplyAsync(_settings, _selectionState,
                _selectionReason, CancellationToken.None).ConfigureAwait(false);
            if (!restored.IsSuccess) throw new IOException($"Saved selection could not be restored: {restored.ErrorMessage}");
            ApplySelectionResult(restored);
            var journal = await _store.SavePreviewJournalAsync(null, CancellationToken.None).ConfigureAwait(false);
            if (!journal.IsSuccess) throw new IOException($"Could not clear preview journal: {journal.Error!.Message}");
        }

        /// <summary>Validates and saves a full document only when the caller read the current revision.</summary>
        public async Task<ApplicationCommandResult> ApplySettingsAsync(
            SettingsDocument candidate,
            long expectedRevision,
            string source,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            ArgumentException.ThrowIfNullOrWhiteSpace(source);
            await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            ApplicationStateSnapshot snapshot;
            string? errorCode = null;
            string? errorMessage = null;
            PersistenceError? warning = null;
            var publish = false;
            BeginCommand();
            try
            {
                SettingsDocument currentSettings;
                if (_preview is not null)
                    return ApplicationCommandResult.Fail(Current, "busy", "End the output preview before editing settings.");
                SelectionStateDocument currentState;
                SelectionReason currentReason;
                long currentRevision;
                lock (_snapshotGate)
                {
                    currentSettings = _settings;
                    currentState = _selectionState;
                    currentReason = _selectionReason;
                    currentRevision = _settingsRevision;
                }

                if (expectedRevision != currentRevision)
                {
                    errorCode = "conflict";
                    errorMessage = $"Settings revision {expectedRevision} is stale; current revision is {currentRevision}.";
                }
                else
                {
                    var validation = SettingsValidator.Validate(candidate);
                    if (validation.Count > 0)
                    {
                        errorCode = "invalid-settings";
                        errorMessage = FormatErrors(validation);
                    }
                    else
                    {
                        var savedCandidate = Clone(candidate);
                        var nextState = RepairProfileReferences(
                            currentState, savedCandidate, out var repaired, out var desiredRepaired);
                        var stateWasSaved = false;
                        if (repaired)
                        {
                            var stateSave = await _store.SaveStateAsync(
                                nextState, savedCandidate, cancellationToken).ConfigureAwait(false);
                            if (!stateSave.IsSuccess)
                            {
                                errorCode = "persistence-failed";
                                errorMessage = $"{stateSave.Error!.FileName}: {stateSave.Error.Message}";
                            }
                            else
                            {
                                stateWasSaved = true;
                            }
                        }

                        if (errorCode is null)
                        {
                            SaveResult save;
                            try
                            {
                                save = await _store.SaveSettingsAsync(savedCandidate, cancellationToken)
                                    .ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) when (stateWasSaved)
                            {
                                await _store.SaveStateAsync(
                                    currentState, currentSettings, CancellationToken.None).ConfigureAwait(false);
                                throw;
                            }
                            if (!save.IsSuccess)
                            {
                                errorCode = "persistence-failed";
                                errorMessage = $"{save.Error!.FileName}: {save.Error.Message}";
                                if (stateWasSaved)
                                {
                                    var rollback = await _store.SaveStateAsync(
                                        currentState, currentSettings, CancellationToken.None).ConfigureAwait(false);
                                    if (!rollback.IsSuccess)
                                        errorMessage += $" State rollback failed: {rollback.Error!.Message}";
                                }
                            }
                            else
                            {
                                warning = save.Warning;
                                if (stateWasSaved)
                                {
                                    currentState = nextState;
                                    if (desiredRepaired)
                                    {
                                        currentReason = currentState.Mode == "manual"
                                            ? SelectionReason.Manual()
                                            : SelectionReason.ObservationPending();
                                    }
                                    _selectionCommands = new SelectionCommandService(_store, _outputCoordinator);
                                }
                                lock (_snapshotGate)
                                {
                                    _settings = savedCandidate;
                                    _selectionState = currentState;
                                    _selectionReason = currentReason;
                                    _selectionEngine = new SelectionEngine(savedCandidate, _gameCatalog);
                                    _settingsRevision++;
                                }
                                var reapplied = await _selectionCommands.ReapplyAsync(
                                    savedCandidate, currentState, currentReason, cancellationToken)
                                    .ConfigureAwait(false);
                                if (reapplied.IsSuccess) ApplySelectionResult(reapplied);
                                else warning ??= new PersistenceError
                                {
                                    FileName = "output",
                                    Message = reapplied.ErrorMessage!
                                };
                                publish = true;
                            }
                        }
                    }
                }
            }
            finally
            {
                snapshot = EndCommand(source, ref publish);
                _commandGate.Release();
            }

            if (publish) SnapshotChanged?.Invoke(this, snapshot);
            return errorCode is null
                ? ApplicationCommandResult.Ok(snapshot, warning)
                : ApplicationCommandResult.Fail(snapshot, errorCode, errorMessage!);
        }

        /// <summary>Validates and saves the app-owned game catalog under the shared revision gate.</summary>
        public async Task<ApplicationCommandResult> ApplyGameCatalogAsync(
            GameCatalogDocument candidate,
            long expectedRevision,
            string source,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            ArgumentException.ThrowIfNullOrWhiteSpace(source);
            await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            ApplicationStateSnapshot snapshot;
            string? errorCode = null;
            string? errorMessage = null;
            var publish = false;
            BeginCommand();
            try
            {
                lock (_snapshotGate)
                {
                    if (expectedRevision != _settingsRevision)
                    {
                        errorCode = "conflict";
                        errorMessage = $"Settings revision {expectedRevision} is stale; current revision is {_settingsRevision}.";
                    }
                }
                if (errorCode is null)
                {
                    var validation = GameCatalogJson.Validate(candidate);
                    if (validation.Count > 0)
                    {
                        errorCode = "invalid-game-catalog";
                        errorMessage = FormatErrors(validation);
                    }
                    else
                    {
                        var savedCandidate = Clone(candidate);
                        var save = await _store.SaveGameCatalogAsync(savedCandidate, cancellationToken)
                            .ConfigureAwait(false);
                        if (!save.IsSuccess)
                        {
                            errorCode = "persistence-failed";
                            errorMessage = $"{save.Error!.FileName}: {save.Error.Message}";
                        }
                        else
                        {
                            lock (_snapshotGate)
                            {
                                _gameCatalog = savedCandidate;
                                _selectionEngine.UpdateGameCatalog(savedCandidate);
                                _settingsRevision++;
                            }
                            publish = true;
                        }
                    }
                }
            }
            finally
            {
                snapshot = EndCommand(source, ref publish);
                _commandGate.Release();
            }

            if (publish) SnapshotChanged?.Invoke(this, snapshot);
            return errorCode is null
                ? ApplicationCommandResult.Ok(snapshot)
                : ApplicationCommandResult.Fail(snapshot, errorCode, errorMessage!);
        }

        private async Task<ApplicationCommandResult> RunSelectionCommandAsync(
            Func<SettingsDocument, SelectionStateDocument, Task<SelectionCommandResult>> command,
            string source,
            CancellationToken cancellationToken,
            bool publishUnchanged = true,
            long? expectedRevision = null)
        {
            await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            ApplicationStateSnapshot snapshot;
            SelectionCommandResult? result = null;
            string? conflictMessage = null;
            var publish = false;
            BeginCommand();
            try
            {
                SettingsDocument settings;
                SelectionStateDocument state;
                SelectionReason reason;
                lock (_snapshotGate)
                {
                    settings = _settings;
                    state = _selectionState;
                    reason = _selectionReason;
                    if (expectedRevision is not null && expectedRevision != _settingsRevision)
                        conflictMessage = $"Settings revision {expectedRevision} is stale; current revision is {_settingsRevision}.";
                }
                if (conflictMessage is null)
                {
                    if (_preview is not null && source != "observation")
                        await EndPreviewCoreAsync().ConfigureAwait(false);
                    result = await command(settings, state).ConfigureAwait(false);
                    if (result.IsSuccess)
                    {
                        ApplySelectionResult(result);
                        publish = publishUnchanged
                            || state.UpdatedAt != result.State!.UpdatedAt
                            || reason.Kind != result.Status!.Desired.Reason.Kind
                            || reason.RuleId != result.Status.Desired.Reason.RuleId;
                    }
                }
            }
            finally
            {
                snapshot = EndCommand(source, ref publish);
                _commandGate.Release();
            }

            if (publish) SnapshotChanged?.Invoke(this, snapshot);
            if (conflictMessage is not null)
                return ApplicationCommandResult.Fail(snapshot, "conflict", conflictMessage);
            return result!.IsSuccess
                ? ApplicationCommandResult.Ok(snapshot)
                : ApplicationCommandResult.Fail(snapshot, result.ErrorCode!, result.ErrorMessage!);
        }

        private void ApplySelectionResult(SelectionCommandResult result)
        {
            var status = result.Status!;
            lock (_snapshotGate)
            {
                _selectionState = Clone(result.State!);
                if (_preview is not null || _runtimeStatus is null || status.Sequence >= _runtimeStatus.Sequence)
                {
                    _runtimeStatus = _preview is null ? status
                        : ProjectRuntimeStatus(_selectionState, status.Desired.Reason, _outputCoordinator?.CurrentStatus);
                    _selectionReason = status.Desired.Reason;
                }
            }
        }

        private void OutputCoordinator_OnStatusChanged(object? sender, SelectionRuntimeStatus status)
        {
            ApplicationStateSnapshot? snapshot = null;
            lock (_snapshotGate)
            {
                if (_runtimeStatus is not null && status.Sequence < _runtimeStatus.Sequence) return;
                _runtimeStatus = _preview is null ? status : ProjectRuntimeStatus(_selectionState, _selectionReason, status);
                if (_preview is null) _selectionReason = status.Desired.Reason;
                if (_commandActive) _outputChangedDuringCommand = true;
                else snapshot = CreateSnapshot("output");
            }
            if (snapshot is not null) SnapshotChanged?.Invoke(this, snapshot);
        }

        private void BeginCommand()
        {
            lock (_snapshotGate)
            {
                _commandActive = true;
                _outputChangedDuringCommand = false;
            }
        }

        private ApplicationStateSnapshot EndCommand(string source, ref bool publish)
        {
            lock (_snapshotGate)
            {
                _commandActive = false;
                var outputOnly = !publish && _outputChangedDuringCommand;
                publish |= _outputChangedDuringCommand;
                _outputChangedDuringCommand = false;
                return CreateSnapshot(outputOnly ? "output" : source);
            }
        }

        private ApplicationStateSnapshot CreateSnapshot(string source) => new()
        {
            Settings = Clone(_settings),
            GameCatalog = Clone(_gameCatalog),
            SelectionState = Clone(_selectionState),
            SelectionReason = _selectionReason,
            RuntimeStatus = _runtimeStatus,
            Preview = _preview,
            SettingsRevision = _settingsRevision,
            Source = source
        };

        private static SelectionStateDocument RepairProfileReferences(
            SelectionStateDocument state,
            SettingsDocument settings,
            out bool repaired,
            out bool desiredRepaired)
        {
            var profileIds = settings.Profiles!.Select(profile => profile.Id!).ToHashSet(StringComparer.Ordinal);
            var manualRepaired = !profileIds.Contains(state.ManualProfile!);
            var automaticRepaired = !profileIds.Contains(state.LastSelectedProfile!);
            repaired = manualRepaired || automaticRepaired;
            desiredRepaired = state.Mode == "manual" ? manualRepaired : automaticRepaired;
            if (!repaired) return state;
            var replacement = settings.Selection!.DefaultProfile!;
            return new SelectionStateDocument
            {
                SchemaVersion = state.SchemaVersion,
                Mode = state.Mode,
                ManualProfile = profileIds.Contains(state.ManualProfile!) ? state.ManualProfile : replacement,
                LastSelectedProfile = profileIds.Contains(state.LastSelectedProfile!) ? state.LastSelectedProfile : replacement,
                UpdatedAt = DateTimeOffset.UtcNow,
                AdditionalProperties = state.AdditionalProperties
            };
        }

        private static SelectionRuntimeStatus ProjectRuntimeStatus(
            SelectionStateDocument state,
            SelectionReason reason,
            SelectionRuntimeStatus? current)
        {
            if (current is null) return SelectionCommandService.GetStatus(state, reason);
            return new SelectionRuntimeStatus
            {
                Sequence = current.Sequence,
                Mode = state.Mode!,
                Desired = new SelectionDecision
                {
                    ProfileId = state.Mode == "manual" ? state.ManualProfile! : state.LastSelectedProfile!,
                    Reason = reason
                },
                Effective = current.Effective,
                Applied = current.Applied,
                Pending = current.Pending,
                Notifications = current.Notifications,
                AppliedDetail = current.AppliedDetail
            };
        }

        private static SettingsDocument Clone(SettingsDocument settings)
        {
            var result = SettingsJson.Load(SettingsJson.Serialize(settings));
            return result.Settings ?? throw new InvalidOperationException("Validated settings could not be copied.");
        }

        private static SelectionStateDocument Clone(SelectionStateDocument state)
        {
            var result = StateJson.Load(StateJson.Serialize(state));
            return result.State ?? throw new InvalidOperationException("Validated selection state could not be copied.");
        }

        private static GameCatalogDocument Clone(GameCatalogDocument catalog)
        {
            var result = GameCatalogJson.Load(GameCatalogJson.Serialize(catalog));
            return result.Catalog ?? throw new InvalidOperationException("Validated game catalog could not be copied.");
        }

        private static string FormatErrors(IEnumerable<ConfigurationError> errors) =>
            string.Join("; ", errors.Select(error => $"{error.Path}: {error.Message}"));
    }

    /// <summary>An immutable copy of the application documents and latest projected runtime status.</summary>
    public sealed class ApplicationStateSnapshot
    {
        public required SettingsDocument Settings { get; init; }
        public required GameCatalogDocument GameCatalog { get; init; }
        public required SelectionStateDocument SelectionState { get; init; }
        public required SelectionReason SelectionReason { get; init; }
        public SelectionRuntimeStatus? RuntimeStatus { get; init; }
        public ProfilePreview? Preview { get; init; }
        public long SettingsRevision { get; init; }
        public required string Source { get; init; }
    }

    /// <summary>Returns a shared command snapshot or one stable validation, conflict, or persistence error.</summary>
    public sealed class ApplicationCommandResult
    {
        public bool IsSuccess => ErrorCode is null;
        public required ApplicationStateSnapshot Snapshot { get; init; }
        public string? ErrorCode { get; private init; }
        public string? ErrorMessage { get; private init; }
        public PersistenceError? Warning { get; private init; }

        public static ApplicationCommandResult Ok(
            ApplicationStateSnapshot snapshot,
            PersistenceError? warning = null) => new() { Snapshot = snapshot, Warning = warning };

        public static ApplicationCommandResult Fail(
            ApplicationStateSnapshot snapshot,
            string code,
            string message) => new() { Snapshot = snapshot, ErrorCode = code, ErrorMessage = message };
    }
}
