using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FocusRgb.Core.Commands;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Coordination;
using FocusRgb.Core.Persistence;
using FocusRgb.Core.Selection;

namespace FocusRgb.App.ViewModels
{
    /// <summary>Connects the desktop shell to persisted local selection and profile state.</summary>
    public sealed class MainWindowViewModel : INotifyPropertyChanged
    {
        private bool _isManual = true;
        private readonly LocalStateStore? _settingsStore;
        private readonly SelectionCommandService? _selectionCommands;
        private SettingsDocument _settings;
        private SelectionEngine _selectionEngine;
        private SelectionStateDocument? _selectionState;
        private SelectionReason _selectionReason = FocusRgb.Core.Selection.SelectionReason.Manual();
        private SelectionRuntimeStatus? _runtimeStatus;
        private string _runtimeStatusMessage = "Preview state; runtime persistence is not connected.";
        private bool _isRuntimeCommandPending;
        private ProfileOptionViewModel _selectedProfile;
        private ProfileOptionViewModel? _editingProfile;
        private ApplicationGroupViewModel? _editingApplicationGroup;
        private string _profileName = "";
        private string _profileEffect = "";
        private string _profileLayout = "";
        private string _profileFallbackKind = "native-setup";
        private ProfileOptionViewModel? _profileFallbackProfile;
        private bool _profileLivePreview;
        private bool _isProfileDirty;
        private bool _loadingProfileDraft;
        private string _applicationGroupName = "";
        private string? _selectedDraftExecutable;
        private string? _selectedDetectedExecutable;
        private string _manualExecutable = "";
        private string _profileEditorStatus = "Edit the selected profile below.";
        private string _startupStatusMessage = "";
        private string _applicationGroupEditorStatus = "Select an application group to edit it.";
        private string _closeButtonBehavior = "Minimize to system tray";
        private bool _startMinimized;
        private string _applicationTheme = "System";
        private bool _verboseLogging;
        private bool _isApplicationSettingsDirty;
        private int _settingsSavePending;
        private string? _settingsSaveWarning;
        private bool _loadingApplicationSettings;
        private string _applicationSettingsStatus = "Application preferences match the saved values.";

        private MainWindowViewModel(
            ObservableCollection<ProfileOptionViewModel> profiles,
            ProfileOptionViewModel selectedProfile,
            MappingEditorViewModel mappingEditor,
            SettingsDocument settings,
            LocalStateStore? settingsStore,
            SelectionStateDocument? selectionState,
            SelectionCommandService? selectionCommands,
            SelectionReason? selectionReason,
            SelectionRuntimeStatus? runtimeStatus,
            bool canMinimizeToTray)
        {
            Profiles = profiles;
            _selectedProfile = selectedProfile;
            MappingEditor = mappingEditor;
            _settings = settings;
            _selectionEngine = new SelectionEngine(settings);
            _settingsStore = settingsStore;
            _selectionState = selectionState;
            _selectionCommands = selectionCommands;
            _runtimeStatus = runtimeStatus;
            CanMinimizeToTray = canMinimizeToTray;
            if (selectionReason is not null) _selectionReason = selectionReason;
            if (selectionCommands is not null)
                _runtimeStatusMessage = runtimeStatus?.AppliedDetail ?? "Local runtime is ready; no output integration is connected.";
            EditingProfile = profiles.Count > 0 ? profiles[0] : null;
            EditingApplicationGroup = ApplicationGroups.Count > 0 ? ApplicationGroups[0] : null;
            LoadApplicationSettings();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<ProfileOptionViewModel> Profiles { get; }
        public MappingEditorViewModel MappingEditor { get; }
        public ObservableCollection<ApplicationGroupViewModel> ApplicationGroups => MappingEditor.ApplicationGroups;
        public ObservableCollection<string> DraftExecutables { get; } = [];
        public ObservableCollection<string> DetectedExecutables { get; } = [];
        public ObservableCollection<string> IntegrationStatuses { get; } = ["No plugins configured."];
        public IReadOnlyList<string> CloseButtonBehaviors => CanMinimizeToTray
            ? ["Minimize to system tray", "Exit application"]
            : ["Exit application"];
        public IReadOnlyList<string> ApplicationThemes { get; } = ["System", "Light", "Dark"];
        public bool CanMinimizeToTray { get; }

        public ProfileOptionViewModel? EditingProfile
        {
            get => _editingProfile;
            set
            {
                var discarded = _editingProfile is not null && !ReferenceEquals(_editingProfile, value) && IsProfileDirty;
                if (!Set(ref _editingProfile, value)) return;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasEditingProfile)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRemoveEditingProfile)));
                _loadingProfileDraft = true;
                ProfileName = value?.DisplayName ?? "";
                ProfileEffect = value?.EffectName ?? "";
                ProfileLayout = value?.LayoutName ?? "";
                LoadProfileFallback(value);
                _loadingProfileDraft = false;
                IsProfileDirty = false;
                if (discarded) ProfileEditorStatus = "Unsaved profile changes were discarded when the selection changed.";
            }
        }

        public ApplicationGroupViewModel? EditingApplicationGroup
        {
            get => _editingApplicationGroup;
            set
            {
                var discarded = _editingApplicationGroup is not null
                    && !ReferenceEquals(_editingApplicationGroup, value)
                    && (ApplicationGroupName != _editingApplicationGroup.DisplayName
                        || !DraftExecutables.SequenceEqual(_editingApplicationGroup.Executables));
                if (!Set(ref _editingApplicationGroup, value)) return;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasEditingApplicationGroup)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoEditingApplicationGroup)));
                ApplicationGroupName = value?.DisplayName ?? "";
                DraftExecutables.Clear();
                if (value is not null)
                    foreach (var executable in value.Executables) DraftExecutables.Add(executable);
                SelectedDraftExecutable = null;
                if (discarded) ApplicationGroupEditorStatus = "Unsaved application-group changes were discarded when the selection changed.";
            }
        }

        public string ProfileName { get => _profileName; set => SetProfileDraft(ref _profileName, value); }
        public string ProfileEffect { get => _profileEffect; set => SetProfileDraft(ref _profileEffect, value); }
        public string ProfileLayout { get => _profileLayout; set => SetProfileDraft(ref _profileLayout, value); }
        public string ProfileFallbackKind
        {
            get => _profileFallbackKind;
            set
            {
                if (!SetProfileDraft(ref _profileFallbackKind, value)) return;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsProfileFallback)));
            }
        }
        public ProfileOptionViewModel? ProfileFallbackProfile
        {
            get => _profileFallbackProfile;
            set => SetProfileDraft(ref _profileFallbackProfile, value);
        }
        public IReadOnlyList<string> ProfileFallbackKinds { get; } = ["native-setup", "profile"];
        public bool IsProfileFallback => ProfileFallbackKind == "profile";
        public bool ProfileLivePreview
        {
            get => _profileLivePreview;
            set
            {
                if (!Set(ref _profileLivePreview, value) || !value || !IsProfileDirty) return;
                ProfilePreviewRevision++;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProfilePreviewRevision)));
                ProfileEditorStatus = "Draft preview updated in the editor; output is sent after Save.";
            }
        }
        public bool IsProfileDirty { get => _isProfileDirty; private set => Set(ref _isProfileDirty, value); }
        public int ProfilePreviewRevision { get; private set; }
        public string ApplicationGroupName { get => _applicationGroupName; set => Set(ref _applicationGroupName, value); }
        public string? SelectedDraftExecutable { get => _selectedDraftExecutable; set => Set(ref _selectedDraftExecutable, value); }
        public string? SelectedDetectedExecutable { get => _selectedDetectedExecutable; set => Set(ref _selectedDetectedExecutable, value); }
        public string ManualExecutable { get => _manualExecutable; set => Set(ref _manualExecutable, value); }
        public string ProfileEditorStatus { get => _profileEditorStatus; private set => Set(ref _profileEditorStatus, value); }
        public string ApplicationGroupEditorStatus { get => _applicationGroupEditorStatus; private set => Set(ref _applicationGroupEditorStatus, value); }
        public string RuntimeStatusMessage { get => _runtimeStatusMessage; private set => Set(ref _runtimeStatusMessage, value); }
        public string StartupStatusMessage { get => _startupStatusMessage; private set => Set(ref _startupStatusMessage, value); }
        public bool HasStartupStatus => !string.IsNullOrWhiteSpace(StartupStatusMessage);
        public bool IsRuntimeCommandPending { get => _isRuntimeCommandPending; private set => Set(ref _isRuntimeCommandPending, value); }
        public bool IsSettingsSavePending => Volatile.Read(ref _settingsSavePending) != 0;
        public bool HasEditingProfile => EditingProfile is not null;
        public bool CanRemoveEditingProfile => EditingProfile is not null && EditingProfile.Id != "default";
        public bool HasEditingApplicationGroup => EditingApplicationGroup is not null;
        public bool HasNoEditingApplicationGroup => EditingApplicationGroup is null;
        public string CloseButtonBehavior
        {
            get => _closeButtonBehavior;
            set => SetApplicationSettingsDraft(ref _closeButtonBehavior, value);
        }
        public bool StartMinimized
        {
            get => _startMinimized;
            set => SetApplicationSettingsDraft(ref _startMinimized, value);
        }
        public string ApplicationTheme
        {
            get => _applicationTheme;
            set => SetApplicationSettingsDraft(ref _applicationTheme, value);
        }
        public bool VerboseLogging
        {
            get => _verboseLogging;
            set => SetApplicationSettingsDraft(ref _verboseLogging, value);
        }
        public bool IsApplicationSettingsDirty
        {
            get => _isApplicationSettingsDirty;
            private set => Set(ref _isApplicationSettingsDirty, value);
        }
        public string ApplicationSettingsStatus
        {
            get => _applicationSettingsStatus;
            private set => Set(ref _applicationSettingsStatus, value);
        }
        public bool ShouldMinimizeOnClose => CanMinimizeToTray
            && _settings.Application!.CloseButtonBehavior == "minimize-to-tray";
        public string SavedApplicationTheme => _settings.Application!.Theme!;
        public string SavedCloseButtonBehavior => _settings.Application!.CloseButtonBehavior!;
        public bool SavedStartMinimized => _settings.Application!.StartMinimized!.Value;
        public bool SavedVerboseLogging => _settings.Application!.VerboseLogging!.Value;

        public bool IsManual
        {
            get => _isManual;
            set
            {
                if (_isManual == value)
                {
                    return;
                }

                _isManual = value;
                NotifyStateChanged();
            }
        }

        public bool IsAutomatic
        {
            get => !IsManual;
            set
            {
                if (value)
                {
                    IsManual = false;
                }
            }
        }

        public ProfileOptionViewModel SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                if (ReferenceEquals(_selectedProfile, value))
                {
                    return;
                }

                _selectedProfile = value;
                NotifyStateChanged();
            }
        }

        public string DesiredProfileName => SelectedProfile.DisplayName;
        public string AppliedProfileName
        {
            get
            {
                if (_selectionCommands is null) return $"{SelectedProfile.DisplayName} (preview)";
                if (_runtimeStatus?.Applied is { } applied
                    && applied.Bindings.Any(binding => binding.Outcome is "applied" or "unverified"))
                {
                    var name = Profiles.FirstOrDefault(profile => profile.Id == applied.ProfileId)?.DisplayName
                        ?? applied.ProfileId;
                    return applied.Bindings.Any(binding => binding.Outcome == "unverified")
                        ? $"{name} (unverified)"
                        : name;
                }
                return _runtimeStatus?.Pending.Count > 0 && _runtimeStatus.Pending[0] is { } pending
                    ? $"Not applied ({pending.ReasonCode})"
                    : "Not applied";
            }
        }
        public bool HasRecoveryNotification => _runtimeStatus?.Notifications.Count > 0;
        public string RecoveryNotificationMessage => HasRecoveryNotification
            ? $"Plugin '{_runtimeStatus!.Notifications[0].PluginId}' recovered. Resume current lighting intent?"
            : string.Empty;
        public string SelectionReason => _selectionCommands is null
            ? IsManual
                ? "Manual preview selection; no command was sent."
                : "Automatic preview mode; waiting for a stable application observation."
            : DescribeReason(_selectionReason);

        public async Task SelectProfileAsync(
            ProfileOptionViewModel profile,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(profile);
            if (_selectionCommands is null || _selectionState is null)
            {
                SelectedProfile = profile;
                return;
            }

            await RunRuntimeCommandAsync(
                () => _selectionCommands.SelectProfileAsync(
                    _settings,
                    _selectionState,
                    profile.Id,
                    DateTimeOffset.UtcNow,
                    cancellationToken),
                $"Selected {profile.DisplayName} in manual mode.");
        }

        public async Task SetModeAsync(bool manual, CancellationToken cancellationToken = default)
        {
            if (_selectionCommands is null || _selectionState is null)
            {
                IsManual = manual;
                return;
            }

            await RunRuntimeCommandAsync(
                () => _selectionCommands.SetModeAsync(
                    _settings,
                    _selectionState,
                    manual ? "manual" : "automatic",
                    DateTimeOffset.UtcNow,
                    cancellationToken),
                manual
                    ? "Manual mode saved."
                    : "Automatic mode saved; waiting for an application match.");
        }

        public async Task<bool> SaveRuntimeStateAsync(CancellationToken cancellationToken = default)
        {
            if (_selectionCommands is null || _selectionState is null) return true;
            var result = await _selectionCommands.SaveCurrentAsync(
                _settings,
                _selectionState,
                DateTimeOffset.UtcNow,
                cancellationToken);
            if (!result.IsSuccess)
            {
                RuntimeStatusMessage = $"Could not save before quitting: {result.ErrorMessage}";
                return false;
            }

            ApplyRuntimeResult(result);
            RuntimeStatusMessage = "Selection state saved. FocusRGB can quit safely.";
            return true;
        }

        public async Task RespondToRecoveryAsync(
            bool accept,
            CancellationToken cancellationToken = default)
        {
            var status = _runtimeStatus;
            if (_selectionCommands is null || _selectionState is null
                || status is null || status.Notifications.Count == 0)
            {
                return;
            }
            var notification = status.Notifications[0];

            await RunRuntimeCommandAsync(
                () => _selectionCommands.RespondToNotificationAsync(
                    _settings,
                    _selectionState,
                    notification.Id,
                    accept ? "accept" : "decline",
                    cancellationToken),
                accept ? "Recovered plugin accepted for current intent." : "Recovered plugin remains inactive.");
        }

        public void NotifyWindowHidden() =>
            RuntimeStatusMessage = "FocusRGB is still running in the system tray.";

        public async Task<bool> SaveApplicationSettingsAsync(CancellationToken cancellationToken = default)
        {
            if (!BeginSettingsSave()) return false;
            try
            {
                var application = new ApplicationSettings
                {
                    CloseButtonBehavior = CloseButtonBehavior == "Exit application" ? "exit" : "minimize-to-tray",
                    StartMinimized = StartMinimized,
                    Theme = ApplicationTheme.ToLowerInvariant(),
                    VerboseLogging = VerboseLogging,
                    AdditionalProperties = _settings.Application!.AdditionalProperties
                };
                var candidate = WithApplication(application);
                var error = await SaveCandidateAsync(candidate, cancellationToken);
                if (error is not null)
                {
                    ApplicationSettingsStatus = error;
                    return false;
                }

                _settings = candidate;
                IsApplicationSettingsDirty = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShouldMinimizeOnClose)));
                ApplicationSettingsStatus = SavedStatus("Application preferences saved.");
                return true;
            }
            finally
            {
                EndSettingsSave();
            }
        }

        public void CancelApplicationSettings()
        {
            LoadApplicationSettings();
            ApplicationSettingsStatus = "Application preferences restored to the saved values.";
        }

        public void ReportSettingsAction(string message) => ApplicationSettingsStatus = message;

        public void ReportUnexpectedError(string message) => RuntimeStatusMessage = message;

        public void SetIntegrationStatuses(IEnumerable<string> statuses)
        {
            IntegrationStatuses.Clear();
            foreach (var status in statuses) IntegrationStatuses.Add(status);
            if (IntegrationStatuses.Count == 0) IntegrationStatuses.Add("No plugins configured.");
        }

        public async Task SaveProfileAsync(CancellationToken cancellationToken = default)
        {
            if (EditingProfile is null) return;
            if (!BeginSettingsSave()) return;
            try
            {
                var name = EditingProfile.Id == "default"
                    ? EditingProfile.DisplayName
                    : RequiredName(ProfileName, "Profile");
                var replacement = CreateProfileSettings(EditingProfile.Id, name, TemplateFor(EditingProfile.Id));
                var candidate = WithProfiles(_settings.Profiles!
                    .Select(profile => profile.Id == EditingProfile.Id ? replacement : profile)
                    .ToList());
                var error = await SaveCandidateAsync(candidate, cancellationToken);
                if (error is not null)
                {
                    ProfileEditorStatus = error;
                    return;
                }

                _settings = candidate;
                await ReapplyOutputAsync(cancellationToken);
                EditingProfile.DisplayName = name;
                EditingProfile.EffectName = replacement.Bindings![0].Effect!.DisplayName!;
                EditingProfile.LayoutName = replacement.Bindings[0].Layout?.DisplayName ?? "Current provider layout";
                ReloadProfileDraft();
                NotifyStateChanged(nameof(SelectedProfile));
                ProfileEditorStatus = SavedStatus($"Saved {EditingProfile.DisplayName}.");
            }
            finally
            {
                EndSettingsSave();
            }
        }

        public async Task SaveProfileAsAsync(CancellationToken cancellationToken = default)
        {
            if (!BeginSettingsSave()) return;
            try
            {
                var name = RequiredName(ProfileName, "New Profile");
                var id = UniqueId(name, Profiles.Select(item => item.Id));
                var created = CreateProfileSettings(id, name, TemplateFor(EditingProfile?.Id ?? "default"));
                var candidate = WithProfiles([.. _settings.Profiles!, created]);
                var error = await SaveCandidateAsync(candidate, cancellationToken);
                if (error is not null)
                {
                    ProfileEditorStatus = error;
                    return;
                }

                var profile = new ProfileOptionViewModel
                {
                    Id = id,
                    DisplayName = name,
                    EffectName = created.Bindings![0].Effect!.DisplayName!,
                    LayoutName = created.Bindings[0].Layout?.DisplayName ?? "Current provider layout"
                };
                _settings = candidate;
                Profiles.Add(profile);
                EditingProfile = profile;
                MappingEditor.NewProfile = profile;
                ProfileEditorStatus = SavedStatus($"Saved {name} as a new profile.");
            }
            finally
            {
                EndSettingsSave();
            }
        }

        public async Task RemoveProfileAsync(CancellationToken cancellationToken = default)
        {
            if (EditingProfile is null) return;
            if (!BeginSettingsSave()) return;
            try
            {
                if (EditingProfile.Id == "default")
                {
                    ProfileEditorStatus = "Default is protected and cannot be removed.";
                    return;
                }
                if (_settings.Rules!.Any(rule => rule.Profile == EditingProfile.Id)
                    || MappingEditor.AppliedMappings.Any(mapping => mapping.ProfileId == EditingProfile.Id)
                    || MappingEditor.Mappings.Any(mapping => mapping.SelectedProfile.Id == EditingProfile.Id))
                {
                    ProfileEditorStatus = "rules: reassign or remove mappings that use this profile first.";
                    return;
                }
                var fallbackOwner = _settings.Profiles!
                    .Select((profile, index) => new { Profile = profile, Index = index })
                    .FirstOrDefault(item => item.Profile.Fallback!.Any(fallback =>
                        fallback.Kind == "profile" && fallback.Profile == EditingProfile.Id));
                if (fallbackOwner is not null)
                {
                    ProfileEditorStatus = $"profiles[{fallbackOwner.Index}].fallback: reassign this profile first.";
                    return;
                }

                var removed = EditingProfile.DisplayName;
                var candidate = WithProfiles(_settings.Profiles!
                    .Where(profile => profile.Id != EditingProfile.Id)
                    .ToList());
                var validationError = ValidateCandidate(candidate);
                if (validationError is not null)
                {
                    ProfileEditorStatus = validationError;
                    return;
                }
                var stateError = await SaveRemovalStateAsync(EditingProfile.Id, candidate, cancellationToken);
                if (stateError is not null)
                {
                    ProfileEditorStatus = stateError;
                    return;
                }
                var error = await SaveCandidateAsync(candidate, cancellationToken);
                if (error is not null)
                {
                    ProfileEditorStatus = error;
                    return;
                }

                _settings = candidate;
                await ReapplyOutputAsync(cancellationToken);
                if (ReferenceEquals(SelectedProfile, EditingProfile))
                    SelectedProfile = Profiles.First(profile => !ReferenceEquals(profile, EditingProfile));
                Profiles.Remove(EditingProfile);
                EditingProfile = Profiles.Count > 0 ? Profiles[0] : null;
                MappingEditor.NewProfile = EditingProfile;
                ProfileEditorStatus = SavedStatus($"Removed {removed}.");
            }
            finally
            {
                EndSettingsSave();
            }
        }

        public void CancelProfile()
        {
            ReloadProfileDraft();
            ProfilePreviewRevision++;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProfilePreviewRevision)));
            ProfileEditorStatus = "Profile draft restored to the last saved values.";
        }

        public bool AddDraftExecutable(string path)
        {
            var executable = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(executable)
                || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                ApplicationGroupEditorStatus = "Enter a Windows executable name ending in .exe.";
                return false;
            }
            if (!DraftExecutables.Contains(executable, StringComparer.OrdinalIgnoreCase))
                DraftExecutables.Add(executable);
            return true;
        }

        public void AddDetectedExecutable()
        {
            if (SelectedDetectedExecutable is not null) AddDraftExecutable(SelectedDetectedExecutable);
        }

        public void AddManualExecutable()
        {
            if (AddDraftExecutable(ManualExecutable)) ManualExecutable = "";
        }

        public async Task ObserveApplicationsAsync(
            ApplicationObservation observation,
            CancellationToken cancellationToken = default)
        {
            var detected = observation.RunningExecutables
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            DetectedExecutables.Clear();
            foreach (var executable in detected) DetectedExecutables.Add(executable);
            if (SelectedDetectedExecutable is not null
                && !DetectedExecutables.Contains(SelectedDetectedExecutable, StringComparer.OrdinalIgnoreCase))
                SelectedDetectedExecutable = null;

            if (_selectionCommands is null || _selectionState is null || _selectionState.Mode != "automatic") return;
            var result = await _selectionCommands.ObserveAsync(
                _settings, _selectionState, _selectionEngine, observation, DateTimeOffset.UtcNow, cancellationToken);
            if (!result.IsSuccess)
            {
                RuntimeStatusMessage = result.ErrorMessage!;
                return;
            }
            ApplyRuntimeResult(result);
            RuntimeStatusMessage = result.Status!.AppliedDetail;
        }

        public void RemoveDraftExecutable()
        {
            if (SelectedDraftExecutable is null) return;
            DraftExecutables.Remove(SelectedDraftExecutable);
            SelectedDraftExecutable = null;
        }

        public async Task SaveApplicationGroupAsync(CancellationToken cancellationToken = default)
        {
            if (EditingApplicationGroup is null) return;
            if (!BeginSettingsSave()) return;
            try
            {
                var name = RequiredName(ApplicationGroupName, "Application Group");
                var replacement = CreateApplicationGroupSettings(EditingApplicationGroup.Id, name, DraftExecutables);
                var candidate = WithMappings(
                    _settings.ApplicationGroups!.Select(group =>
                        group.Id == EditingApplicationGroup.Id ? replacement : group).ToList(),
                    _settings.Rules!);
                var error = await SaveCandidateAsync(candidate, cancellationToken);
                if (error is not null)
                {
                    ApplicationGroupEditorStatus = error;
                    return;
                }

                _settings = candidate;
                _selectionEngine = new SelectionEngine(candidate);
                EditingApplicationGroup.DisplayName = name;
                ReplaceExecutables(EditingApplicationGroup);
                ApplicationGroupEditorStatus = SavedStatus($"Saved {name}.");
            }
            finally
            {
                EndSettingsSave();
            }
        }

        public async Task SaveApplicationGroupAsAsync(CancellationToken cancellationToken = default)
        {
            if (!BeginSettingsSave()) return;
            try
            {
                var name = RequiredName(ApplicationGroupName, "New Application Group");
                var id = UniqueId(name, ApplicationGroups.Select(item => item.Id));
                var created = CreateApplicationGroupSettings(id, name, DraftExecutables);
                var candidate = WithMappings([.. _settings.ApplicationGroups!, created], _settings.Rules!);
                var error = await SaveCandidateAsync(candidate, cancellationToken);
                if (error is not null)
                {
                    ApplicationGroupEditorStatus = error;
                    return;
                }

                var group = new ApplicationGroupViewModel(id, name, created.Executables!);
                _settings = candidate;
                _selectionEngine = new SelectionEngine(candidate);
                ApplicationGroups.Add(group);
                EditingApplicationGroup = group;
                MappingEditor.NewApplicationGroup = group;
                ApplicationGroupEditorStatus = SavedStatus($"Saved {name} as a new group.");
            }
            finally
            {
                EndSettingsSave();
            }
        }

        public async Task RemoveApplicationGroupAsync(CancellationToken cancellationToken = default)
        {
            if (EditingApplicationGroup is null) return;
            if (!BeginSettingsSave()) return;
            try
            {
            if (MappingEditor.AppliedMappings.Any(mapping => mapping.ApplicationGroupId == EditingApplicationGroup.Id)
                || MappingEditor.Mappings.Any(mapping => mapping.SelectedApplicationGroup.Id == EditingApplicationGroup.Id))
            {
                ApplicationGroupEditorStatus = "Remove mappings that use this application group first.";
                return;
            }
            var removed = EditingApplicationGroup.DisplayName;
            var candidate = WithMappings(
                _settings.ApplicationGroups!.Where(group => group.Id != EditingApplicationGroup.Id).ToList(),
                _settings.Rules!);
            var error = await SaveCandidateAsync(candidate, cancellationToken);
            if (error is not null)
            {
                ApplicationGroupEditorStatus = error;
                return;
            }
            _settings = candidate;
            _selectionEngine = new SelectionEngine(candidate);
            ApplicationGroups.Remove(EditingApplicationGroup);
            EditingApplicationGroup = ApplicationGroups.Count > 0 ? ApplicationGroups[0] : null;
            MappingEditor.NewApplicationGroup = EditingApplicationGroup;
            ApplicationGroupEditorStatus = SavedStatus($"Removed {removed}.");
            }
            finally
            {
                EndSettingsSave();
            }
        }

        public async Task SaveMappingsAsync(CancellationToken cancellationToken = default)
        {
            if (!BeginSettingsSave()) return;
            try
            {
                var candidate = WithMappings(CreateApplicationGroupSettings(), MappingEditor.CreateRuleSettings());
                var error = await SaveCandidateAsync(candidate, cancellationToken);
                if (error is not null)
                {
                    MappingEditor.ReportSaveFailure(error);
                    return;
                }

                _settings = candidate;
                _selectionEngine = new SelectionEngine(candidate);
                MappingEditor.Apply();
            }
            finally
            {
                EndSettingsSave();
            }
        }

        public static MainWindowViewModel CreatePreview()
        {
            var result = SettingsJson.LoadDefaults();
            if (!result.IsSuccess || result.Settings is null)
            {
                throw new InvalidOperationException("Validated default profiles are unavailable.");
            }

            return Create(result.Settings, null, null, null, null, null, canMinimizeToTray: true);
        }

        public static MainWindowViewModel CreatePreview(SettingsDocument settings)
            => Create(settings, null, null, null, null, null, canMinimizeToTray: true);

        public static MainWindowViewModel CreatePersistent(
            StartupSnapshot snapshot,
            LocalStateStore settingsStore,
            bool canMinimizeToTray = true,
            OutputCoordinator? outputCoordinator = null,
            SelectionRuntimeStatus? runtimeStatus = null)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(settingsStore);
            var viewModel = Create(
                snapshot.Settings,
                settingsStore,
                snapshot.State,
                new SelectionCommandService(settingsStore, outputCoordinator),
                snapshot.Desired.Reason,
                runtimeStatus,
                canMinimizeToTray);
            viewModel.IsManual = snapshot.State.Mode == "manual";
            var selectedId = viewModel.IsManual
                ? snapshot.State.ManualProfile
                : snapshot.State.LastSelectedProfile;
            viewModel.SelectedProfile = viewModel.Profiles.Single(profile => profile.Id == selectedId);
            viewModel.StartupStatusMessage = $"Settings loaded from {snapshot.SettingsSource}.";
            foreach (var recovery in snapshot.Recoveries)
            {
                viewModel.StartupStatusMessage += recovery.ArchivedPath is null
                    ? $" {recovery.FileName}: {recovery.Message}"
                    : $" Recovered {recovery.FileName}; the invalid file was kept as {Path.GetFileName(recovery.ArchivedPath)}.";
            }
            viewModel.PropertyChanged?.Invoke(viewModel, new PropertyChangedEventArgs(nameof(HasStartupStatus)));
            return viewModel;
        }

        private static MainWindowViewModel Create(
            SettingsDocument settings,
            LocalStateStore? settingsStore,
            SelectionStateDocument? selectionState,
            SelectionCommandService? selectionCommands,
            SelectionReason? selectionReason,
            SelectionRuntimeStatus? runtimeStatus,
            bool canMinimizeToTray)
        {
            ArgumentNullException.ThrowIfNull(settings);
            if (SettingsValidator.Validate(settings).Count > 0
                || settings.Profiles is not { Count: > 0 } settingsProfiles)
            {
                throw new ArgumentException("Preview requires validated settings.", nameof(settings));
            }

            var profiles = new ObservableCollection<ProfileOptionViewModel>(settingsProfiles.Select(profile =>
            {
                var binding = profile.Bindings!.FirstOrDefault();
                return new ProfileOptionViewModel
                {
                    Id = profile.Id!,
                    DisplayName = profile.DisplayName!,
                    EffectName = binding?.Effect?.DisplayName ?? "No effect",
                    LayoutName = binding is null
                        ? "No layout"
                        : binding.Layout?.DisplayName ?? "Current provider layout"
                };
            }));
            var defaultId = settings.Selection!.DefaultProfile;
            var selectedProfile = profiles.Single(profile => profile.Id == defaultId);
            var mappingEditor = MappingEditorViewModel.Create(settings, profiles);
            return new MainWindowViewModel(
                profiles,
                selectedProfile,
                mappingEditor,
                settings,
                settingsStore,
                selectionState,
                selectionCommands,
                selectionReason,
                runtimeStatus,
                canMinimizeToTray);
        }

        private async Task RunRuntimeCommandAsync(
            Func<Task<SelectionCommandResult>> command,
            string successMessage)
        {
            if (IsRuntimeCommandPending) return;
            IsRuntimeCommandPending = true;
            try
            {
                var result = await command();
                if (!result.IsSuccess)
                {
                    RuntimeStatusMessage = result.ErrorMessage!;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsManual)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAutomatic)));
                    NotifyStateChanged(nameof(SelectedProfile));
                    return;
                }

                ApplyRuntimeResult(result);
                RuntimeStatusMessage = result.Status!.AppliedDetail;
            }
            finally
            {
                IsRuntimeCommandPending = false;
            }
        }

        private void ApplyRuntimeResult(SelectionCommandResult result)
        {
            var modeChanged = _isManual != (result.Status!.Mode == "manual");
            _selectionState = result.State!;
            if (_runtimeStatus is not null && result.Status.Sequence < _runtimeStatus.Sequence) return;
            _selectionReason = result.Status.Desired.Reason;
            _runtimeStatus = result.Status;
            _isManual = result.Status.Mode == "manual";
            if (modeChanged) _selectionEngine = new SelectionEngine(_settings);
            _selectedProfile = Profiles.Single(profile => profile.Id == result.Status.Desired.ProfileId);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsManual)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAutomatic)));
            NotifyStateChanged(nameof(SelectedProfile));
        }

        public void ApplyOutputStatus(SelectionRuntimeStatus status)
        {
            ArgumentNullException.ThrowIfNull(status);
            if (_runtimeStatus is not null && status.Sequence < _runtimeStatus.Sequence) return;
            _runtimeStatus = status;
            _selectionReason = status.Desired.Reason;
            RuntimeStatusMessage = status.AppliedDetail;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AppliedProfileName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasRecoveryNotification)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RecoveryNotificationMessage)));
        }

        private static string DescribeReason(SelectionReason reason) => reason.Kind switch
        {
            "first-run" => "First run starts with the protected Default profile.",
            "restored" => "Restored from the last saved selection.",
            "manual" => "Selected manually and saved locally.",
            "observation-pending" => "Waiting for a stable application observation.",
            "rule" => $"Matched mapping '{reason.RuleId}'.",
            "unmatched-default" => "No mapping matched; using Default.",
            "invalid-state-reset" => "Invalid saved selection was reset to Default.",
            _ => reason.Kind
        };

        private void NotifyStateChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            if (propertyName is nameof(IsManual))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAutomatic)));
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DesiredProfileName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AppliedProfileName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionReason)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasRecoveryNotification)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RecoveryNotificationMessage)));
        }

        private void ReplaceExecutables(ApplicationGroupViewModel group)
        {
            group.Executables.Clear();
            foreach (var executable in DraftExecutables) group.Executables.Add(executable);
        }

        private void ReloadProfileDraft()
        {
            if (EditingProfile is null) return;
            _loadingProfileDraft = true;
            ProfileName = EditingProfile.DisplayName;
            ProfileEffect = EditingProfile.EffectName;
            ProfileLayout = EditingProfile.LayoutName;
            LoadProfileFallback(EditingProfile);
            _loadingProfileDraft = false;
            IsProfileDirty = false;
        }

        private void LoadProfileFallback(ProfileOptionViewModel? profile)
        {
            var saved = profile is null ? null : _settings.Profiles!.Single(item => item.Id == profile.Id);
            var fallback = saved?.Fallback?.FirstOrDefault();
            ProfileFallbackKind = fallback?.Kind ?? "native-setup";
            ProfileFallbackProfile = fallback?.Kind == "profile"
                ? Profiles.FirstOrDefault(item => item.Id == fallback.Profile)
                : null;
        }

        private ProfileSettings TemplateFor(string id) =>
            _settings.Profiles!.FirstOrDefault(profile => profile.Id == id)
            ?? _settings.Profiles!.Single(profile => profile.Id == "default");

        private ProfileSettings CreateProfileSettings(string id, string name, ProfileSettings template)
        {
            var binding = template.Bindings![0];
            var savedFallback = template.Fallback!.FirstOrDefault();
            var effectName = ProfileEffect.Trim();
            var layoutName = ProfileLayout.Trim();
            var effect = new ResourceReference
            {
                ResourceId = binding.Effect?.DisplayName == effectName ? binding.Effect.ResourceId : null,
                DisplayName = effectName,
                AdditionalProperties = binding.Effect?.AdditionalProperties
            };
            ResourceReference? layout = null;
            if (!string.IsNullOrWhiteSpace(layoutName)
                && !string.Equals(layoutName, "Current provider layout", StringComparison.Ordinal))
            {
                layout = new ResourceReference
                {
                    ResourceId = binding.Layout?.DisplayName == layoutName ? binding.Layout.ResourceId : null,
                    DisplayName = layoutName,
                    AdditionalProperties = binding.Layout?.AdditionalProperties
                };
            }

            return new ProfileSettings
            {
                Id = id,
                DisplayName = name,
                Bindings =
                [
                    new BindingSettings
                    {
                        Id = binding.Id,
                        Plugin = binding.Plugin,
                        RenderingAuthority = binding.RenderingAuthority,
                        DeviceScope = binding.DeviceScope,
                        Effect = effect,
                        Layout = layout,
                        AdditionalProperties = binding.AdditionalProperties
                    }
                ],
                Fallback =
                [
                    ProfileFallbackKind == "profile"
                        ? new FallbackSettings
                        {
                            Kind = "profile",
                            Profile = ProfileFallbackProfile?.Id,
                            AdditionalProperties = savedFallback?.Kind == "profile"
                                ? savedFallback.AdditionalProperties
                                : null
                        }
                        : new FallbackSettings
                        {
                            Kind = "native-setup",
                            AdditionalProperties = savedFallback?.Kind == "native-setup"
                                ? savedFallback.AdditionalProperties
                                : null
                        }
                ],
                AdditionalProperties = template.AdditionalProperties
            };
        }

        private SettingsDocument WithProfiles(List<ProfileSettings> profiles) => new()
        {
            SchemaVersion = _settings.SchemaVersion,
            Application = _settings.Application,
            Plugins = _settings.Plugins,
            Selection = _settings.Selection,
            Profiles = profiles,
            ApplicationGroups = _settings.ApplicationGroups,
            Rules = _settings.Rules,
            AdditionalProperties = _settings.AdditionalProperties
        };

        private SettingsDocument WithApplication(ApplicationSettings application) => new()
        {
            SchemaVersion = _settings.SchemaVersion,
            Application = application,
            Plugins = _settings.Plugins,
            Selection = _settings.Selection,
            Profiles = _settings.Profiles,
            ApplicationGroups = _settings.ApplicationGroups,
            Rules = _settings.Rules,
            AdditionalProperties = _settings.AdditionalProperties
        };

        private SettingsDocument WithMappings(
            List<ApplicationGroupSettings> applicationGroups,
            List<RuleSettings> rules) => new()
        {
            SchemaVersion = _settings.SchemaVersion,
            Application = _settings.Application,
            Plugins = _settings.Plugins,
            Selection = _settings.Selection,
            Profiles = _settings.Profiles,
            ApplicationGroups = applicationGroups,
            Rules = rules,
            AdditionalProperties = _settings.AdditionalProperties
        };

        private List<ApplicationGroupSettings> CreateApplicationGroupSettings() =>
            ApplicationGroups.Select(group => CreateApplicationGroupSettings(
                group.Id, group.DisplayName, group.Executables)).ToList();

        private static ApplicationGroupSettings CreateApplicationGroupSettings(
            string id,
            string displayName,
            IEnumerable<string> executables) => new()
            {
                Id = id,
                DisplayName = displayName,
                Executables = executables.ToList()
            };

        private void LoadApplicationSettings()
        {
            _loadingApplicationSettings = true;
            var application = _settings.Application!;
            CloseButtonBehavior = CanMinimizeToTray
                && application.CloseButtonBehavior == "minimize-to-tray"
                ? "Minimize to system tray"
                : "Exit application";
            StartMinimized = application.StartMinimized!.Value;
            ApplicationTheme = char.ToUpperInvariant(application.Theme![0]) + application.Theme[1..];
            VerboseLogging = application.VerboseLogging!.Value;
            _loadingApplicationSettings = false;
            IsApplicationSettingsDirty = false;
        }

        private bool SetApplicationSettingsDraft<T>(
            ref T field,
            T value,
            [CallerMemberName] string? propertyName = null)
        {
            if (!Set(ref field, value, propertyName)) return false;
            if (_loadingApplicationSettings) return true;
            IsApplicationSettingsDirty = true;
            ApplicationSettingsStatus = "Application preference changes are waiting for Save.";
            return true;
        }

        private async Task<string?> SaveCandidateAsync(
            SettingsDocument candidate,
            CancellationToken cancellationToken)
        {
            var validationError = ValidateCandidate(candidate);
            if (validationError is not null) return validationError;
            _settingsSaveWarning = null;
            if (_settingsStore is null) return null;
            var result = await _settingsStore.SaveSettingsAsync(candidate, cancellationToken);
            if (result.Warning is not null)
                _settingsSaveWarning = $"{result.Warning.FileName}: {result.Warning.Message}";
            return result.IsSuccess ? null : $"{result.Error!.FileName}: {result.Error.Message}";
        }

        private string SavedStatus(string message) => _settingsSaveWarning is null
            ? message
            : $"{message} Warning: {_settingsSaveWarning}";

        public async Task ReapplyOutputAsync(CancellationToken cancellationToken = default)
        {
            if (_selectionCommands is null || _selectionState is null) return;
            var result = await _selectionCommands.ReapplyAsync(
                _settings, _selectionState, _selectionReason, cancellationToken);
            ApplyRuntimeResult(result);
        }

        private static string? ValidateCandidate(SettingsDocument candidate)
        {
            var errors = SettingsValidator.Validate(candidate);
            return errors.Count == 0
                ? null
                : string.Join("; ", errors.Select(error => $"{error.Path}: {error.Message}"));
        }

        private bool BeginSettingsSave()
        {
            if (Interlocked.CompareExchange(ref _settingsSavePending, 1, 0) != 0) return false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSettingsSavePending)));
            return true;
        }

        private void EndSettingsSave()
        {
            Interlocked.Exchange(ref _settingsSavePending, 0);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSettingsSavePending)));
        }

        private async Task<string?> SaveRemovalStateAsync(
            string removedProfileId,
            SettingsDocument candidate,
            CancellationToken cancellationToken)
        {
            if (_settingsStore is null || _selectionState is null) return null;
            if (_selectionState.ManualProfile != removedProfileId
                && _selectionState.LastSelectedProfile != removedProfileId)
                return null;

            var replacementId = candidate.Selection!.DefaultProfile!;
            var replacement = new SelectionStateDocument
            {
                SchemaVersion = _selectionState.SchemaVersion,
                Mode = _selectionState.Mode,
                ManualProfile = _selectionState.ManualProfile == removedProfileId
                    ? replacementId
                    : _selectionState.ManualProfile,
                LastSelectedProfile = _selectionState.LastSelectedProfile == removedProfileId
                    ? replacementId
                    : _selectionState.LastSelectedProfile,
                UpdatedAt = DateTimeOffset.UtcNow,
                AdditionalProperties = _selectionState.AdditionalProperties
            };
            var result = await _settingsStore.SaveStateAsync(replacement, candidate, cancellationToken);
            if (!result.IsSuccess) return $"{result.Error!.FileName}: {result.Error.Message}";
            _selectionState = replacement;
            return null;
        }

        private bool SetProfileDraft<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (!Set(ref field, value, propertyName)) return false;
            if (_loadingProfileDraft) return true;
            IsProfileDirty = !ProfileDraftMatchesSaved();
            if (ProfileLivePreview)
            {
                ProfilePreviewRevision++;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProfilePreviewRevision)));
                ProfileEditorStatus = "Draft preview updated in the editor; output is sent after Save.";
            }
            else
            {
                ProfileEditorStatus = IsProfileDirty
                    ? "Profile changes are waiting for Save."
                    : "Profile draft matches the saved values.";
            }
            return true;
        }

        private bool ProfileDraftMatchesSaved()
        {
            if (EditingProfile is null) return true;
            var saved = TemplateFor(EditingProfile.Id);
            var fallback = saved.Fallback!.FirstOrDefault();
            var savedLayout = saved.Bindings![0].Layout?.DisplayName ?? "Current provider layout";
            return ProfileName == EditingProfile.DisplayName
                && ProfileEffect == saved.Bindings[0].Effect!.DisplayName
                && ProfileLayout == savedLayout
                && ProfileFallbackKind == (fallback?.Kind ?? "native-setup")
                && (ProfileFallbackKind != "profile" || ProfileFallbackProfile?.Id == fallback?.Profile);
        }

        private static string RequiredName(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

        private static string UniqueId(string name, IEnumerable<string> existingIds)
        {
            var stem = new string(name.ToLowerInvariant().Select(character =>
                char.IsAsciiLetterOrDigit(character) ? character : '-').ToArray()).Trim('-');
            if (string.IsNullOrEmpty(stem)) stem = "item";
            stem = stem.Length > 56 ? stem[..56].TrimEnd('-') : stem;
            var existing = existingIds.ToHashSet(StringComparer.Ordinal);
            var id = stem;
            var suffix = 2;
            while (existing.Contains(id)) id = $"{stem}-{suffix++}";
            return id;
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
