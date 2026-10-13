using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using FocusRgb.Core.Configuration;

namespace FocusRgb.App.ViewModels
{
    /// <summary>Holds explicit preview state until selection and output services are available.</summary>
    public sealed class MainWindowViewModel : INotifyPropertyChanged
    {
        private bool _isManual = true;
        private ProfileOptionViewModel _selectedProfile;
        private ProfileOptionViewModel? _editingProfile;
        private ApplicationGroupViewModel? _editingApplicationGroup;
        private string _profileName = "";
        private string _profileEffect = "";
        private string _profileLayout = "";
        private string _applicationGroupName = "";
        private string? _selectedDraftExecutable;
        private string _profileEditorStatus = "Select a profile to edit it.";
        private string _applicationGroupEditorStatus = "Select an application group to edit it.";

        private MainWindowViewModel(
            ObservableCollection<ProfileOptionViewModel> profiles,
            ProfileOptionViewModel selectedProfile,
            MappingEditorViewModel mappingEditor)
        {
            Profiles = profiles;
            _selectedProfile = selectedProfile;
            MappingEditor = mappingEditor;
            EditingProfile = profiles.Count > 0 ? profiles[0] : null;
            EditingApplicationGroup = ApplicationGroups.Count > 0 ? ApplicationGroups[0] : null;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<ProfileOptionViewModel> Profiles { get; }
        public MappingEditorViewModel MappingEditor { get; }
        public ObservableCollection<ApplicationGroupViewModel> ApplicationGroups => MappingEditor.ApplicationGroups;
        public ObservableCollection<string> DraftExecutables { get; } = [];

        public ProfileOptionViewModel? EditingProfile
        {
            get => _editingProfile;
            set
            {
                if (!Set(ref _editingProfile, value)) return;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasEditingProfile)));
                ProfileName = value?.DisplayName ?? "";
                ProfileEffect = value?.EffectName ?? "";
                ProfileLayout = value?.LayoutName ?? "";
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

        public string ProfileName { get => _profileName; set => Set(ref _profileName, value); }
        public string ProfileEffect { get => _profileEffect; set => Set(ref _profileEffect, value); }
        public string ProfileLayout { get => _profileLayout; set => Set(ref _profileLayout, value); }
        public string ApplicationGroupName { get => _applicationGroupName; set => Set(ref _applicationGroupName, value); }
        public string? SelectedDraftExecutable { get => _selectedDraftExecutable; set => Set(ref _selectedDraftExecutable, value); }
        public string ProfileEditorStatus { get => _profileEditorStatus; private set => Set(ref _profileEditorStatus, value); }
        public string ApplicationGroupEditorStatus { get => _applicationGroupEditorStatus; private set => Set(ref _applicationGroupEditorStatus, value); }
        public bool HasEditingProfile => EditingProfile is not null;
        public bool HasEditingApplicationGroup => EditingApplicationGroup is not null;
        public bool HasNoEditingApplicationGroup => EditingApplicationGroup is null;

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
        public string AppliedProfileName => $"{SelectedProfile.DisplayName} (preview)";
        public string SelectionReason => IsManual
            ? "Manual preview selection; no command was sent."
            : "Automatic preview mode; application detection is not connected yet.";

        public void SaveProfile()
        {
            if (EditingProfile is null) return;
            if (EditingProfile.Id == "default")
                ProfileName = EditingProfile.DisplayName;
            else
                EditingProfile.DisplayName = RequiredName(ProfileName, "Profile");
            EditingProfile.EffectName = ProfileEffect.Trim();
            EditingProfile.LayoutName = ProfileLayout.Trim();
            NotifyStateChanged(nameof(SelectedProfile));
            ProfileEditorStatus = $"Saved {EditingProfile.DisplayName} in preview state.";
        }

        public void SaveProfileAs()
        {
            var name = RequiredName(ProfileName, "New Profile");
            var profile = new ProfileOptionViewModel
            {
                Id = UniqueId(name, Profiles.Select(item => item.Id)),
                DisplayName = name,
                EffectName = ProfileEffect.Trim(),
                LayoutName = ProfileLayout.Trim()
            };
            Profiles.Add(profile);
            EditingProfile = profile;
            MappingEditor.NewProfile = profile;
            ProfileEditorStatus = $"Saved {name} as a new profile in preview state.";
        }

        public void RemoveProfile()
        {
            if (EditingProfile is null) return;
            if (EditingProfile.Id == "default")
            {
                ProfileEditorStatus = "Default is protected and cannot be removed.";
                return;
            }
            if (MappingEditor.Mappings.Any(mapping => ReferenceEquals(mapping.SelectedProfile, EditingProfile)))
            {
                ProfileEditorStatus = "Reassign or remove mappings that use this profile first.";
                return;
            }
            var removed = EditingProfile.DisplayName;
            if (ReferenceEquals(SelectedProfile, EditingProfile))
                SelectedProfile = Profiles.First(profile => !ReferenceEquals(profile, EditingProfile));
            Profiles.Remove(EditingProfile);
            EditingProfile = Profiles.Count > 0 ? Profiles[0] : null;
            MappingEditor.NewProfile = EditingProfile;
            ProfileEditorStatus = $"Removed {removed} from preview state.";
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

            return CreatePreview(result.Settings);
        }

        public static MainWindowViewModel CreatePreview(SettingsDocument settings)
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
            return new MainWindowViewModel(profiles, selectedProfile, mappingEditor);
        }

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
