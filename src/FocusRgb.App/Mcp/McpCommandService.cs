using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FocusRgb.Core.Commands;
using FocusRgb.Core.Configuration;
using FocusRgb.LocalMcp;

namespace FocusRgb.App.Mcp
{
    /// <summary>Routes local MCP reads and holds write requests until the owner approves them in the app.</summary>
    public sealed class McpCommandService
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly ApplicationStateService _state;
        private readonly McpReadQueryService _queries;
        private readonly TimeProvider _timeProvider;
        private readonly TimeSpan _approvalTimeout;
        private readonly TimeSpan _declineCooldown;
        private readonly object _approvalGate = new();
        private PendingApproval? _pending;
        private DateTimeOffset _cooldownUntil;

        public McpCommandService(
            ApplicationStateService state,
            McpReadQueryService queries,
            TimeProvider? timeProvider = null,
            TimeSpan? approvalTimeout = null,
            TimeSpan? declineCooldown = null)
        {
            _state = state;
            _queries = queries;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _approvalTimeout = approvalTimeout ?? TimeSpan.FromMinutes(1);
            _declineCooldown = declineCooldown ?? TimeSpan.FromSeconds(3);
        }

        public event EventHandler<McpApprovalRequest>? ApprovalRequested;
        public event EventHandler? ApprovalCleared;

        public McpApprovalRequest? Pending
        {
            get
            {
                lock (_approvalGate) return _pending?.Request;
            }
        }

        public async Task<JsonElement> DispatchAsync(
            LocalMcpRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (IsReadMethod(request.Method!))
                return await _queries.QueryAsync(request.Method!, cancellationToken).ConfigureAwait(false);

            PreparedCommand prepared;
            try
            {
                prepared = Prepare(request);
            }
            catch (McpCommandException exception)
            {
                return Error(exception.Code, exception.Message, _state.Current);
            }

            var pending = TryBeginApproval(request, prepared);
            if (pending is null)
                return Error("busy", "Another MCP approval is pending or a decline cool-down is active.", _state.Current);

            bool approved;
            try
            {
                approved = await pending.Decision.Task.WaitAsync(
                    _approvalTimeout, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                if (Resolve(pending.Request.Id, approved: false, startCooldown: true))
                    return Error("approval-expired", "The MCP approval expired without a decision.", _state.Current);
                approved = await pending.Decision.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (pending.Decision.Task.IsCompletedSuccessfully)
            {
                approved = pending.Decision.Task.Result;
            }
            catch (OperationCanceledException)
            {
                Resolve(pending.Request.Id, approved: false, startCooldown: true);
                throw;
            }

            if (!approved)
                return Error("approval-declined", "The owner declined the MCP request.", _state.Current);

            // After approval the app owns the command through completion, even if the client disconnects.
            var result = await prepared.ExecuteAsync(CancellationToken.None).ConfigureAwait(false);
            return Result(result);
        }

        public bool Approve(string approvalId) => Resolve(approvalId, approved: true, startCooldown: false);

        public bool Decline(string approvalId) => Resolve(approvalId, approved: false, startCooldown: true);

        public void DeclinePending()
        {
            string? id;
            lock (_approvalGate) id = _pending?.Request.Id;
            if (id is not null) Resolve(id, approved: false, startCooldown: true);
        }

        private PendingApproval? TryBeginApproval(LocalMcpRequest request, PreparedCommand prepared)
        {
            PendingApproval pending;
            lock (_approvalGate)
            {
                if (_pending is not null || _timeProvider.GetUtcNow() < _cooldownUntil) return null;
                var approval = new McpApprovalRequest
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ClientName = SafeText(string.IsNullOrWhiteSpace(request.ClientName)
                        ? "Local MCP client"
                        : request.ClientName),
                    Method = request.Method!,
                    Summary = prepared.Summary,
                    PayloadHash = PayloadHash(request),
                    ExpectedRevision = prepared.ExpectedRevision,
                    ExpiresAt = _timeProvider.GetUtcNow() + _approvalTimeout
                };
                pending = new PendingApproval(approval);
                _pending = pending;
            }
            ApprovalRequested?.Invoke(this, pending.Request);
            return pending;
        }

        private bool Resolve(string approvalId, bool approved, bool startCooldown)
        {
            PendingApproval? pending;
            lock (_approvalGate)
            {
                if (_pending?.Request.Id != approvalId) return false;
                pending = _pending;
                pending.Decision.TrySetResult(approved);
                _pending = null;
                if (startCooldown) _cooldownUntil = _timeProvider.GetUtcNow() + _declineCooldown;
            }
            ApprovalCleared?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private PreparedCommand Prepare(LocalMcpRequest request)
        {
            if (request.Method is not (
                "selectProfile" or "setMode" or "applySettings" or "respondToNotification"
                or "upsertProfile" or "upsertApplicationGroup" or "reorderMappings"))
                throw new McpCommandException("method-not-found", $"Unknown MCP method '{request.Method}'.");
            var parameters = RequireParameters(request);
            var snapshot = _state.Current;
            return request.Method switch
            {
                "selectProfile" => PrepareSelectProfile(parameters, snapshot),
                "setMode" => PrepareSetMode(parameters, snapshot),
                "applySettings" => PrepareApplySettings(parameters, snapshot),
                "respondToNotification" => PrepareNotification(parameters, snapshot),
                "upsertProfile" => PrepareUpsertProfile(parameters, snapshot),
                "upsertApplicationGroup" => PrepareUpsertApplicationGroup(parameters, snapshot),
                "reorderMappings" => PrepareReorderMappings(parameters, snapshot),
                _ => throw new McpCommandException("method-not-found", $"Unknown MCP method '{request.Method}'.")
            };
        }

        private PreparedCommand PrepareSelectProfile(JsonElement parameters, ApplicationStateSnapshot snapshot)
        {
            var profileId = RequiredString(parameters, "profile");
            var profile = snapshot.Settings.Profiles!.SingleOrDefault(item => item.Id == profileId)
                ?? throw new McpCommandException("invalid-profile", $"Profile '{profileId}' does not exist.");
            var current = ProfileName(snapshot.Settings, snapshot.SelectionState.Mode == "manual"
                ? snapshot.SelectionState.ManualProfile!
                : snapshot.SelectionState.LastSelectedProfile!);
            var summary = $"Switch from {current} to {profile.DisplayName} and enter manual mode.";
            return new PreparedCommand(
                snapshot.SettingsRevision,
                summary,
                cancellationToken => _state.SelectProfileAsync(
                    profileId,
                    _timeProvider.GetUtcNow(),
                    snapshot.SettingsRevision,
                    "mcp",
                    cancellationToken));
        }

        private PreparedCommand PrepareSetMode(JsonElement parameters, ApplicationStateSnapshot snapshot)
        {
            var mode = RequiredString(parameters, "mode");
            if (mode is not ("manual" or "automatic"))
                throw new McpCommandException("invalid-mode", "Mode must be 'manual' or 'automatic'.");
            var active = mode == "manual"
                ? snapshot.SelectionState.ManualProfile!
                : snapshot.SelectionState.LastSelectedProfile!;
            var summary = $"Change mode from {snapshot.SelectionState.Mode} to {mode}; "
                + $"{ProfileName(snapshot.Settings, active)} is expected to become active.";
            return new PreparedCommand(
                snapshot.SettingsRevision,
                summary,
                cancellationToken => _state.SetModeAsync(
                    mode,
                    _timeProvider.GetUtcNow(),
                    snapshot.SettingsRevision,
                    "mcp",
                    cancellationToken));
        }

        private PreparedCommand PrepareApplySettings(JsonElement parameters, ApplicationStateSnapshot snapshot)
        {
            var expectedRevision = RequiredRevision(parameters);
            if (expectedRevision != snapshot.SettingsRevision) throw Conflict(expectedRevision, snapshot.SettingsRevision);
            if (!parameters.TryGetProperty("settings", out var settingsElement))
                throw new McpCommandException("invalid-parameters", "A complete settings document is required.");
            var loaded = SettingsJson.Load(settingsElement.GetRawText());
            if (loaded.Settings is null)
                throw new McpCommandException("invalid-settings", FormatErrors(loaded.Errors));
            return PrepareApply(loaded.Settings, snapshot, expectedRevision);
        }

        private PreparedCommand PrepareUpsertProfile(JsonElement parameters, ApplicationStateSnapshot snapshot)
        {
            var expectedRevision = RequiredRevision(parameters);
            if (expectedRevision != snapshot.SettingsRevision) throw Conflict(expectedRevision, snapshot.SettingsRevision);
            var profile = DeserializeProperty<ProfileSettings>(parameters, "profile");
            var candidate = Clone(snapshot.Settings);
            var index = candidate.Profiles!.FindIndex(item => item.Id == profile.Id);
            if (index < 0) candidate.Profiles.Add(profile);
            else candidate.Profiles[index] = profile;
            return PrepareApply(candidate, snapshot, expectedRevision);
        }

        private PreparedCommand PrepareUpsertApplicationGroup(JsonElement parameters, ApplicationStateSnapshot snapshot)
        {
            var expectedRevision = RequiredRevision(parameters);
            if (expectedRevision != snapshot.SettingsRevision) throw Conflict(expectedRevision, snapshot.SettingsRevision);
            var group = DeserializeProperty<ApplicationGroupSettings>(parameters, "applicationGroup");
            var candidate = Clone(snapshot.Settings);
            var index = candidate.ApplicationGroups!.FindIndex(item => item.Id == group.Id);
            if (index < 0) candidate.ApplicationGroups.Add(group);
            else candidate.ApplicationGroups[index] = group;
            return PrepareApply(candidate, snapshot, expectedRevision);
        }

        private PreparedCommand PrepareReorderMappings(JsonElement parameters, ApplicationStateSnapshot snapshot)
        {
            var expectedRevision = RequiredRevision(parameters);
            if (expectedRevision != snapshot.SettingsRevision) throw Conflict(expectedRevision, snapshot.SettingsRevision);
            if (!parameters.TryGetProperty("mappingIds", out var idsElement)
                || idsElement.ValueKind != JsonValueKind.Array)
                throw new McpCommandException("invalid-parameters", "mappingIds must be an array.");
            if (idsElement.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
                throw new McpCommandException("invalid-parameters", "mappingIds must contain only strings.");
            var ids = idsElement.EnumerateArray().Select(item => item.GetString()).ToList();
            if (ids.Any(string.IsNullOrWhiteSpace)
                || ids.Count != snapshot.Settings.Rules!.Count
                || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
                throw new McpCommandException("invalid-parameters", "mappingIds must contain every mapping ID exactly once.");
            var byId = snapshot.Settings.Rules.ToDictionary(rule => rule.Id!, StringComparer.Ordinal);
            if (ids.Any(id => !byId.ContainsKey(id!)))
                throw new McpCommandException("invalid-parameters", "mappingIds contains an unknown mapping ID.");
            var candidate = Clone(snapshot.Settings);
            candidate.Rules!.Clear();
            candidate.Rules.AddRange(ids.Select(id => byId[id!]));
            return PrepareApply(candidate, snapshot, expectedRevision);
        }

        private PreparedCommand PrepareNotification(JsonElement parameters, ApplicationStateSnapshot snapshot)
        {
            var id = RequiredString(parameters, "id");
            var response = RequiredString(parameters, "response");
            if (response is not ("accept" or "decline"))
                throw new McpCommandException("invalid-response", "Response must be 'accept' or 'decline'.");
            var notification = snapshot.RuntimeStatus!.Notifications.SingleOrDefault(item => item.Id == id)
                ?? throw new McpCommandException("notification-not-found", $"Notification '{id}' does not exist.");
            var effect = response == "accept" ? "can resume plugin output" : "keeps output stopped";
            var summary = $"Respond '{response}' to recovery for plugin {notification.PluginId}; accepting {effect}.";
            return new PreparedCommand(
                snapshot.SettingsRevision,
                summary,
                cancellationToken => _state.RespondToNotificationAsync(
                    id,
                    response,
                    snapshot.SettingsRevision,
                    "mcp",
                    cancellationToken));
        }

        private PreparedCommand PrepareApply(
            SettingsDocument candidate,
            ApplicationStateSnapshot snapshot,
            long expectedRevision)
        {
            if (Json(snapshot.Settings.Plugins) != Json(candidate.Plugins))
                throw new McpCommandException(
                    "plugin-changes-not-allowed",
                    "Plugin enablement can be changed only in the FocusRGB app.");
            var errors = SettingsValidator.Validate(candidate);
            if (errors.Count > 0)
                throw new McpCommandException("invalid-settings", FormatErrors(errors));
            var summary = SummarizeSettings(snapshot.Settings, candidate);
            return new PreparedCommand(
                expectedRevision,
                summary,
                cancellationToken => _state.ApplySettingsAsync(
                    candidate, expectedRevision, "mcp", cancellationToken));
        }

        private static string SummarizeSettings(SettingsDocument current, SettingsDocument candidate)
        {
            var changes = new List<string>();
            var currentProfiles = current.Profiles!;
            var candidateProfiles = candidate.Profiles!;
            var currentRules = current.Rules!;
            var candidateRules = candidate.Rules!;
            AddNamedChanges(changes, "profiles", currentProfiles, candidateProfiles, item => item.Id!, item => item.DisplayName!);
            AddNamedChanges(changes, "application groups", current.ApplicationGroups!, candidate.ApplicationGroups!, item => item.Id!, item => item.DisplayName!);
            if (!currentRules.Select(rule => rule.Id).SequenceEqual(candidateRules.Select(rule => rule.Id)))
                changes.Add($"mapping order: {Names(currentRules.Select(rule => rule.Id))} -> {Names(candidateRules.Select(rule => rule.Id))}");
            var changedRules = candidateRules
                .Where(rule => currentRules.SingleOrDefault(item => item.Id == rule.Id) is { } previous
                    && Json(previous) != Json(rule))
                .Select(rule => rule.Id!);
            AddList(changes, "changed mappings", changedRules);
            foreach (var profile in candidateProfiles)
            {
                var previous = currentProfiles.SingleOrDefault(item => item.Id == profile.Id);
                if (previous is null) continue;
                var oldResources = Resources(previous);
                var newResources = Resources(profile);
                if (oldResources != newResources)
                    changes.Add($"{Quote(profile.DisplayName!)} effect/layout: {oldResources} -> {newResources}");
                if (previous is not null && Json(previous.Bindings) != Json(profile.Bindings))
                    changes.Add($"{Quote(profile.DisplayName!)} bindings changed");
                if (previous is not null && Json(previous.Fallback) != Json(profile.Fallback))
                    changes.Add($"{Quote(profile.DisplayName!)} fallback changed");
            }
            foreach (var group in candidate.ApplicationGroups!)
            {
                var previous = current.ApplicationGroups!.SingleOrDefault(item => item.Id == group.Id);
                if (previous is not null && Json(previous.Executables) != Json(group.Executables))
                    changes.Add($"{Quote(group.DisplayName!)} executables: {Names(group.Executables!)}");
            }
            if (Json(current.Application) != Json(candidate.Application))
                changes.Add("application preferences changed");
            if (Json(current.Selection) != Json(candidate.Selection))
                changes.Add("selection preferences changed");
            if (current.SchemaVersion != candidate.SchemaVersion
                || Json(current.AdditionalProperties) != Json(candidate.AdditionalProperties))
                changes.Add("settings schema or extension fields changed");
            return changes.Count == 0 ? "Apply a settings document with no detected changes." : string.Join(Environment.NewLine, changes);
        }

        private static void AddNamedChanges<T>(
            ICollection<string> changes,
            string label,
            IEnumerable<T> current,
            IEnumerable<T> candidate,
            Func<T, string> id,
            Func<T, string> name)
        {
            var oldItems = current.ToDictionary(id, StringComparer.Ordinal);
            var newItems = candidate.ToDictionary(id, StringComparer.Ordinal);
            var added = newItems.Keys.Except(oldItems.Keys, StringComparer.Ordinal).Select(key => Quote(name(newItems[key])));
            var removed = oldItems.Keys.Except(newItems.Keys, StringComparer.Ordinal).Select(key => Quote(name(oldItems[key])));
            var changed = newItems.Keys.Intersect(oldItems.Keys, StringComparer.Ordinal)
                .Where(key => Json(oldItems[key]) != Json(newItems[key])).Select(key => Quote(name(newItems[key])));
            AddList(changes, $"added {label}", added);
            AddList(changes, $"removed {label}", removed);
            AddList(changes, $"changed {label}", changed);
        }

        private static void AddList(ICollection<string> changes, string label, IEnumerable<string> names)
        {
            var values = names.ToList();
            if (values.Count > 0) changes.Add($"{label}: {string.Join(", ", values)}");
        }

        private static string Resources(ProfileSettings profile) => string.Join(", ", profile.Bindings!.Select(binding =>
            $"{Quote(binding.Id!)} effect={Quote(binding.Effect?.ResourceId ?? binding.Effect?.DisplayName ?? "none")}, "
            + $"layout={Quote(binding.Layout?.ResourceId ?? binding.Layout?.DisplayName ?? "current provider layout")}"));

        private static string ProfileName(SettingsDocument settings, string profileId) =>
            settings.Profiles!.SingleOrDefault(profile => profile.Id == profileId)?.DisplayName ?? profileId;

        private static string Names(IEnumerable<string?> values) =>
            string.Join(", ", values.Select(value => Quote(value ?? string.Empty)));

        private static string Quote(string value) => JsonSerializer.Serialize(SafeText(value));

        private static string SafeText(string value) => new(
            value.Take(128).Select(character => char.IsControl(character) ? ' ' : character).ToArray());

        private static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

        private static SettingsDocument Clone(SettingsDocument settings) =>
            SettingsJson.Load(SettingsJson.Serialize(settings)).Settings!;

        private static JsonElement RequireParameters(LocalMcpRequest request) =>
            request.Parameters is { ValueKind: JsonValueKind.Object } parameters
                ? parameters
                : throw new McpCommandException("invalid-parameters", "The command parameters must be an object.");

        private static string RequiredString(JsonElement parameters, string name) =>
            parameters.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()!
                : throw new McpCommandException("invalid-parameters", $"'{name}' is required.");

        private static long RequiredRevision(JsonElement parameters) =>
            parameters.TryGetProperty("expectedRevision", out var revision) && revision.TryGetInt64(out var value)
                ? value
                : throw new McpCommandException("invalid-parameters", "'expectedRevision' is required.");

        private static T DeserializeProperty<T>(JsonElement parameters, string name)
        {
            if (!parameters.TryGetProperty(name, out var value))
                throw new McpCommandException("invalid-parameters", $"'{name}' is required.");
            try
            {
                return value.Deserialize<T>(JsonOptions)
                    ?? throw new McpCommandException("invalid-parameters", $"'{name}' is invalid.");
            }
            catch (JsonException exception)
            {
                throw new McpCommandException("invalid-parameters", $"'{name}' is invalid: {exception.Message}");
            }
        }

        private static McpCommandException Conflict(long expected, long current) =>
            new("conflict", $"Settings revision {expected} is stale; current revision is {current}.");

        private static string FormatErrors(IEnumerable<ConfigurationError> errors) =>
            string.Join("; ", errors.Select(error => $"{error.Path}: {error.Message}"));

        private static string PayloadHash(LocalMcpRequest request)
        {
            var value = request.Method + "\n" + (request.Parameters?.GetRawText() ?? string.Empty);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        }

        private static bool IsReadMethod(string method) => method is
            "listProfiles" or "listApplicationGroups" or "listMappings" or "listPlugins"
            or "listDevices" or "getStatus" or "explainActiveSelection";

        private static JsonElement Result(ApplicationCommandResult result) =>
            result.IsSuccess
                ? JsonSerializer.SerializeToElement(new
                {
                    ok = true,
                    status = Status(result.Snapshot),
                    warning = result.Warning?.Message
                }, JsonOptions)
                : Error(result.ErrorCode!, result.ErrorMessage!, result.Snapshot);

        private static JsonElement Error(string code, string message, ApplicationStateSnapshot snapshot) =>
            JsonSerializer.SerializeToElement(new
            {
                ok = false,
                error = new { code, message },
                status = Status(snapshot)
            }, JsonOptions);

        private static object Status(ApplicationStateSnapshot snapshot) => new
        {
            snapshot.SettingsRevision,
            snapshot.SelectionState,
            snapshot.RuntimeStatus
        };

        private sealed record PreparedCommand(
            long ExpectedRevision,
            string Summary,
            Func<CancellationToken, Task<ApplicationCommandResult>> ExecuteAsync);

        private sealed class PendingApproval(McpApprovalRequest request)
        {
            public McpApprovalRequest Request { get; } = request;
            public TaskCompletionSource<bool> Decision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class McpCommandException(string code, string message) : Exception(message)
        {
            public string Code { get; } = code;
        }
    }

    /// <summary>Describes one exact local MCP mutation awaiting a decision from the app UI.</summary>
    public sealed class McpApprovalRequest
    {
        public required string Id { get; init; }
        public required string ClientName { get; init; }
        public required string Method { get; init; }
        public required string Summary { get; init; }
        public required string PayloadHash { get; init; }
        public long ExpectedRevision { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
    }
}
