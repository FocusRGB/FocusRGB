using System.ComponentModel;
using System.Runtime.CompilerServices;
using FocusRgb.Core.Configuration;

namespace FocusRgb.App.ViewModels
{
    /// <summary>Holds explicit preview state until selection and output services are available.</summary>
    public sealed class MainWindowViewModel : INotifyPropertyChanged
    {
        private bool _isManual = true;
        private ProfileOptionViewModel _selectedProfile;

        private MainWindowViewModel(IReadOnlyList<ProfileOptionViewModel> profiles, ProfileOptionViewModel selectedProfile)
        {
            Profiles = profiles;
            _selectedProfile = selectedProfile;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public IReadOnlyList<ProfileOptionViewModel> Profiles { get; }

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
            if (!result.IsSuccess || result.Settings?.Profiles is not { Count: > 0 } settingsProfiles)
            {
                throw new InvalidOperationException("Validated default profiles are unavailable.");
            }

            var profiles = settingsProfiles.Select(profile => new ProfileOptionViewModel
            {
                Id = profile.Id!,
                DisplayName = profile.DisplayName!,
                EffectName = profile.Bindings![0].Effect!.DisplayName!,
                LayoutName = profile.Bindings[0].Layout?.DisplayName ?? "Current provider layout"
            }).ToList();
            var defaultId = result.Settings.Selection!.DefaultProfile;
            var selectedProfile = profiles.Single(profile => profile.Id == defaultId);
            return new MainWindowViewModel(profiles, selectedProfile);
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
