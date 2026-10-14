using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using FocusRgb.Core.Commands;
using FocusRgb.Core.Configuration;
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
        private SelectionStateDocument? _selectionState;
        private SelectionReason _selectionReason = FocusRgb.Core.Selection.SelectionReason.Manual();
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
        private string _profileEditorStatus = "Select a profile to edit it.";
        private string _applicationGroupEditorStatus = "Select an application group to edit it.";
        private string _closeButtonBehavior = "Minimize to system tray";
        private bool _startMinimized;
        private string _applicationTheme = "System";
        private bool _verboseLogging;
        private bool _isApplicationSettingsDirty;
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
            bool canMinimizeToTray)
        {
            Profiles = profiles;
            _selectedProfile = selectedProfile;
            MappingEditor = mappingEditor;
            _settings = settings;
            _settingsStore = settingsStore;
            _selectionState = selectionState;
            _selectionCommands = selectionCommands;
            CanMinimizeToTray = canMinimizeToTray;
            if (selectionReason is not null) _selectionReason = selectionReason;
            if (selectionCommands is not null)
                _runtimeStatusMessage = "Local runtime is ready; no output integration is connected.";
            EditingProfile = profiles.Count > 0 ? profiles[0] : null;
            EditingApplicationGroup = ApplicationGroups.Count > 0 ? ApplicationGroups[0] : null;
            LoadApplicationSettings();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<ProfileOptionViewModel> Profiles { get; }
        public MappingEditorViewModel MappingEditor { get; }
        public ObservableCollection<ApplicationGroupViewModel> ApplicationGroups => MappingEditor.ApplicationGroups;
        public ObservableCollection<string> DraftExecutables { get; } = [];
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
            }
        }

        public ApplicationGroupViewModel? EditingApplicationGroup
        {
            get => _editingApplicationGroup;
            set
            {
                if (!Set(ref _editingApplicationGroup, value)) return;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasEditingApplicationGroup)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoEditingApplicationGroup)));
                ApplicationGroupName = value?.DisplayName ?? "";
                DraftExecutables.Clear();
                if (value is not null)
                    foreach (var executable in value.Executables) DraftExecutables.Add(executable);
                SelectedDraftExecutable = null;
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
                ProfileEditorStatus = "Live preview updated from the profile draft.";
            }
        }
        public bool IsProfileDirty { get => _isProfileDirty; private set => Set(ref _isProfileDirty, value); }
        public int ProfilePreviewRevision { get; private set; }
        public string ApplicationGroupName { get => _applicationGroupName; set => Set(ref _applicationGroupName, value); }
        public string? SelectedDraftExecutable { get => _selectedDraftExecutable; set => Set(ref _selectedDraftExecutable, value); }
        public string ProfileEditorStatus { get => _profileEditorStatus; private set => Set(ref _profileEditorStatus, value); }
        public string ApplicationGroupEditorStatus { get => _applicationGroupEditorStatus; private set => Set(ref _applicationGroupEditorStatus, value); }
        public string RuntimeStatusMessage { get => _runtimeStatusMessage; private set => Set(ref _runtimeStatusMessage, value); }
        public bool IsRuntimeCommandPending { get => _isRuntimeCommandPending; private set => Set(ref _isRuntimeCommandPending, value); }
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
        public string AppliedProfileName => _selectionCommands is null
            ? $"{SelectedProfile.DisplayName} (preview)"
            : "Not applied (no output integration)";
        public string SelectionReason => _selectionCommands is null
            ? IsManual
                ? "Manual preview selection; no command was sent."
                : "Automatic preview mode; application detection is not connected yet."
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
                    : "Automatic mode saved; application detection is not connected yet.");
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

        public void NotifyWindowHidden() =>
            RuntimeStatusMessage = "FocusRGB is still running in the system tray.";

        public async Task<bool> SaveApplicationSettingsAsync(CancellationToken cancellationToken = default)
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
            ApplicationSettingsStatus = "Application preferences saved.";
            return true;
        }

        public void CancelApplicationSettings()
        {
            LoadApplicationSettings();
            ApplicationSettingsStatus = "Application preferences restored to the saved values.";
        }

        public void ReportSettingsAction(string message) => ApplicationSettingsStatus = message;

        public async Task SaveProfileAsync(CancellationToken cancellationToken = default)
        {
            if (EditingProfile is null) return;
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
            EditingProfile.DisplayName = name;
            EditingProfile.EffectName = replacement.Bindings![0].Effect!.DisplayName!;
            EditingProfile.LayoutName = replacement.Bindings[0].Layout?.DisplayName ?? "Current provider layout";
            ReloadProfileDraft();
            NotifyStateChanged(nameof(SelectedProfile));
            ProfileEditorStatus = $"Saved {EditingProfile.DisplayName}.";
        }

        public async Task SaveProfileAsAsync(CancellationToken cancellationToken = default)
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
            ProfileEditorStatus = $"Saved {name} as a new profile.";
        }

        public async Task RemoveProfileAsync(CancellationToken cancellationToken = default)
        {
            if (EditingProfile is null) return;
            if (EditingProfile.Id == "default")
            {
                ProfileEditorStatus = "Default is protected and cannot be removed.";
                return;
            }
            if (MappingEditor.Mappings.Any(mapping => ReferenceEquals(mapping.SelectedProfile, EditingProfile)))
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
            if (ReferenceEquals(SelectedProfile, EditingProfile))
                SelectedProfile = Profiles.First(profile => !ReferenceEquals(profile, EditingProfile));
            Profiles.Remove(EditingProfile);
            EditingProfile = Profiles.Count > 0 ? Profiles[0] : null;
            MappingEditor.NewProfile = EditingProfile;
            ProfileEditorStatus = $"Removed {removed}.";
        }

        public void CancelProfile()
        {
            ReloadProfileDraft();
            ProfilePreviewRevision++;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProfilePreviewRevision)));
            ProfileEditorStatus = "Profile draft restored to the last saved values.";
        }

        public void AddDraftExecutable(string path)
        {
            var executable = Path.GetFileName(path);
            if (!string.IsNullOrWhiteSpace(executable)
                && !DraftExecutables.Contains(executable, StringComparer.OrdinalIgnoreCase))
                DraftExecutables.Add(executable);
        }

        public void RemoveDraftExecutable()
        {
            if (SelectedDraftExecutable is null) return;
            DraftExecutables.Remove(SelectedDraftExecutable);
            SelectedDraftExecutable = null;
        }

        public void SaveApplicationGroup()
        {
            if (EditingApplicationGroup is null) return;
            EditingApplicationGroup.DisplayName = RequiredName(ApplicationGroupName, "Application Group");
            ReplaceExecutables(EditingApplicationGroup);
            ApplicationGroupEditorStatus = $"Saved {EditingApplicationGroup.DisplayName} in preview state.";
        }

        public void SaveApplicationGroupAs()
        {
            var name = RequiredName(ApplicationGroupName, "New Application Group");
            var group = new ApplicationGroupViewModel(
                UniqueId(name, ApplicationGroups.Select(item => item.Id)), name, DraftExecutables);
            ApplicationGroups.Add(group);
            EditingApplicationGroup = group;
            MappingEditor.NewApplicationGroup = group;
            ApplicationGroupEditorStatus = $"Saved {name} as a new group in preview state.";
        }

        public void RemoveApplicationGroup()
        {
            if (EditingApplicationGroup is null) return;
            if (MappingEditor.Mappings.Any(mapping => ReferenceEquals(mapping.SelectedApplicationGroup, EditingApplicationGroup)))
            {
                ApplicationGroupEditorStatus = "Remove mappings that use this application group first.";
                return;
            }
            var removed = EditingApplicationGroup.DisplayName;
            ApplicationGroups.Remove(EditingApplicationGroup);
            EditingApplicationGroup = ApplicationGroups.Count > 0 ? ApplicationGroups[0] : null;
            MappingEditor.NewApplicationGroup = EditingApplicationGroup;
            ApplicationGroupEditorStatus = $"Removed {removed} from preview state.";
        }

        public static MainWindowViewModel CreatePreview()
        {
            var result = SettingsJson.LoadDefaults();
            if (!result.IsSuccess || result.Settings is null)
            {
                throw new InvalidOperationException("Validated default profiles are unavailable.");
            }

            return Create(result.Settings, null, null, null, null, canMinimizeToTray: true);
        }

        public static MainWindowViewModel CreatePreview(SettingsDocument settings)
            => Create(settings, null, null, null, null, canMinimizeToTray: true);

        public static MainWindowViewModel CreatePersistent(
            StartupSnapshot snapshot,
            LocalStateStore settingsStore,
            bool canMinimizeToTray = true)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(settingsStore);
            var viewModel = Create(
                snapshot.Settings,
                settingsStore,
                snapshot.State,
                new SelectionCommandService(settingsStore),
                snapshot.Desired.Reason,
                canMinimizeToTray);
            viewModel.IsManual = snapshot.State.Mode == "manual";
            var selectedId = viewModel.IsManual
                ? snapshot.State.ManualProfile
                : snapshot.State.LastSelectedProfile;
            viewModel.SelectedProfile = viewModel.Profiles.Single(profile => profile.Id == selectedId);
            return viewModel;
        }

        private static MainWindowViewModel Create(
            SettingsDocument settings,
            LocalStateStore? settingsStore,
            SelectionStateDocument? selectionState,
            SelectionCommandService? selectionCommands,
            SelectionReason? selectionReason,
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
                RuntimeStatusMessage = successMessage;
            }
            finally
            {
                IsRuntimeCommandPending = false;
            }
        }

        private void ApplyRuntimeResult(SelectionCommandResult result)
        {
            _selectionState = result.State!;
            _selectionReason = result.Status!.Desired.Reason;
            _isManual = result.Status.Mode == "manual";
            _selectedProfile = Profiles.Single(profile => profile.Id == result.Status.Desired.ProfileId);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsManual)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAutomatic)));
            NotifyStateChanged(nameof(SelectedProfile));
        }

        private static string DescribeReason(SelectionReason reason) => reason.Kind switch
        {
            "first-run" => "First run starts with the protected Default profile.",
            "restored" => "Restored from the last saved selection.",
            "manual" => "Selected manually and saved locally.",
            "observation-pending" => "Waiting for application detection, which is not connected yet.",
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
            Selection = _settings.Selection,
            Profiles = profiles,
            Rules = _settings.Rules,
            AdditionalProperties = _settings.AdditionalProperties
        };

        private SettingsDocument WithApplication(ApplicationSettings application) => new()
        {
            SchemaVersion = _settings.SchemaVersion,
            Application = application,
            Selection = _settings.Selection,
            Profiles = _settings.Profiles,
            Rules = _settings.Rules,
            AdditionalProperties = _settings.AdditionalProperties
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
            var errors = SettingsValidator.Validate(candidate);
            if (errors.Count > 0)
                return string.Join("; ", errors.Select(error => $"{error.Path}: {error.Message}"));
            if (_settingsStore is null) return null;
            var result = await _settingsStore.SaveSettingsAsync(candidate, cancellationToken);
            return result.IsSuccess ? null : $"{result.Error!.FileName}: {result.Error.Message}";
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
                ProfileEditorStatus = "Live preview updated from the profile draft.";
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
