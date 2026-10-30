using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FocusRgb.App.Diagnostics;
using FocusRgb.App.Plugins;
using FocusRgb.Core.Commands;
using FocusRgb.Core.Configuration;
using FocusRgb.Core.Coordination;
using FocusRgb.Core.Persistence;
using FocusRgb.Core.Protocol;
using FocusRgb.Core.Selection;

namespace FocusRgb.App.ViewModels
{
    /// <summary>Connects the desktop shell to persisted local selection and profile state.</summary>
    public sealed class MainWindowViewModel : INotifyPropertyChanged
    {
        private const string SettingsConflictMessage =
            "Settings changed elsewhere. Reload the saved settings before saving this draft.";
        private bool _isManual = true;
        private readonly ApplicationStateService? _applicationState;
        private readonly IPluginOutputGateway? _pluginGateway;
        private SettingsDocument _settings;
        private GameCatalogDocument _gameCatalog;
        private SelectionStateDocument? _selectionState;
        private SelectionReason _selectionReason = FocusRgb.Core.Selection.SelectionReason.Manual();
        private SelectionRuntimeStatus? _runtimeStatus;
        private long _settingsRevision;
        private bool _hasSettingsConflict;
        private string _runtimeStatusMessage = "Preview state; runtime persistence is not connected.";
        private bool _isRuntimeCommandPending;
        private ProfileOptionViewModel _selectedProfile;
        private ProfileOptionViewModel? _editingProfile;
        private ApplicationGroupViewModel? _editingApplicationGroup;
        private GameCatalogItemViewModel? _editingGame;
        private string _profileName = "";
        private string _profileEffect = "";
        private string _profileLayout = "";
        private ProviderResourceOptionViewModel? _selectedProfileEffect;
        private ProviderResourceOptionViewModel? _selectedProfileLayout;
        private string _profileResourceStatus = "Provider resources have not been loaded.";
        private int _profileResourceRevision;
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
        private bool _debuggingMode;
        private bool _showProfileChangePopup = true;
        private string _notificationSize = "Medium";
        private string _notificationBackground = "Dark";
        private int _notificationDurationSeconds = 3;
        private bool _includeBetaPlugins;
        private string _switchingSpeed = "Normal (750 ms)";
        private bool _isApplicationSettingsDirty;
        private int _settingsSavePending;
        private string? _settingsSaveWarning;
        private DateTimeOffset _observationRetryAfter;
        private bool _loadingApplicationSettings;
        private string _applicationSettingsStatus = "Application preferences match the saved values.";
        private string _pluginsStatus = "Open this page to load official plugins.";
        private string _gameName = "";
        private string _manualGameExecutable = "";
        private bool _gameExecutableFromRunningApplications;
        private string? _selectedGameExecutable;
        private string _gameEditorStatus = "Manual and discovered games are stored locally.";
        private string _customPluginGitHubUrl = "";
        private int _pluginActionPending;
        private IReadOnlyList<OfficialPluginListing> _officialPluginCatalog = [];
        private IReadOnlyList<InstalledPluginInfo> _installedPluginInventory = [];
        private Func<OfficialPluginListing, bool> _isPluginCached = _ => false;

        private MainWindowViewModel(
            ObservableCollection<ProfileOptionViewModel> profiles,
            ProfileOptionViewModel selectedProfile,
            MappingEditorViewModel mappingEditor,
            SettingsDocument settings,
            GameCatalogDocument gameCatalog,
            SelectionStateDocument? selectionState,
            ApplicationStateService? applicationState,
            IPluginOutputGateway? pluginGateway,
            SelectionReason? selectionReason,
            SelectionRuntimeStatus? runtimeStatus,
            bool canMinimizeToTray)
        {
            Profiles = profiles;
            _selectedProfile = selectedProfile;
            MappingEditor = mappingEditor;
            _settings = settings;
            _gameCatalog = gameCatalog;
            _selectionState = selectionState;
            _applicationState = applicationState;
            _pluginGateway = pluginGateway;
            _runtimeStatus = runtimeStatus;
            _settingsRevision = applicationState?.Current.SettingsRevision ?? 0;
            CanMinimizeToTray = canMinimizeToTray;
            if (selectionReason is not null) _selectionReason = selectionReason;
            if (applicationState is not null)
                _runtimeStatusMessage = runtimeStatus?.AppliedDetail ?? "Local runtime is ready; no output integration is connected.";
            EditingProfile = profiles.Count > 0 ? profiles[0] : null;
            EditingApplicationGroup = ApplicationGroups.Count > 0 ? ApplicationGroups[0] : null;
            ReloadGameProjection();
            LoadApplicationSettings();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<ProfileOptionViewModel> Profiles { get; }
        public MappingEditorViewModel MappingEditor { get; private set; }
        public ObservableCollection<ApplicationGroupViewModel> ApplicationGroups => MappingEditor.ApplicationGroups;
        public ObservableCollection<GameCatalogItemViewModel> Games { get; } = [];
        public ObservableCollection<string> DraftExecutables { get; } = [];
        public ObservableCollection<string> DraftGameExecutables { get; } = [];
        public ObservableCollection<GameExecutableOptionViewModel> GameExecutableOptions { get; } = [];
        public bool HasDiscoveredGame => EditingGame is { IsManual: false };
        public ObservableCollection<string> DetectedExecutables { get; } = [];
        public ObservableCollection<string> IntegrationStatuses { get; } = ["No plugins configured."];
        public ObservableCollection<OfficialPluginItemViewModel> OfficialPlugins { get; } = [];
        public bool HasNoOfficialPlugins => OfficialPlugins.Count == 0;
        public string OfficialPluginsEmptyMessage => _officialPluginCatalog.Count > 0 && HasNoOfficialPlugins
            ? "All official plugins are installed." : PluginsStatus;
        public ObservableCollection<InstalledPluginItemViewModel> InstalledPlugins { get; } = [];
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
                LoadSavedProviderResources(value);
                _loadingProfileDraft = false;
                IsProfileDirty = false;
                if (discarded) ProfileEditorStatus = "Unsaved profile changes were discarded when the selection changed.";
                if (_pluginGateway is not null) _ = RefreshProfileResourcesAsync();
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

        public GameCatalogItemViewModel? EditingGame
        {
            get => _editingGame;
            set
            {
                if (!Set(ref _editingGame, value)) return;
                GameName = value?.DisplayName ?? "";
                DraftGameExecutables.Clear();
                if (value is not null)
                    foreach (var executable in value.Executables) DraftGameExecutables.Add(executable);
                SelectedGameExecutable = null;
                GameExecutableOptions.Clear();
                if (value is { IsManual: false })
                {
                    foreach (var name in value.Executables.Union(value.MatchingExecutables, StringComparer.OrdinalIgnoreCase))
                        GameExecutableOptions.Add(new GameExecutableOptionViewModel(name,
                            value.MatchingExecutables.Contains(name, StringComparer.OrdinalIgnoreCase)));
                }
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDiscoveredGame)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanEditGame)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRemoveGame)));
            }
        }

        public string ProfileName { get => _profileName; set => SetProfileDraft(ref _profileName, value); }
        public string ProfileEffect { get => _profileEffect; set => SetProfileDraft(ref _profileEffect, value); }
        public string ProfileLayout { get => _profileLayout; set => SetProfileDraft(ref _profileLayout, value); }
        public ObservableCollection<ProviderResourceOptionViewModel> ProfileEffectOptions { get; } = [];
        public ObservableCollection<ProviderResourceOptionViewModel> ProfileLayoutOptions { get; } = [];
        public ProviderResourceOptionViewModel? SelectedProfileEffect
        {
            get => _selectedProfileEffect;
            set
            {
                if (!Set(ref _selectedProfileEffect, value)) return;
                var textChanged = value is not null && ProfileEffect != value.DisplayName;
                if (value is not null) ProfileEffect = value.DisplayName;
                if (!_loadingProfileDraft && !textChanged) UpdateProfileDraftState();
            }
        }
        public ProviderResourceOptionViewModel? SelectedProfileLayout
        {
            get => _selectedProfileLayout;
            set
            {
                if (!Set(ref _selectedProfileLayout, value)) return;
                var textChanged = value is not null && ProfileLayout != value.DisplayName;
                if (value is not null) ProfileLayout = value.DisplayName;
                if (!_loadingProfileDraft && !textChanged) UpdateProfileDraftState();
            }
        }
        public string ProfileResourceStatus
        {
            get => _profileResourceStatus;
            private set => Set(ref _profileResourceStatus, value);
        }
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
        public string GameName { get => _gameName; set => Set(ref _gameName, value); }
        public string ManualGameExecutable { get => _manualGameExecutable; set => Set(ref _manualGameExecutable, value); }
        public string? SelectedGameExecutable { get => _selectedGameExecutable; set => Set(ref _selectedGameExecutable, value); }
        public string GamePickerPlaceholder => Games.Count == 0 ? "No games have been added or discovered" : "Choose a game";
        public string GameEditorStatus
        {
            get => _gameEditorStatus;
            private set
            {
                if (!Set(ref _gameEditorStatus, value))
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GameEditorStatus)));
            }
        }
        public bool CanEditGame => EditingGame is null || EditingGame.IsManual;
        public bool GameExecutableFromRunningApplications
        {
            get => _gameExecutableFromRunningApplications;
            set
            {
                if (!Set(ref _gameExecutableFromRunningApplications, value)) return;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GameExecutableFromFilePath)));
            }
        }
        public bool GameExecutableFromFilePath => !GameExecutableFromRunningApplications;
        public bool CanRemoveGame => EditingGame?.IsManual == true;
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
        public bool HasSettingsConflict
        {
            get => _hasSettingsConflict;
            private set => Set(ref _hasSettingsConflict, value);
        }
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
            set
            {
                if (SetApplicationSettingsDraft(ref _verboseLogging, value)) RaiseDiagnosticLoggingChanged();
            }
        }
        public bool DebuggingMode
        {
            get => _debuggingMode;
            set
            {
                if (SetApplicationSettingsDraft(ref _debuggingMode, value)) RaiseDiagnosticLoggingChanged();
            }
        }
        // The logging choice is kept while Debugging mode is off; it only shows and applies while the mode is on.
        public bool DiagnosticLogging
        {
            get => DebuggingMode && VerboseLogging;
            set => VerboseLogging = value;
        }
        private void RaiseDiagnosticLoggingChanged() =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DiagnosticLogging)));
        public bool ShowProfileChangePopup
        {
            get => _showProfileChangePopup;
            set => SetApplicationSettingsDraft(ref _showProfileChangePopup, value);
        }
        public IReadOnlyList<string> NotificationSizes { get; } = ["Small", "Medium", "Large"];
        public IReadOnlyList<string> NotificationBackgrounds { get; } = ["Dark", "Light", "Blue"];
        public IReadOnlyList<int> NotificationDurations { get; } = [2, 3, 5];
        public string NotificationSize { get => _notificationSize; set => SetApplicationSettingsDraft(ref _notificationSize, value); }
        public string NotificationBackground { get => _notificationBackground; set => SetApplicationSettingsDraft(ref _notificationBackground, value); }
        public int NotificationDurationSeconds { get => _notificationDurationSeconds; set => SetApplicationSettingsDraft(ref _notificationDurationSeconds, value); }
        public bool IncludeBetaPlugins
        {
            get => _includeBetaPlugins;
            set => SetApplicationSettingsDraft(ref _includeBetaPlugins, value);
        }
        public bool SavedIncludeBetaPlugins =>
            (_applicationState?.Current.Settings ?? _settings).Application?.IncludeBetaPlugins ?? ApplicationVersion.IsPrerelease;
        public IReadOnlyList<string> SwitchingSpeeds { get; } = ["Fast (300 ms)", "Normal (750 ms)", "Smooth (2000 ms)"];
        public string SwitchingSpeed
        {
            get => _switchingSpeed;
            set => SetApplicationSettingsDraft(ref _switchingSpeed, value);
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
        public string PluginsStatus
        {
            get => _pluginsStatus;
            private set
            {
                Set(ref _pluginsStatus, value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OfficialPluginsEmptyMessage)));
            }
        }
        public string CustomPluginGitHubUrl { get => _customPluginGitHubUrl; set => Set(ref _customPluginGitHubUrl, value); }
        public bool IsPluginActionPending => Volatile.Read(ref _pluginActionPending) != 0;
        public IReadOnlyList<string> EnabledPluginIds => _settings.Plugins!.Enabled!.ToArray();
        public bool ShouldMinimizeOnClose => CanMinimizeToTray
            && _settings.Application!.CloseButtonBehavior == "minimize-to-tray";
        public string SavedApplicationTheme => _settings.Application!.Theme!;
        public string SavedCloseButtonBehavior => _settings.Application!.CloseButtonBehavior!;
        public bool SavedStartMinimized => _settings.Application!.StartMinimized!.Value;
        public bool SavedVerboseLogging => _settings.Application!.VerboseLogging!.Value;
        public bool SavedDebuggingMode => _settings.Application!.DebuggingMode == true;
        public TimeSpan ApplicationObservationInterval => IsAutomatic
            ? TimeSpan.FromMilliseconds(100)
            : TimeSpan.FromSeconds(5);

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
                if (_applicationState is null) return $"{SelectedProfile.DisplayName} (preview)";
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
        public string SelectionReason => _applicationState is null
            ? IsManual
                ? "Manual preview selection; no command was sent."
                : "Automatic preview mode; waiting for a stable application observation."
            : DescribeReason(_selectionReason);

        public async Task SelectProfileAsync(
            ProfileOptionViewModel profile,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(profile);
            if (_applicationState is null || _selectionState is null)
            {
                SelectedProfile = profile;
                return;
            }

            await RunRuntimeCommandAsync(
                () => _applicationState.SelectProfileAsync(
                    profile.Id,
                    DateTimeOffset.UtcNow,
                    cancellationToken: cancellationToken),
                $"Selected {profile.DisplayName} in manual mode.");
        }

        public async Task SetModeAsync(bool manual, CancellationToken cancellationToken = default)
        {
            if (_applicationState is null || _selectionState is null)
            {
                IsManual = manual;
                return;
            }

            await RunRuntimeCommandAsync(
                () => _applicationState.SetModeAsync(
                    manual ? "manual" : "automatic",
                    DateTimeOffset.UtcNow,
                    cancellationToken: cancellationToken),
                manual
                    ? "Manual mode saved."
                    : "Automatic mode saved; waiting for an application match.");
        }

        public async Task<bool> SaveRuntimeStateAsync(CancellationToken cancellationToken = default)
        {
            if (_applicationState is null || _selectionState is null) return true;
            var result = await _applicationState.SaveCurrentAsync(
                DateTimeOffset.UtcNow,
                source: "shutdown",
                cancellationToken: cancellationToken);
            if (!result.IsSuccess)
            {
                RuntimeStatusMessage = $"Could not save before quitting: {result.ErrorMessage}";
                return false;
            }

            ApplyApplicationSnapshot(result.Snapshot);
            RuntimeStatusMessage = "Selection state saved. FocusRGB can quit safely.";
            return true;
        }

        public async Task RespondToRecoveryAsync(
            bool accept,
            CancellationToken cancellationToken = default)
        {
            var status = _runtimeStatus;
            if (_applicationState is null || _selectionState is null
                || status is null || status.Notifications.Count == 0)
            {
                return;
            }
            var notification = status.Notifications[0];

            await RunRuntimeCommandAsync(
                () => _applicationState.RespondToNotificationAsync(
                    notification.Id,
                    accept ? "accept" : "decline",
                    cancellationToken: cancellationToken),
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
                    DebuggingMode = DebuggingMode,
                    ShowProfileChangePopup = ShowProfileChangePopup,
                    Notification = new NotificationSettings
                    {
                        Size = NotificationSize.ToLowerInvariant(), Background = NotificationBackground.ToLowerInvariant(),
                        DurationSeconds = NotificationDurationSeconds,
                        AdditionalProperties = _settings.Application!.Notification?.AdditionalProperties
                    },
                    IncludeBetaPlugins = IncludeBetaPlugins,
                    SwitchingSpeed = SwitchingSpeed.Split(' ')[0].ToLowerInvariant(),
                    AdditionalProperties = _settings.Application!.AdditionalProperties
                };
                var candidate = WithApplication(application);
                var error = await SaveCandidateAsync(candidate, cancellationToken);
                if (error is not null)
                {
                    ApplicationSettingsStatus = error;
                    return false;
                }

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

        internal bool HasUnsavedChanges(EditorDrafts editors) =>
            editors.HasFlag(EditorDrafts.Settings) && IsApplicationSettingsDirty
            || editors.HasFlag(EditorDrafts.FocusLibrary) && MappingEditor.IsDirty
            || editors.HasFlag(EditorDrafts.Profiles) && IsProfileDirty;

        internal async Task<bool> SaveUnsavedChangesAsync(EditorDrafts editors)
        {
            if (IsSettingsSavePending || HasSettingsConflict) return false;
            if (editors.HasFlag(EditorDrafts.Settings) && IsApplicationSettingsDirty
                && !await SaveApplicationSettingsAsync()) return false;
            if (editors.HasFlag(EditorDrafts.Profiles) && IsProfileDirty)
            {
                await SaveProfileAsync();
                if (IsProfileDirty) return false;
            }
            if (editors.HasFlag(EditorDrafts.FocusLibrary) && MappingEditor.IsDirty)
            {
                if (MappingEditor.HasEditor) await SaveEditingFocusAsync(false);
                else await SaveMappingsAsync();
            }
            return !HasUnsavedChanges(editors);
        }

        internal void DiscardUnsavedChanges(EditorDrafts editors)
        {
            if (editors.HasFlag(EditorDrafts.Settings)) CancelApplicationSettings();
            if (editors.HasFlag(EditorDrafts.FocusLibrary)) MappingEditor.Cancel();
            if (editors.HasFlag(EditorDrafts.Profiles)) CancelProfile();
        }

        public void ReportSettingsAction(string message) => ApplicationSettingsStatus = message;

        public void ReportUnexpectedError(string message) => RuntimeStatusMessage = message;

        public void SetIntegrationStatuses(IEnumerable<string> statuses)
        {
            IntegrationStatuses.Clear();
            foreach (var status in statuses) IntegrationStatuses.Add(status);
            if (IntegrationStatuses.Count == 0) IntegrationStatuses.Add("No plugins configured.");
        }

        public void SetOfficialPlugins(
            OfficialPluginCatalogResult catalog,
            Func<OfficialPluginListing, bool>? isCached = null)
        {
            _officialPluginCatalog = catalog.Items;
            _isPluginCached = isCached ?? (_ => false);
            RefreshPluginLists();
            PluginsStatus = catalog.Status;
        }

        public void SetInstalledPlugins(IEnumerable<InstalledPluginInfo> plugins)
        {
            _installedPluginInventory = plugins.ToArray();
            RefreshPluginLists();
        }

        public void RefreshPluginLists()
        {
            OfficialPlugins.Clear();
            foreach (var item in _officialPluginCatalog)
            {
                if (!_installedPluginInventory.Any(plugin => plugin.Id == item.Id))
                {
                    OfficialPlugins.Add(new OfficialPluginItemViewModel(item, _isPluginCached(item)));
                }
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoOfficialPlugins)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OfficialPluginsEmptyMessage)));
            InstalledPlugins.Clear();
            foreach (var plugin in _installedPluginInventory)
                InstalledPlugins.Add(new InstalledPluginItemViewModel(plugin,
                    _officialPluginCatalog.FirstOrDefault(item => item.Id == plugin.Id)));
        }

        public void ReportPluginAction(string message) => PluginsStatus = message;

        public bool BeginPluginAction()
        {
            if (Interlocked.CompareExchange(ref _pluginActionPending, 1, 0) != 0) return false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPluginActionPending)));
            return true;
        }

        public void EndPluginAction()
        {
            Interlocked.Exchange(ref _pluginActionPending, 0);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPluginActionPending)));
        }

        public async Task<string?> SetPluginEnabledAsync(
            string pluginId,
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            if (!BeginSettingsSave())
                return HasSettingsConflict ? SettingsConflictMessage : "Another settings save is already in progress.";
            try
            {
                var enabledIds = _settings.Plugins!.Enabled!.ToHashSet(StringComparer.Ordinal);
                if (enabled) enabledIds.Add(pluginId); else enabledIds.Remove(pluginId);
                var candidate = WithPlugins(new PluginSettings
                {
                    Enabled = enabledIds.Order(StringComparer.Ordinal).ToList(),
                    AdditionalProperties = _settings.Plugins.AdditionalProperties
                });
                var error = await SaveCandidateAsync(candidate, cancellationToken);
                return error;
            }
            finally
            {
                EndSettingsSave();
            }
        }

        public async Task RefreshProfileResourcesAsync(CancellationToken cancellationToken = default)
        {
            var revision = Interlocked.Increment(ref _profileResourceRevision);
            var profile = EditingProfile;
            if (_pluginGateway is null || profile is null) return;
            var binding = _settings.Profiles!.SingleOrDefault(item => item.Id == profile.Id)?.Bindings?[0];
            if (binding is null) return;
            var effectDraft = DraftResource(SelectedProfileEffect, ProfileEffect);
            var layoutDraft = string.Equals(ProfileLayout, "Current provider layout", StringComparison.Ordinal)
                ? null
                : DraftResource(SelectedProfileLayout, ProfileLayout);
            try
            {
                var capabilities = _pluginGateway.GetCapabilities(binding.Plugin!);
                if (capabilities is null)
                {
                    if (revision == _profileResourceRevision && ReferenceEquals(profile, EditingProfile))
                    {
                        ReplaceResourceOptions(ProfileEffectOptions, [], effectDraft, retainCurrent: false);
                        ReplaceResourceOptions(ProfileLayoutOptions, [], layoutDraft, retainCurrent: true);
                        SelectProviderResources(effectDraft, layoutDraft);
                        ProfileResourceStatus = $"Plugin '{binding.Plugin}' is unavailable; current choices are shown as unavailable.";
                    }
                    return;
                }

                var effect = await LoadCatalogAsync(binding.Plugin!, "effect", capabilities, cancellationToken);
                var layout = await LoadCatalogAsync(binding.Plugin!, "layout", capabilities, cancellationToken);
                if (revision != _profileResourceRevision || !ReferenceEquals(profile, EditingProfile)) return;
                effectDraft = DraftResource(SelectedProfileEffect, ProfileEffect);
                layoutDraft = string.Equals(ProfileLayout, "Current provider layout", StringComparison.Ordinal)
                    ? null
                    : DraftResource(SelectedProfileLayout, ProfileLayout);

                ReplaceResourceOptions(ProfileEffectOptions, effect.Items, effectDraft, retainCurrent: false);
                ReplaceResourceOptions(ProfileLayoutOptions, layout.Items, layoutDraft, retainCurrent: true);
                SelectProviderResources(effectDraft, layoutDraft);
                var resolvedByName = binding.Effect?.ResourceId is null
                    && SelectedProfileEffect?.ResourceId is not null
                    && SelectedProfileEffect.DisplayName == binding.Effect?.DisplayName;
                ProfileResourceStatus = $"Effects: {effect.Status} Layouts: {layout.Status}"
                    + (resolvedByName ? " Resolved by name; save to pin the provider ID." : "");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                AppLog.Write($"Provider catalog refresh failed: {exception.Message}");
                if (revision == _profileResourceRevision && ReferenceEquals(profile, EditingProfile))
                    ProfileResourceStatus = $"Provider catalogs could not be refreshed: {exception.Message}";
            }
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
                    ProfileEditorStatus = "rules: reassign or remove Focuses that use this profile first.";
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
                var error = await SaveCandidateAsync(candidate, cancellationToken);
                if (error is not null)
                {
                    ProfileEditorStatus = error;
                    return;
                }

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
            foreach (var executable in DetectedExecutables.Where(existing =>
                !detected.Contains(existing, StringComparer.OrdinalIgnoreCase)).ToArray())
                DetectedExecutables.Remove(executable);
            foreach (var executable in detected.Where(candidate =>
                !DetectedExecutables.Contains(candidate, StringComparer.OrdinalIgnoreCase)))
                DetectedExecutables.Add(executable);
            for (var index = 0; index < detected.Length; index++)
            {
                var currentIndex = DetectedExecutables.IndexOf(detected[index]);
                if (currentIndex >= 0 && currentIndex != index) DetectedExecutables.Move(currentIndex, index);
            }
            if (SelectedDetectedExecutable is not null
                && !DetectedExecutables.Contains(SelectedDetectedExecutable, StringComparer.OrdinalIgnoreCase))
                SelectedDetectedExecutable = null;

            if (_applicationState is null || _selectionState is null || _selectionState.Mode != "automatic") return;
            var now = DateTimeOffset.UtcNow;
            if (now < _observationRetryAfter) return;
            var previousState = _selectionState;
            var previousReason = _selectionReason;
            var result = await _applicationState.ObserveAsync(observation, now, cancellationToken);
            if (!result.IsSuccess)
            {
                RuntimeStatusMessage = result.ErrorMessage!;
                _observationRetryAfter = now.AddSeconds(10);
                return;
            }
            var changed = previousState.UpdatedAt != result.Snapshot.SelectionState.UpdatedAt
                || previousReason.Kind != result.Snapshot.SelectionReason.Kind
                || previousReason.RuleId != result.Snapshot.SelectionReason.RuleId;
            if (!changed) return;
            _observationRetryAfter = default;
            ApplyApplicationSnapshot(result.Snapshot);
            RuntimeStatusMessage = result.Snapshot.RuntimeStatus!.AppliedDetail;
        }

        public void RemoveDraftExecutable()
        {
            if (SelectedDraftExecutable is null) return;
            DraftExecutables.Remove(SelectedDraftExecutable);
            SelectedDraftExecutable = null;
        }

        public void StartNewManualGame()
        {
            EditingGame = null;
            GameEditorStatus = "Enter a name and at least one Windows executable, then save.";
        }

        public bool AddDraftGameExecutable(string path)
        {
            if (!CanEditGame) return false;
            var executable = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(executable)
                || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                GameEditorStatus = "Enter a Windows executable name ending in .exe.";
                return false;
            }
            if (!DraftGameExecutables.Contains(executable, StringComparer.OrdinalIgnoreCase))
                DraftGameExecutables.Add(executable);
            return true;
        }

        public void AddManualGameExecutable()
        {
            if (AddDraftGameExecutable(ManualGameExecutable)) ManualGameExecutable = "";
        }

        public void AddDetectedGameExecutable()
        {
            if (SelectedDetectedExecutable is not null)
                AddDraftGameExecutable(SelectedDetectedExecutable);
        }

        public void RemoveDraftGameExecutable()
        {
            if (!CanEditGame || SelectedGameExecutable is null) return;
            DraftGameExecutables.Remove(SelectedGameExecutable);
            SelectedGameExecutable = null;
        }

        public async Task SaveManualGameAsync(CancellationToken cancellationToken = default)
        {
            if (!CanEditGame || !BeginSettingsSave()) return;
            try
            {
                var name = RequiredName(GameName, "Manual game");
                var id = EditingGame?.Id ?? UniqueId(
                    name,
                    _gameCatalog.Games!
                        .Where(game => game.Launcher == GameCatalogJson.ManualLauncher)
                        .Select(game => game.Id!));
                var existing = _gameCatalog.Games!.FirstOrDefault(game =>
                    game.Launcher == GameCatalogJson.ManualLauncher && game.Id == id);
                var saved = new GameCatalogEntry
                {
                    Launcher = GameCatalogJson.ManualLauncher,
                    Id = id,
                    DisplayName = name,
                    Executables = [.. DraftGameExecutables],
                    Available = true,
                    AdditionalProperties = existing?.AdditionalProperties
                };
                var candidate = new GameCatalogDocument
                {
                    SchemaVersion = _gameCatalog.SchemaVersion,
                    Games = existing is null
                        ? [.. _gameCatalog.Games!, saved]
                        : _gameCatalog.Games!.Select(game =>
                            game.Launcher == GameCatalogJson.ManualLauncher && game.Id == id ? saved : game).ToList(),
                    AdditionalProperties = _gameCatalog.AdditionalProperties
                };
                var error = await SaveGameCatalogCandidateAsync(candidate, cancellationToken);
                if (error is not null)
                {
                    GameEditorStatus = error;
                    return;
                }
                MappingEditor.UpsertGameTarget(saved);
                ReloadGameProjection();
                EditingGame = Games.Single(game => game.Launcher == GameCatalogJson.ManualLauncher && game.Id == id);
                GameEditorStatus = $"Saved {name}.";
            }
            finally
            {
                EndSettingsSave();
            }
        }

        public async Task SaveDiscoveredGameExecutablesAsync(CancellationToken cancellationToken = default)
        {
            if (EditingGame is not { IsManual: false } editing || !BeginSettingsSave()) return;
            try
            {
                var selected = GameExecutableOptions.Where(option => option.IsSelected).Select(option => option.Name).ToList();
                if (selected.Count == 0)
                {
                    GameEditorStatus = "Select at least one main executable to match.";
                    return;
                }
                var candidate = GameCatalogJson.Load(GameCatalogJson.Serialize(_gameCatalog)).Catalog!;
                var games = candidate.Games!;
                var existing = games.FirstOrDefault(game => game.Launcher == editing.Launcher && game.Id == editing.Id);
                if (existing is null)
                {
                    GameEditorStatus = "This game is no longer in the catalog. Refresh games and try again.";
                    return;
                }
                var saved = new GameCatalogEntry
                {
                    Launcher = existing.Launcher, Id = existing.Id, DisplayName = existing.DisplayName,
                    Executables = existing.Executables, SelectedExecutables = selected,
                    Available = existing.Available, UnavailableReason = existing.UnavailableReason,
                    LastRefreshedAt = existing.LastRefreshedAt, AdditionalProperties = existing.AdditionalProperties
                };
                games[games.IndexOf(existing)] = saved;
                var error = await SaveGameCatalogCandidateAsync(candidate, cancellationToken, source: "catalog");
                if (error is not null)
                {
                    GameEditorStatus = error;
                    return;
                }
                MappingEditor.UpsertGameTarget(saved);
                ReloadGameProjection();
                GameEditorStatus = $"Saved executable selection for {editing.DisplayName}. Focuses now match the selected executables.";
            }
            finally
            {
                EndSettingsSave();
            }
        }

        public async Task RemoveManualGameAsync(CancellationToken cancellationToken = default)
        {
            if (EditingGame?.IsManual != true || !BeginSettingsSave()) return;
            try
            {
                var launcher = EditingGame.Launcher;
                var id = EditingGame.Id;
                if (MappingEditor.AppliedMappings.Any(mapping =>
                        mapping.GameLauncher == launcher && mapping.GameId == id)
                    || MappingEditor.Mappings.Any(mapping =>
                        mapping.SelectedTarget.GameLauncher == launcher && mapping.SelectedTarget.GameId == id))
                {
                    GameEditorStatus = "Remove Focuses that use this game first.";
                    return;
                }
                var removed = EditingGame.DisplayName;
                var candidate = new GameCatalogDocument
                {
                    SchemaVersion = _gameCatalog.SchemaVersion,
                    Games = _gameCatalog.Games!.Where(game =>
                        game.Launcher != launcher || game.Id != id).ToList(),
                    AdditionalProperties = _gameCatalog.AdditionalProperties
                };
                var error = await SaveGameCatalogCandidateAsync(candidate, cancellationToken);
                if (error is not null)
                {
                    GameEditorStatus = error;
                    return;
                }
                MappingEditor.RemoveGameTarget(launcher, id);
                ReloadGameProjection();
                GameEditorStatus = $"Removed {removed}.";
            }
            finally
            {
                EndSettingsSave();
            }
        }

        public async Task RefreshGameCatalogAsync(
            string launcher,
            PluginGatewayResult<PluginCatalogResult> result,
            CancellationToken cancellationToken = default)
        {
            if (_applicationState is null || launcher == GameCatalogJson.ManualLauncher) return;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var current = _applicationState.Current;
                var launcherGames = current.GameCatalog.Games!
                    .Where(game => game.Launcher == launcher)
                    .ToList();
                if ((!result.IsSuccess || result.Value!.Support is not ("supported" or "unverified"))
                    && launcherGames.Count == 0)
                {
                    return;
                }
                var retained = current.GameCatalog.Games!
                    .Where(game => game.Launcher != launcher)
                    .ToList();
                if (result.IsSuccess && result.Value!.Support is "supported" or "unverified")
                {
                    var incomplete = result.Value.RefreshedAt is null
                        || result.Value.Reason?.Code == "catalog-scan-incomplete";
                    if (incomplete) retained.AddRange(launcherGames);
                    GameEditorStatus = result.Value.Reason?.Message
                        ?? (incomplete ? "Game discovery is incomplete; retaining cached games." : string.Empty);
                    var refreshedAt = result.Value.RefreshedAt ?? DateTimeOffset.UtcNow;
                    var seen = new HashSet<GameIdentity>();
                    var rejected = 0;
                    foreach (var item in result.Value.Items)
                    {
                        if (item?.Game?.Executables is null)
                        {
                            rejected++;
                            continue;
                        }
                        var existing = launcherGames.FirstOrDefault(game => game.Id == item.ResourceId);
                        var game = new GameCatalogEntry
                        {
                            Launcher = launcher,
                            Id = item.ResourceId,
                            DisplayName = item.DisplayName,
                            Executables = [.. item.Game!.Executables],
                            SelectedExecutables = existing?.SelectedExecutables,
                            Available = item.Available,
                            UnavailableReason = item.Reason?.Message,
                            LastRefreshedAt = refreshedAt,
                            AdditionalProperties = existing?.AdditionalProperties
                        };
                        if (GameCatalogJson.Validate(new GameCatalogDocument
                            {
                                SchemaVersion = GameCatalogJson.CurrentSchemaVersion,
                                Games = [game]
                            }).Count > 0 || !seen.Add(new GameIdentity(launcher, item.ResourceId)))
                        {
                            rejected++;
                            continue;
                        }
                        if (incomplete)
                            retained.RemoveAll(existing => existing.Launcher == launcher && existing.Id == game.Id);
                        retained.Add(game);
                    }
                    if (!incomplete)
                        retained.AddRange(launcherGames.Where(game => game.SelectedExecutables is not null
                            && !seen.Contains(new GameIdentity(launcher, game.Id!)))
                            .Select(game => UnavailableGame(game, "Not reported by the latest discovery scan.")));
                    if (rejected > 0)
                    {
                        GameEditorStatus = string.Join(" ", new[]
                        {
                            GameEditorStatus,
                            $"Skipped {rejected} invalid or duplicate game catalog entries from {launcher}."
                        }.Where(message => !string.IsNullOrEmpty(message)));
                    }
                }
                else
                {
                    var reason = result.IsSuccess
                        ? result.Value!.Reason?.Message ?? "Game discovery is unavailable."
                        : result.ErrorMessage!;
                    retained.AddRange(launcherGames
                        .Select(game => UnavailableGame(game, reason)));
                }
                var candidate = new GameCatalogDocument
                {
                    SchemaVersion = GameCatalogJson.CurrentSchemaVersion,
                    Games = retained,
                    AdditionalProperties = current.GameCatalog.AdditionalProperties
                };
                if (GameCatalogJson.HasSameContent(candidate, current.GameCatalog)) return;
                var saved = await _applicationState.ApplyGameCatalogAsync(
                    candidate, current.SettingsRevision, "catalog", cancellationToken);
                if (saved.IsSuccess) return;
                if (saved.ErrorCode != "conflict")
                {
                    GameEditorStatus = saved.ErrorMessage!;
                    return;
                }
            }
            GameEditorStatus = "Game catalog changed concurrently; discovery will retry later.";
        }

        private static GameCatalogEntry UnavailableGame(GameCatalogEntry game, string reason) => new()
        {
            Launcher = game.Launcher, Id = game.Id, DisplayName = game.DisplayName,
            Executables = game.Executables, SelectedExecutables = game.SelectedExecutables,
            Available = false, UnavailableReason = reason, LastRefreshedAt = game.LastRefreshedAt,
            AdditionalProperties = game.AdditionalProperties
        };

        public async Task SaveApplicationGroupAsync(CancellationToken cancellationToken = default)
        {
            if (EditingApplicationGroup is null) return;
            if (!BeginSettingsSave()) return;
            try
            {
                var name = RequiredName(ApplicationGroupName, "Application Group");
                var saved = _settings.ApplicationGroups!.Single(group => group.Id == EditingApplicationGroup.Id);
                var replacement = CreateApplicationGroupSettings(
                    EditingApplicationGroup.Id, name, DraftExecutables, saved.AdditionalProperties);
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
                ApplicationGroups.Add(group);
                MappingEditor.AddApplicationGroupTarget(group);
                EditingApplicationGroup = group;
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
                    || MappingEditor.Mappings.Any(mapping =>
                        mapping.SelectedTarget.ApplicationGroupId == EditingApplicationGroup.Id))
                {
                    ApplicationGroupEditorStatus = "Remove Focuses that use this application group first.";
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
                var removedId = EditingApplicationGroup.Id;
                ApplicationGroups.Remove(EditingApplicationGroup);
                MappingEditor.RemoveApplicationGroupTarget(removedId);
                EditingApplicationGroup = ApplicationGroups.Count > 0 ? ApplicationGroups[0] : null;
                ApplicationGroupEditorStatus = SavedStatus($"Removed {removed}.");
            }
            finally
            {
                EndSettingsSave();
            }
        }

        public Task SaveMappingsAsync(CancellationToken cancellationToken = default) =>
            SaveMappingsCoreAsync(false, false, cancellationToken);

        public async Task SaveMappingOrderAsync(CancellationToken cancellationToken = default)
        {
            if (!BeginSettingsSave()) return;
            try
            {
                var saved = _settings.Rules!.ToDictionary(rule => rule.Id!, StringComparer.Ordinal);
                if (MappingEditor.Mappings.Any(item => !saved.ContainsKey(item.Id)))
                {
                    MappingEditor.ReportSaveFailure("Save or cancel the new Focus before reordering.");
                    return;
                }
                // Save only list membership/order; keep the separate Focus editor draft untouched.
                var rules = MappingEditor.Mappings.Select(item => saved[item.Id]).ToList();
                var error = await SaveCandidateAsync(WithMappings(_settings.ApplicationGroups!, rules), cancellationToken);
                if (error is not null)
                {
                    MappingEditor.RestoreSavedOrder();
                    MappingEditor.ReportSaveFailure(error);
                    return;
                }
                MappingEditor.AcceptSavedOrder();
            }
            catch (OperationCanceledException)
            {
                MappingEditor.RestoreSavedOrder();
                throw;
            }
            finally { EndSettingsSave(); }
        }

        public Task SaveEditingFocusAsync(bool saveAs, CancellationToken cancellationToken = default) =>
            SaveMappingsCoreAsync(true, saveAs, cancellationToken);

        private async Task SaveMappingsCoreAsync(bool saveEditor, bool saveAs, CancellationToken cancellationToken)
        {
            if (!BeginSettingsSave()) return;
            try
            {
                if (saveEditor && !MappingEditor.CommitEditing(saveAs)) return;
                var candidate = WithMappings(CreateApplicationGroupSettings(), MappingEditor.CreateRuleSettings());
                var error = await SaveCandidateAsync(candidate, cancellationToken);
                if (error is not null)
                {
                    MappingEditor.ReportSaveFailure(error);
                    return;
                }

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

            return Create(
                result.Settings,
                GameCatalogJson.CreateDefault(),
                null, null, null, null, null,
                canMinimizeToTray: true);
        }

        public static MainWindowViewModel CreatePreview(SettingsDocument settings)
            => Create(
                settings,
                GameCatalogJson.CreateDefault(),
                null, null, null, null, null,
                canMinimizeToTray: true);

        public static MainWindowViewModel CreatePreview(
            SettingsDocument settings,
            GameCatalogDocument gameCatalog)
            => Create(settings, gameCatalog, null, null, null, null, null, canMinimizeToTray: true);

        public static MainWindowViewModel CreatePersistent(
            StartupSnapshot snapshot,
            LocalStateStore settingsStore,
            bool canMinimizeToTray = true,
            OutputCoordinator? outputCoordinator = null,
            SelectionRuntimeStatus? runtimeStatus = null,
            IPluginOutputGateway? pluginGateway = null,
            ApplicationStateService? applicationState = null,
            Action<Action>? dispatch = null)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(settingsStore);
            var stateService = applicationState ?? new ApplicationStateService(
                snapshot.Settings,
                snapshot.State,
                snapshot.Desired.Reason,
                settingsStore,
                outputCoordinator,
                runtimeStatus,
                snapshot.GameCatalog);
            var current = stateService.Current;
            var viewModel = Create(
                current.Settings,
                current.GameCatalog,
                current.SelectionState,
                stateService,
                pluginGateway,
                current.SelectionReason,
                current.RuntimeStatus,
                canMinimizeToTray);
            var notify = dispatch ?? (action => action());
            stateService.SnapshotChanged += (_, changed) => notify(() => viewModel.ApplyApplicationSnapshot(changed));
            viewModel.IsManual = current.SelectionState.Mode == "manual";
            var selectedId = viewModel.IsManual
                ? current.SelectionState.ManualProfile
                : current.SelectionState.LastSelectedProfile;
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
            GameCatalogDocument gameCatalog,
            SelectionStateDocument? selectionState,
            ApplicationStateService? applicationState,
            IPluginOutputGateway? pluginGateway,
            SelectionReason? selectionReason,
            SelectionRuntimeStatus? runtimeStatus,
            bool canMinimizeToTray)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(gameCatalog);
            if (SettingsValidator.Validate(settings).Count > 0
                || GameCatalogJson.Validate(gameCatalog).Count > 0
                || settings.Profiles is not { Count: > 0 } settingsProfiles)
            {
                throw new ArgumentException("Preview requires validated settings.", nameof(settings));
            }

            var profiles = new ObservableCollection<ProfileOptionViewModel>(CreateProfileOptions(settingsProfiles));
            var defaultId = settings.Selection!.DefaultProfile;
            var selectedProfile = profiles.Single(profile => profile.Id == defaultId);
            var mappingEditor = MappingEditorViewModel.Create(settings, profiles, gameCatalog);
            return new MainWindowViewModel(
                profiles,
                selectedProfile,
                mappingEditor,
                settings,
                gameCatalog,
                selectionState,
                applicationState,
                pluginGateway,
                selectionReason,
                runtimeStatus,
                canMinimizeToTray);
        }

        private async Task RunRuntimeCommandAsync(
            Func<Task<ApplicationCommandResult>> command,
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

                ApplyApplicationSnapshot(result.Snapshot);
                RuntimeStatusMessage = result.Snapshot.RuntimeStatus!.AppliedDetail;
            }
            finally
            {
                IsRuntimeCommandPending = false;
            }
        }

        public void ApplyApplicationSnapshot(ApplicationStateSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            var settingsChanged = snapshot.SettingsRevision > _settingsRevision;
            if (settingsChanged)
            {
                if (snapshot.Source == "catalog")
                {
                    _gameCatalog = snapshot.GameCatalog;
                    if (!HasSettingsConflict) _settingsRevision = snapshot.SettingsRevision;
                    MappingEditor.SynchronizeGameTargets(_gameCatalog);
                    ReloadGameProjection(preserveManualDraft: true);
                }
                else
                {
                    var hasDirtyEditor = HasDirtyEditor();
                    if (snapshot.Source != "ui" && (HasSettingsConflict || hasDirtyEditor))
                    {
                        HasSettingsConflict = true;
                        ReportSettingsConflict();
                    }
                    else
                    {
                        _settings = snapshot.Settings;
                        _gameCatalog = snapshot.GameCatalog;
                        _settingsRevision = snapshot.SettingsRevision;
                        if (snapshot.Source != "ui") ReloadSettingsProjection();
                    }
                }
            }

            _selectionState = snapshot.SelectionState;
            if (snapshot.RuntimeStatus is null
                || _runtimeStatus is not null && snapshot.RuntimeStatus.Sequence < _runtimeStatus.Sequence)
            {
                return;
            }
            _selectionReason = snapshot.SelectionReason;
            _runtimeStatus = snapshot.RuntimeStatus;
            _isManual = snapshot.RuntimeStatus.Mode == "manual";
            var selected = Profiles.FirstOrDefault(profile => profile.Id == snapshot.RuntimeStatus.Desired.ProfileId);
            if (selected is not null) _selectedProfile = selected;
            if (snapshot.Source != "shutdown")
                RuntimeStatusMessage = snapshot.Preview is { } preview
                    ? $"Previewing {preview.ProfileId} until {preview.ExpiresAt.LocalDateTime:T}; saved selection remains {snapshot.RuntimeStatus.Desired.ProfileId}. {snapshot.RuntimeStatus.AppliedDetail}"
                    : snapshot.RuntimeStatus.AppliedDetail;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsManual)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAutomatic)));
            NotifyStateChanged(nameof(SelectedProfile));
        }

        private static string DescribeReason(SelectionReason reason) => reason.Kind switch
        {
            "first-run" => "First run starts with the protected Default profile.",
            "restored" => "Restored from the last saved selection.",
            "manual" => "Selected manually and saved locally.",
            "observation-pending" => "Waiting for a stable application observation.",
            "rule" => $"Matched Focus '{reason.RuleId}'.",
            "unmatched-default" => "No Focus matched; using Default.",
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

        public void ReloadSettingsAfterConflict()
        {
            if (!HasSettingsConflict || _applicationState is null) return;
            var current = _applicationState.Current;
            _settings = current.Settings;
            _gameCatalog = current.GameCatalog;
            _settingsRevision = current.SettingsRevision;
            _selectionState = current.SelectionState;
            ReloadSettingsProjection();
            HasSettingsConflict = false;
            ApplyApplicationSnapshot(current);
            ProfileEditorStatus = "Reloaded the current saved settings; the conflicting draft was discarded.";
            ApplicationGroupEditorStatus = "Reloaded the current saved settings.";
            GameEditorStatus = "Reloaded the current saved game catalog.";
            ApplicationSettingsStatus = "Reloaded the current saved application preferences.";
            MappingEditor.ReportSaveFailure("Reloaded the current saved Focuses.");
        }

        private bool HasDirtyEditor()
        {
            var groupDirty = EditingApplicationGroup is null
                ? !string.IsNullOrWhiteSpace(ApplicationGroupName) || DraftExecutables.Count > 0
                : !_settings.ApplicationGroups!.Any(group => group.Id == EditingApplicationGroup.Id)
                  || ApplicationGroupName != EditingApplicationGroup.DisplayName
                  || !DraftExecutables.SequenceEqual(EditingApplicationGroup.Executables);
            var gameDirty = CanEditGame && (EditingGame is null
                ? !string.IsNullOrWhiteSpace(GameName) || DraftGameExecutables.Count > 0
                : GameName != EditingGame.DisplayName
                  || !DraftGameExecutables.SequenceEqual(EditingGame.Executables));
            return IsProfileDirty || MappingEditor.IsDirty || IsApplicationSettingsDirty || groupDirty || gameDirty || HasDirtyGameExecutableSelection();
        }

        private bool HasDirtyGameExecutableSelection() => HasDiscoveredGame
            && !GameExecutableOptions.Where(option => option.IsSelected).Select(option => option.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(EditingGame!.MatchingExecutables);

        private void ReportSettingsConflict()
        {
            ProfileEditorStatus = SettingsConflictMessage;
            ApplicationGroupEditorStatus = SettingsConflictMessage;
            ApplicationSettingsStatus = SettingsConflictMessage;
            GameEditorStatus = SettingsConflictMessage;
            MappingEditor.ReportSaveFailure(SettingsConflictMessage);
        }

        private void ReloadSettingsProjection()
        {
            var editingProfileId = EditingProfile?.Id;
            var editingGroupId = EditingApplicationGroup?.Id;
            var profiles = CreateProfileOptions(_settings.Profiles!);
            Profiles.Clear();
            foreach (var profile in profiles) Profiles.Add(profile);
            MappingEditor = MappingEditorViewModel.Create(_settings, Profiles, _gameCatalog);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MappingEditor)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ApplicationGroups)));
            ReloadGameProjection();

            var selectedId = _selectionState?.Mode == "manual"
                ? _selectionState.ManualProfile
                : _selectionState?.LastSelectedProfile;
            _selectedProfile = Profiles.FirstOrDefault(profile => profile.Id == selectedId)
                ?? Profiles.Single(profile => profile.Id == _settings.Selection!.DefaultProfile);
            _editingProfile = null;
            _editingApplicationGroup = null;
            IsProfileDirty = false;
            IsApplicationSettingsDirty = false;
            EditingProfile = Profiles.FirstOrDefault(profile => profile.Id == editingProfileId) ?? Profiles[0];
            EditingApplicationGroup = ApplicationGroups.FirstOrDefault(group => group.Id == editingGroupId)
                ?? ApplicationGroups.FirstOrDefault();
            LoadApplicationSettings();
            NotifyStateChanged(nameof(SelectedProfile));
        }

        private void ReloadGameProjection(bool preserveManualDraft = false)
        {
            var preserveDraft = preserveManualDraft && (EditingGame?.IsManual == true
                || EditingGame is null && (GameName.Length > 0 || DraftGameExecutables.Count > 0));
            var draftName = GameName;
            var draftExecutables = DraftGameExecutables.ToList();
            var selectedExecutable = SelectedGameExecutable;
            var preserveExecutableSelection = preserveManualDraft && HasDirtyGameExecutableSelection();
            var executableChoices = GameExecutableOptions.Where(option => option.IsSelected).Select(option => option.Name).ToArray();
            GameIdentity? selected = EditingGame is null
                ? null
                : new GameIdentity(EditingGame.Launcher, EditingGame.Id);
            var ambiguous = _gameCatalog.Games!.SelectMany(game => game.MatchingExecutables)
                .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1).Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Games.Clear();
            foreach (var game in _gameCatalog.Games!
                .OrderBy(game => game.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(game => game.Launcher, StringComparer.Ordinal)
                .ThenBy(game => game.Id, StringComparer.Ordinal))
                Games.Add(new GameCatalogItemViewModel(game, ambiguous));
            if (!(preserveDraft && selected is null))
                EditingGame = Games.FirstOrDefault(game => selected is not null
                    && game.Launcher == selected.Value.Launcher && game.Id == selected.Value.Id)
                    ?? Games.FirstOrDefault(game => game.IsManual) ?? Games.FirstOrDefault();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GamePickerPlaceholder)));
            if (preserveDraft)
            {
                GameName = draftName;
                DraftGameExecutables.Clear();
                foreach (var executable in draftExecutables) DraftGameExecutables.Add(executable);
                SelectedGameExecutable = selectedExecutable;
            }
            if (preserveExecutableSelection && EditingGame is not null && selected is not null
                && EditingGame.Launcher == selected.Value.Launcher && EditingGame.Id == selected.Value.Id)
            {
                foreach (var name in executableChoices)
                    if (!GameExecutableOptions.Any(option => string.Equals(option.Name, name, StringComparison.OrdinalIgnoreCase)))
                        GameExecutableOptions.Add(new GameExecutableOptionViewModel(name, true));
                foreach (var option in GameExecutableOptions)
                    option.IsSelected = executableChoices.Contains(option.Name, StringComparer.OrdinalIgnoreCase);
            }
        }

        private static IEnumerable<ProfileOptionViewModel> CreateProfileOptions(
            IEnumerable<ProfileSettings> profiles) => profiles.Select(profile =>
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
            });

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

        private void LoadSavedProviderResources(ProfileOptionViewModel? profile)
        {
            ProfileEffectOptions.Clear();
            ProfileLayoutOptions.Clear();
            SelectedProfileEffect = null;
            SelectedProfileLayout = null;
            if (profile is null) return;
            var binding = _settings.Profiles!.Single(item => item.Id == profile.Id).Bindings![0];
            ReplaceResourceOptions(ProfileEffectOptions, [], binding.Effect, retainCurrent: false);
            ReplaceResourceOptions(ProfileLayoutOptions, [], binding.Layout, retainCurrent: true);
            SelectProviderResources(binding.Effect, binding.Layout);
            ProfileResourceStatus = $"Plugin '{binding.Plugin}' has not reported its catalogs yet.";
        }

        private async Task<ResourceCatalogLoad> LoadCatalogAsync(
            string pluginId,
            string kind,
            IReadOnlyList<PluginCapability> capabilities,
            CancellationToken cancellationToken)
        {
            var capability = capabilities.SingleOrDefault(item => item.Id == $"provider.{kind}.catalog");
            if (capability is null || capability.Support == "unsupported")
                return new ResourceCatalogLoad([], capability?.Reason?.Message ?? $"{kind} catalog unsupported.");
            var selection = capabilities.SingleOrDefault(item => item.Id == $"provider.{kind}.select");
            if (selection is null || selection.Support == "unsupported")
                return new ResourceCatalogLoad([], selection?.Reason?.Message ?? $"{kind} selection unsupported.");
            var result = await _pluginGateway!.ListCatalogAsync(pluginId, kind, cancellationToken);
            if (!result.IsSuccess)
                return new ResourceCatalogLoad([], result.ErrorMessage!);
            if (result.Value!.Support != "supported")
                return new ResourceCatalogLoad([], result.Value.Reason?.Message ?? $"{kind} catalog unsupported.");
            return new ResourceCatalogLoad(result.Value.Items, $"{result.Value.Items.Count} reported.");
        }

        private static void ReplaceResourceOptions(
            ObservableCollection<ProviderResourceOptionViewModel> target,
            IReadOnlyList<PluginCatalogItem> items,
            ResourceReference? saved,
            bool retainCurrent)
        {
            target.Clear();
            if (retainCurrent)
            {
                target.Add(new ProviderResourceOptionViewModel
                {
                    ResourceId = null,
                    DisplayName = "Current provider layout",
                    IsAvailable = true
                });
            }
            foreach (var item in items)
            {
                target.Add(new ProviderResourceOptionViewModel
                {
                    ResourceId = item.ResourceId,
                    DisplayName = item.DisplayName,
                    IsAvailable = item.Available,
                    UnavailableReason = item.Reason?.Message
                });
            }
            var savedMatches = saved is null
                ? []
                : target.Where(item => item.DisplayName == saved.DisplayName).ToList();
            if (saved is not null
                && !target.Any(item => item.ResourceId == saved.ResourceId
                    && item.DisplayName == saved.DisplayName)
                && (saved.ResourceId is not null || savedMatches.Count != 1))
            {
                target.Add(new ProviderResourceOptionViewModel
                {
                    ResourceId = saved.ResourceId,
                    DisplayName = saved.DisplayName!,
                    IsAvailable = false,
                    UnavailableReason = "not reported by the provider"
                });
            }
        }

        private void SelectProviderResources(ResourceReference? effect, ResourceReference? layout)
        {
            SelectedProfileEffect = MatchSaved(ProfileEffectOptions, effect);
            SelectedProfileLayout = layout is null
                ? ProfileLayoutOptions.FirstOrDefault(item => item.ResourceId is null)
                : MatchSaved(ProfileLayoutOptions, layout);
        }

        private static ResourceReference DraftResource(
            ProviderResourceOptionViewModel? selected,
            string displayName) => new()
            {
                ResourceId = selected?.DisplayName == displayName ? selected.ResourceId : null,
                DisplayName = displayName
            };

        private static ProviderResourceOptionViewModel? MatchSaved(
            IEnumerable<ProviderResourceOptionViewModel> options,
            ResourceReference? saved)
        {
            if (saved is null) return null;
            var values = options.ToList();
            if (saved.ResourceId is not null)
                return values.FirstOrDefault(item => item.ResourceId == saved.ResourceId
                    && item.DisplayName == saved.DisplayName);
            var matches = values.Where(item => item.DisplayName == saved.DisplayName).ToList();
            return matches.Count == 1
                ? matches[0]
                : matches.SingleOrDefault(item => item.ResourceId is null);
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
                ResourceId = SelectedProfileEffect?.DisplayName == effectName
                    ? SelectedProfileEffect.ResourceId
                    : binding.Effect?.DisplayName == effectName ? binding.Effect.ResourceId : null,
                DisplayName = effectName,
                AdditionalProperties = binding.Effect?.AdditionalProperties
            };
            ResourceReference? layout = null;
            if (!string.IsNullOrWhiteSpace(layoutName)
                && !string.Equals(layoutName, "Current provider layout", StringComparison.Ordinal))
            {
                layout = new ResourceReference
                {
                    ResourceId = SelectedProfileLayout?.DisplayName == layoutName
                        ? SelectedProfileLayout.ResourceId
                        : binding.Layout?.DisplayName == layoutName ? binding.Layout.ResourceId : null,
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

        private SettingsDocument WithPlugins(PluginSettings plugins) => new()
        {
            SchemaVersion = _settings.SchemaVersion,
            Application = _settings.Application,
            Plugins = plugins,
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
                group.Id,
                group.DisplayName,
                group.Executables,
                _settings.ApplicationGroups!.FirstOrDefault(saved => saved.Id == group.Id)?.AdditionalProperties)).ToList();

        private static ApplicationGroupSettings CreateApplicationGroupSettings(
            string id,
            string displayName,
            IEnumerable<string> executables,
            Dictionary<string, System.Text.Json.JsonElement>? additionalProperties = null) => new()
            {
                Id = id,
                DisplayName = displayName,
                Executables = executables.ToList(),
                AdditionalProperties = additionalProperties
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
            DebuggingMode = application.DebuggingMode == true;
            VerboseLogging = application.VerboseLogging!.Value;
            ShowProfileChangePopup = application.ShowProfileChangePopup ?? true;
            NotificationSize = application.Notification!.Size switch { "small" => "Small", "large" => "Large", _ => "Medium" };
            NotificationBackground = application.Notification.Background switch { "light" => "Light", "blue" => "Blue", _ => "Dark" };
            NotificationDurationSeconds = application.Notification.DurationSeconds!.Value;
            IncludeBetaPlugins = SavedIncludeBetaPlugins;
            SwitchingSpeed = application.SwitchingSpeed switch
            {
                "fast" => SwitchingSpeeds[0],
                "smooth" => SwitchingSpeeds[2],
                _ => SwitchingSpeeds[1]
            };
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
            _settingsSaveWarning = null;
            if (_applicationState is null)
            {
                var validationError = ValidateCandidate(candidate);
                if (validationError is not null) return validationError;
                _settings = candidate;
                return null;
            }

            var result = await _applicationState.ApplySettingsAsync(
                candidate, _settingsRevision, "ui", cancellationToken);
            if (result.Warning is not null)
                _settingsSaveWarning = $"{result.Warning.FileName}: {result.Warning.Message}";
            if (!result.IsSuccess) return result.ErrorMessage;
            ApplyApplicationSnapshot(result.Snapshot);
            return null;
        }

        private async Task<string?> SaveGameCatalogCandidateAsync(
            GameCatalogDocument candidate,
            CancellationToken cancellationToken,
            string source = "ui")
        {
            if (_applicationState is null)
            {
                var errors = GameCatalogJson.Validate(candidate);
                if (errors.Count > 0)
                    return string.Join("; ", errors.Select(error => $"{error.Path}: {error.Message}"));
                _gameCatalog = candidate;
                return null;
            }

            var result = await _applicationState.ApplyGameCatalogAsync(
                candidate, _settingsRevision, source, cancellationToken);
            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "conflict")
                {
                    HasSettingsConflict = true;
                    ReportSettingsConflict();
                }
                return result.ErrorMessage;
            }
            ApplyApplicationSnapshot(result.Snapshot);
            return null;
        }

        private string SavedStatus(string message) => _settingsSaveWarning is null
            ? message
            : $"{message} Warning: {_settingsSaveWarning}";

        public async Task ReapplyOutputAsync(CancellationToken cancellationToken = default)
        {
            if (_applicationState is null || _selectionState is null) return;
            var result = await _applicationState.ReapplyAsync(cancellationToken: cancellationToken);
            if (result.IsSuccess) ApplyApplicationSnapshot(result.Snapshot);
            else RuntimeStatusMessage = result.ErrorMessage!;
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
            if (HasSettingsConflict)
            {
                ReportSettingsConflict();
                return false;
            }
            if (Interlocked.CompareExchange(ref _settingsSavePending, 1, 0) != 0) return false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSettingsSavePending)));
            return true;
        }

        private void EndSettingsSave()
        {
            Interlocked.Exchange(ref _settingsSavePending, 0);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSettingsSavePending)));
        }

        private bool SetProfileDraft<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (!Set(ref field, value, propertyName)) return false;
            if (_loadingProfileDraft) return true;
            UpdateProfileDraftState();
            return true;
        }

        private void UpdateProfileDraftState()
        {
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
        }

        private bool ProfileDraftMatchesSaved()
        {
            if (EditingProfile is null) return true;
            var saved = TemplateFor(EditingProfile.Id);
            var fallback = saved.Fallback!.FirstOrDefault();
            var savedEffect = saved.Bindings![0].Effect!;
            var savedLayoutResource = saved.Bindings[0].Layout;
            var savedLayout = saved.Bindings![0].Layout?.DisplayName ?? "Current provider layout";
            return ProfileName == EditingProfile.DisplayName
                && ProfileEffect == savedEffect.DisplayName
                && ResourceMatchesSaved(SelectedProfileEffect, savedEffect)
                && ProfileLayout == savedLayout
                && ResourceMatchesSaved(SelectedProfileLayout, savedLayoutResource)
                && ProfileFallbackKind == (fallback?.Kind ?? "native-setup")
                && (ProfileFallbackKind != "profile" || ProfileFallbackProfile?.Id == fallback?.Profile);
        }

        private static bool ResourceMatchesSaved(
            ProviderResourceOptionViewModel? selected,
            ResourceReference? saved) => saved is null
                ? selected?.ResourceId is null
                : selected?.DisplayName == saved.DisplayName
                    && (saved.ResourceId is null || selected?.ResourceId == saved.ResourceId);

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

        private sealed record ResourceCatalogLoad(
            IReadOnlyList<PluginCatalogItem> Items,
            string Status);
    }
}
