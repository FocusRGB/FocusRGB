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

        private MainWindowViewModel(
            IReadOnlyList<ProfileOptionViewModel> profiles,
            ProfileOptionViewModel selectedProfile,
            MappingEditorViewModel mappingEditor)
        {
            Profiles = profiles;
            _selectedProfile = selectedProfile;
            MappingEditor = mappingEditor;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public IReadOnlyList<ProfileOptionViewModel> Profiles { get; }
        public MappingEditorViewModel MappingEditor { get; }
        public ObservableCollection<ApplicationGroupViewModel> ApplicationGroups => MappingEditor.ApplicationGroups;

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

            var profiles = settingsProfiles.Select(profile =>
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
            }).ToList();
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
    }
}
