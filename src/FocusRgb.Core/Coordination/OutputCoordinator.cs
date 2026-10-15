using FocusRgb.Contracts;
using FocusRgb.Core.Commands;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Selection;

namespace FocusRgb.Core.Coordination
{
    /// <summary>Provides provider-neutral plugin calls without coupling Core to process hosting.</summary>
    public interface IPluginOutputGateway
    {
        IReadOnlyList<PluginCapability>? GetCapabilities(string pluginId);

        Task<PluginGatewayResult<PluginCatalogResult>> ListCatalogAsync(
            string pluginId,
            string kind,
            CancellationToken cancellationToken = default);

        Task<PluginGatewayResult<BindingApplyResult>> ApplyBindingAsync(
            string pluginId,
            BindingApplyParameters parameters,
            CancellationToken cancellationToken = default);

        Task<PluginGatewayResult<BindingReleaseResult>> ReleaseBindingAsync(
            string pluginId,
            BindingReleaseParameters parameters,
            CancellationToken cancellationToken = default);
    }

    /// <summary>Returns a typed plugin result or one stable host/plugin failure.</summary>
    public sealed class PluginGatewayResult<T>
    {
        public PluginGatewayResult(T value) => Value = value;

        public PluginGatewayResult(string code, string message)
        {
            ErrorCode = code;
            ErrorMessage = message;
        }

        public bool IsSuccess => ErrorCode is null;
        public T? Value { get; }
        public string? ErrorCode { get; }
        public string? ErrorMessage { get; }

    }

    /// <summary>Coordinates desired profile output, fallback, ownership, and recovery consent.</summary>
    public sealed class OutputCoordinator : IDisposable
    {
        private readonly IPluginOutputGateway _gateway;
        private readonly NativeSetupDocument _nativeSetup;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Dictionary<string, (string ReasonCode, string Message)> _unavailablePlugins =
            new(StringComparer.Ordinal);
        private readonly HashSet<string> _recoveryBlockedPlugins = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RuntimeNotification> _notifications = new(StringComparer.Ordinal);
        private AppliedBinding? _ownedBinding;
        private SettingsDocument? _settings;
        private SelectionStateDocument? _state;
        private SelectionReason? _reason;
        private long _statusSequence;

        public OutputCoordinator(IPluginOutputGateway gateway, NativeSetupDocument nativeSetup)
        {
            ArgumentNullException.ThrowIfNull(gateway);
            ArgumentNullException.ThrowIfNull(nativeSetup);
            _gateway = gateway;
            _nativeSetup = nativeSetup;
        }

        public event EventHandler<SelectionRuntimeStatus>? StatusChanged;

        public SelectionRuntimeStatus? CurrentStatus { get; private set; }

        /// <summary>Applies the current desired profile or its first usable fallback.</summary>
        public Task<SelectionRuntimeStatus> ApplyAsync(
            SettingsDocument settings,
            SelectionStateDocument state,
            SelectionReason reason,
            CancellationToken cancellationToken = default) =>
            ApplyAsync(settings, state, reason, false, cancellationToken);

        /// <summary>Applies current intent and optionally treats it as recovery consent.</summary>
        public async Task<SelectionRuntimeStatus> ApplyAsync(
            SettingsDocument settings,
            SelectionStateDocument state,
            SelectionReason reason,
            bool approveRecovery,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(state);
            ArgumentNullException.ThrowIfNull(reason);
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            SelectionRuntimeStatus status;
            try
            {
                _settings = settings;
                _state = state;
                _reason = reason;
                status = await ApplyCoreAsync(
                    settings, state, reason, approveRecovery, cancellationToken).ConfigureAwait(false);
                CurrentStatus = status;
            }
            finally
            {
                _gate.Release();
            }
            StatusChanged?.Invoke(this, status);
            return status;
        }

        /// <summary>Records plugin loss or exposes one consent-gated recovery notification.</summary>
        public Task<SelectionRuntimeStatus?> ReportAvailabilityAsync(
            string pluginId,
            bool available,
            CancellationToken cancellationToken = default) =>
            ReportAvailabilityAsync(pluginId, available, null, null, cancellationToken);

        public async Task<SelectionRuntimeStatus?> ReportAvailabilityAsync(
            string pluginId,
            bool available,
            string? reasonCode,
            string? message,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            SelectionRuntimeStatus? status;
            try
            {
                if (!available)
                {
                    _unavailablePlugins[pluginId] = (
                        reasonCode ?? "provider-not-running",
                        message ?? "The plugin is unavailable.");
                    _recoveryBlockedPlugins.Remove(pluginId);
                    _notifications.Remove(NotificationId(pluginId));
                    if (_ownedBinding?.PluginId == pluginId && _settings is not null && _state is not null && _reason is not null)
                    {
                        CurrentStatus = await ApplyCoreAsync(
                            _settings, _state, _reason, false, cancellationToken).ConfigureAwait(false);
                    }
                }
                else if (_unavailablePlugins.Remove(pluginId))
                {
                    _recoveryBlockedPlugins.Add(pluginId);
                    _notifications.TryAdd(NotificationId(pluginId), new RuntimeNotification
                    {
                        Id = NotificationId(pluginId),
                        Kind = "plugin-recovered",
                        PluginId = pluginId,
                        Actions = ["accept", "decline"]
                    });
                    CurrentStatus = CopyWithNotifications(CurrentStatus);
                }

                status = CurrentStatus;
            }
            finally
            {
                _gate.Release();
            }
            if (status is not null) StatusChanged?.Invoke(this, status);
            return status;
        }

        /// <summary>Accepts recovery using current intent, or declines it without resuming output.</summary>
        public async Task<SelectionRuntimeStatus?> RespondToNotificationAsync(
            string notificationId,
            string response,
            CancellationToken cancellationToken = default)
        {
            if (response is not ("accept" or "decline"))
                throw new ArgumentException("Response must be accept or decline.", nameof(response));

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            SelectionRuntimeStatus? status;
            try
            {
                if (!_notifications.Remove(notificationId, out var notification)) return null;
                if (response == "accept")
                {
                    _recoveryBlockedPlugins.Remove(notification.PluginId);
                    if (_settings is not null && _state is not null && _reason is not null)
                    {
                        CurrentStatus = await ApplyCoreAsync(
                            _settings, _state, _reason, false, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    CurrentStatus = CopyWithNotifications(CurrentStatus);
                }

                status = CurrentStatus;
            }
            finally
            {
                _gate.Release();
            }
            if (status is not null) StatusChanged?.Invoke(this, status);
            return status;
        }

        private async Task<SelectionRuntimeStatus> ApplyCoreAsync(
            SettingsDocument settings,
            SelectionStateDocument state,
            SelectionReason reason,
            bool approveRecovery,
            CancellationToken cancellationToken)
        {
            var desiredId = state.Mode == "manual" ? state.ManualProfile! : state.LastSelectedProfile!;
            var desired = settings.Profiles!.Single(profile => profile.Id == desiredId);
            var desiredAttempt = await TryApplyProfileAsync(
                desired, approveRecovery, cancellationToken).ConfigureAwait(false);
            if (desiredAttempt.IsApplied)
            {
                return Status(state, reason, desiredId, "desired", desiredAttempt, [], []);
            }

            var skipped = new List<SkippedFallback>
            {
                new() { ProfileId = desiredId, ReasonCode = desiredAttempt.ReasonCode! }
            };
            var visited = new HashSet<string>(StringComparer.Ordinal) { desiredId };
            async Task<SelectionRuntimeStatus?> TryFallbacksAsync(ProfileSettings profile, int? rootIndex)
            {
                var candidates = profile.Fallback ?? [];
                var failedProfiles = new List<(ProfileSettings Profile, int RootIndex)>();
                for (var index = 0; index < candidates.Count; index++)
                {
                    var candidate = candidates[index];
                    var candidateIndex = rootIndex ?? index;
                    if (candidate.Kind == "native-setup")
                    {
                        return NativeFallbackStatus(
                            state, reason, desiredId, candidateIndex, desiredAttempt, skipped);
                    }

                    if (candidate.Profile is null || !visited.Add(candidate.Profile))
                    {
                        var priorReason = skipped.LastOrDefault(item => item.ProfileId == candidate.Profile)?.ReasonCode;
                        skipped.Add(new SkippedFallback
                        {
                            ProfileId = candidate.Profile ?? "unknown",
                            ReasonCode = priorReason ?? "resource-not-found"
                        });
                        continue;
                    }

                    var fallback = settings.Profiles!.SingleOrDefault(item => item.Id == candidate.Profile);
                    if (fallback is null)
                    {
                        skipped.Add(new SkippedFallback
                        {
                            ProfileId = candidate.Profile,
                            ReasonCode = "resource-not-found"
                        });
                        continue;
                    }
                    var attempt = await TryApplyProfileAsync(
                        fallback, approveRecovery: false, cancellationToken).ConfigureAwait(false);
                    if (attempt.IsApplied)
                    {
                        return Status(
                            state, reason, fallback.Id!, "fallback", attempt, skipped, [], candidateIndex);
                    }
                    skipped.Add(new SkippedFallback
                    {
                        ProfileId = fallback.Id!,
                        ReasonCode = attempt.ReasonCode!
                    });
                    failedProfiles.Add((fallback, candidateIndex));
                }

                foreach (var failed in failedProfiles)
                {
                    var nested = await TryFallbacksAsync(
                        failed.Profile, failed.RootIndex).ConfigureAwait(false);
                    if (nested is not null) return nested;
                }
                return null;
            }

            var fallbackStatus = await TryFallbacksAsync(desired, null).ConfigureAwait(false);
            return fallbackStatus ?? Status(state, reason, desiredId, "fallback", desiredAttempt, skipped,
                [new PendingBinding
                {
                    BindingId = desired.Bindings![0].Id!,
                    ReasonCode = desiredAttempt.ReasonCode!,
                    Message = desiredAttempt.Message!
                }]);
        }

        private async Task<ProfileAttempt> TryApplyProfileAsync(
            ProfileSettings profile,
            bool approveRecovery,
            CancellationToken cancellationToken)
        {
            var binding = profile.Bindings![0];
            if (binding.RenderingAuthority == "application")
                return ProfileAttempt.Fail(profile.Id!, binding, "native-output-unavailable", "Native output is not available yet.");
            if (_unavailablePlugins.TryGetValue(binding.Plugin!, out var unavailable))
                return ProfileAttempt.Fail(
                    profile.Id!, binding, unavailable.ReasonCode, unavailable.Message);
            if (_recoveryBlockedPlugins.Contains(binding.Plugin!))
            {
                if (!approveRecovery)
                    return ProfileAttempt.Fail(profile.Id!, binding, "provider-not-running", "Plugin recovery is waiting for approval.");
                _recoveryBlockedPlugins.Remove(binding.Plugin!);
                _notifications.Remove(NotificationId(binding.Plugin!));
            }

            var capabilities = _gateway.GetCapabilities(binding.Plugin!);
            if (capabilities is null)
                return ProfileAttempt.Fail(profile.Id!, binding, "plugin-absent", "The plugin is not running.");
            if (!Supports(capabilities, "provider.effect.select"))
                return ProfileAttempt.Fail(profile.Id!, binding, "capability-unsupported", "Effect selection is unsupported.");
            if (binding.Layout is not null && !Supports(capabilities, "provider.layout.select"))
                return ProfileAttempt.Fail(profile.Id!, binding, "capability-unsupported", "Layout selection is unsupported.");

            var effect = await ResolveAsync(binding.Plugin!, "effect", binding.Effect!, cancellationToken).ConfigureAwait(false);
            if (!effect.IsSuccess)
                return ProfileAttempt.Fail(profile.Id!, binding, effect.ErrorCode!, effect.ErrorMessage!);
            ResolvedResource? layout = null;
            if (binding.Layout is not null)
            {
                layout = await ResolveAsync(binding.Plugin!, "layout", binding.Layout, cancellationToken).ConfigureAwait(false);
                if (!layout.IsSuccess)
                    return ProfileAttempt.Fail(profile.Id!, binding, layout.ErrorCode!, layout.ErrorMessage!);
            }

            var ownershipFailure = await ReleaseCompetingOwnerAsync(
                binding, approveRecovery, cancellationToken).ConfigureAwait(false);
            if (ownershipFailure is not null)
                return ProfileAttempt.Fail(profile.Id!, binding, "device-owned-elsewhere", ownershipFailure);

            var apply = await _gateway.ApplyBindingAsync(binding.Plugin!, new BindingApplyParameters
            {
                Profile = profile.Id!,
                BindingId = binding.Id!,
                RenderingAuthority = binding.RenderingAuthority!,
                DeviceScope = new PluginDeviceScope
                {
                    Kind = binding.DeviceScope!.Kind!,
                    DeviceIds = binding.DeviceScope.DeviceIds
                },
                Effect = effect.Value,
                Layout = layout?.Value
            }, cancellationToken).ConfigureAwait(false);
            if (!apply.IsSuccess)
                return ProfileAttempt.Fail(profile.Id!, binding, apply.ErrorCode!, apply.ErrorMessage!);

            var result = apply.Value!;
            if (result.Outcome is not ("applied" or "unverified"))
            {
                var code = result.Reason?.Code ?? result.Outcome;
                return ProfileAttempt.Fail(profile.Id!, binding, code,
                    result.Reason?.Message ?? $"Plugin returned {result.Outcome}.", result.Outcome);
            }

            var applied = ProfileAttempt.Applied(profile.Id!, binding, result);
            _ownedBinding = new AppliedBinding(
                profile.Id!, binding.Plugin!, binding.Id!, ScopeKeys(binding), applied.BindingResult);
            return applied;
        }

        private async Task<string?> ReleaseCompetingOwnerAsync(
            BindingSettings incoming,
            bool allowDeadOwnerOverride,
            CancellationToken cancellationToken)
        {
            if (_ownedBinding is null
                || _ownedBinding.PluginId == incoming.Plugin
                || !ScopesOverlap(_ownedBinding.ScopeKeys, ScopeKeys(incoming)))
            {
                return null;
            }

            if (allowDeadOwnerOverride
                && _unavailablePlugins.TryGetValue(_ownedBinding.PluginId, out var unavailable)
                && unavailable.ReasonCode is "plugin-exited" or "plugin-absent")
            {
                _ownedBinding = null;
                return null;
            }

            var release = await _gateway.ReleaseBindingAsync(_ownedBinding.PluginId, new BindingReleaseParameters
            {
                Profile = _ownedBinding.ProfileId,
                BindingId = _ownedBinding.BindingId
            }, cancellationToken).ConfigureAwait(false);
            if (!release.IsSuccess)
            {
                if (allowDeadOwnerOverride && release.ErrorCode == "plugin-exited")
                {
                    _ownedBinding = null;
                    return null;
                }
                return "The previous writer did not confirm device release.";
            }
            if (release.Value!.Outcome is not ("released" or "nothing-to-release"))
                return "The previous writer did not confirm device release.";
            _ownedBinding = null;
            return null;
        }

        private async Task<ResolvedResource> ResolveAsync(
            string pluginId,
            string kind,
            ResourceReference resource,
            CancellationToken cancellationToken)
        {
            if (resource.ResourceId is not null)
            {
                return ResolvedResource.Ok(new PluginResourceReference
                {
                    ResourceId = resource.ResourceId,
                    DisplayName = resource.DisplayName!
                });
            }

            var catalog = await _gateway.ListCatalogAsync(pluginId, kind, cancellationToken).ConfigureAwait(false);
            if (!catalog.IsSuccess)
                return ResolvedResource.Fail(catalog.ErrorCode!, catalog.ErrorMessage!);
            if (catalog.Value!.Support != "supported")
                return ResolvedResource.Fail("capability-unsupported", $"The plugin does not provide a {kind} catalog.");
            var matches = catalog.Value.Items
                .Where(item => item.DisplayName == resource.DisplayName)
                .ToList();
            if (matches.Count != 1)
                return ResolvedResource.Fail(matches.Count == 0 ? "resource-not-found" : "resource-ambiguous", matches.Count == 0
                    ? $"{kind} '{resource.DisplayName}' was not found."
                    : $"{kind} '{resource.DisplayName}' is ambiguous.");
            var match = matches[0];
            if (!match.Available)
                return ResolvedResource.Fail(
                    match.Reason?.Code ?? "resource-not-found",
                    match.Reason?.Message ?? $"{kind} '{resource.DisplayName}' is unavailable.");
            return ResolvedResource.Ok(new PluginResourceReference
            {
                ResourceId = match.ResourceId,
                DisplayName = match.DisplayName
            });
        }

        private SelectionRuntimeStatus NativeFallbackStatus(
            SelectionStateDocument state,
            SelectionReason reason,
            string desiredId,
            int candidateIndex,
            ProfileAttempt desiredAttempt,
            IReadOnlyList<SkippedFallback> skipped) => Status(
                state,
                reason,
                desiredId,
                "fallback",
                desiredAttempt,
                skipped,
                [new PendingBinding
                {
                    BindingId = _nativeSetup.Bindings!.FirstOrDefault()?.Id ?? "native-setup",
                    ReasonCode = "native-output-unavailable",
                    Message = "Native fallback is pending until a native output plugin is available."
                }],
                candidateIndex);

        private SelectionRuntimeStatus Status(
            SelectionStateDocument state,
            SelectionReason reason,
            string effectiveProfileId,
            string source,
            ProfileAttempt attempt,
            IReadOnlyList<SkippedFallback> skipped,
            IReadOnlyList<PendingBinding> pending,
            int? candidateIndex = null) => new()
            {
                Sequence = ++_statusSequence,
                Mode = state.Mode!,
                Desired = new SelectionDecision
                {
                    ProfileId = state.Mode == "manual" ? state.ManualProfile! : state.LastSelectedProfile!,
                    Reason = reason
                },
                Effective = new EffectiveSelection
                {
                    ProfileId = effectiveProfileId,
                    Source = source,
                    CandidateIndex = candidateIndex,
                    Skipped = skipped
                },
                Applied = attempt.IsApplied
                    ? new AppliedSelection
                    {
                        ProfileId = attempt.ProfileId,
                        Bindings = [attempt.BindingResult]
                    }
                    : _ownedBinding is not null && !_unavailablePlugins.ContainsKey(_ownedBinding.PluginId)
                        ? new AppliedSelection
                        {
                            ProfileId = _ownedBinding.ProfileId,
                            Bindings = [_ownedBinding.BindingResult]
                        }
                        : new AppliedSelection
                        {
                            ProfileId = attempt.ProfileId,
                            Bindings = [attempt.BindingResult]
                        },
                Pending = pending,
                Notifications = _notifications.Values.ToList(),
                AppliedDetail = attempt.IsApplied
                    ? $"{attempt.ProfileId} output {attempt.BindingResult.Outcome}."
                    : pending.Count > 0
                        ? pending[0].Message
                        : attempt.Message!
            };

        private SelectionRuntimeStatus? CopyWithNotifications(SelectionRuntimeStatus? status) => status is null ? null : new()
        {
            Sequence = ++_statusSequence,
            Mode = status.Mode,
            Desired = status.Desired,
            Effective = status.Effective,
            Applied = status.Applied,
            Pending = status.Pending,
            Notifications = _notifications.Values.ToList(),
            AppliedDetail = status.AppliedDetail
        };

        private static bool Supports(IReadOnlyList<PluginCapability> capabilities, string id) =>
            capabilities.Any(capability => capability.Id == id && capability.Support is "supported" or "unverified");

        private static List<string> ScopeKeys(BindingSettings binding) =>
            binding.DeviceScope!.Kind == "provider-managed"
                ? ["provider-managed"]
                : binding.DeviceScope.DeviceIds!.Select(id => "device:" + id).ToList();

        private static bool ScopesOverlap(IReadOnlyList<string> current, IReadOnlyList<string> incoming) =>
            current.Contains("provider-managed", StringComparer.Ordinal)
            || incoming.Contains("provider-managed", StringComparer.Ordinal)
            || current.Intersect(incoming, StringComparer.Ordinal).Any();

        private static string NotificationId(string pluginId) => "plugin-recovered:" + pluginId;

        public void Dispose()
        {
            _gate.Wait();
            _gate.Dispose();
        }

        private sealed record AppliedBinding(
            string ProfileId,
            string PluginId,
            string BindingId,
            IReadOnlyList<string> ScopeKeys,
            BindingRuntimeResult BindingResult);

        private sealed class ResolvedResource
        {
            public bool IsSuccess => Value is not null;
            public PluginResourceReference? Value { get; private init; }
            public string? ErrorCode { get; private init; }
            public string? ErrorMessage { get; private init; }
            public static ResolvedResource Ok(PluginResourceReference value) => new() { Value = value };
            public static ResolvedResource Fail(string code, string message) => new()
            {
                ErrorCode = code,
                ErrorMessage = message
            };
        }

        private sealed class ProfileAttempt
        {
            public required string ProfileId { get; init; }
            public required BindingRuntimeResult BindingResult { get; init; }
            public bool IsApplied => BindingResult.Outcome is "applied" or "unverified";
            public string? ReasonCode => BindingResult.ReasonCode;
            public string? Message => BindingResult.Message;

            public static ProfileAttempt Applied(
                string profileId,
                BindingSettings binding,
                BindingApplyResult result) => new()
                {
                    ProfileId = profileId,
                    BindingResult = new BindingRuntimeResult
                    {
                        BindingId = binding.Id!,
                        PluginId = binding.Plugin!,
                        Outcome = result.Outcome,
                        ReasonCode = result.Reason?.Code,
                        Message = result.Reason?.Message
                    }
                };

            public static ProfileAttempt Fail(
                string profileId,
                BindingSettings binding,
                string code,
                string message,
                string outcome = "unavailable") => new()
                {
                    ProfileId = profileId,
                    BindingResult = new BindingRuntimeResult
                    {
                        BindingId = binding.Id!,
                        PluginId = binding.Plugin!,
                        Outcome = outcome,
                        ReasonCode = code,
                        Message = message
                    }
                };
        }
    }

    /// <summary>Reports the selected effective profile without replacing desired intent.</summary>
    public sealed class EffectiveSelection
    {
        public required string ProfileId { get; init; }
        public required string Source { get; init; }
        public int? CandidateIndex { get; init; }
        public IReadOnlyList<SkippedFallback> Skipped { get; init; } = [];
    }

    /// <summary>Explains one fallback candidate that could not take control.</summary>
    public sealed class SkippedFallback
    {
        public required string ProfileId { get; init; }
        public required string ReasonCode { get; init; }
    }

    /// <summary>Collects the latest binding outcomes reported for one profile.</summary>
    public sealed class AppliedSelection
    {
        public required string ProfileId { get; init; }
        public IReadOnlyList<BindingRuntimeResult> Bindings { get; init; } = [];
    }

    /// <summary>Reports one binding result without claiming failed output was visible.</summary>
    public sealed class BindingRuntimeResult
    {
        public required string BindingId { get; init; }
        public required string PluginId { get; init; }
        public required string Outcome { get; init; }
        public string? ReasonCode { get; init; }
        public string? Message { get; init; }
    }

    /// <summary>Reports output that cannot yet be sent.</summary>
    public sealed class PendingBinding
    {
        public required string BindingId { get; init; }
        public required string ReasonCode { get; init; }
        public required string Message { get; init; }
    }

    /// <summary>Requires an explicit response before a recovered plugin can resume.</summary>
    public sealed class RuntimeNotification
    {
        public required string Id { get; init; }
        public required string Kind { get; init; }
        public required string PluginId { get; init; }
        public IReadOnlyList<string> Actions { get; init; } = [];
    }
}
