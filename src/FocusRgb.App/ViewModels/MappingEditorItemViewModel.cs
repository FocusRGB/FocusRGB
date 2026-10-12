using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FocusRgb.App.ViewModels
{
    /// <summary>Edits one application-to-profile mapping in the in-memory draft.</summary>
    public sealed class MappingEditorItemViewModel : INotifyPropertyChanged
    {
        private readonly MappingEditorViewModel _editor;
        private string _applicationName;
        private string _executables;
        private string _eligibility;
        private ResourceOptionViewModel _selectedEffect;
        private ProfileOptionViewModel _selectedProfile;
        private int _priority;
        private int _position;

        internal MappingEditorItemViewModel(
            MappingEditorViewModel editor,
            MappingSnapshot snapshot,
            IReadOnlyList<ProfileOptionViewModel> profiles,
            IReadOnlyList<ResourceOptionViewModel> effects)
        {
            _editor = editor;
            Id = snapshot.Id;
            Profiles = profiles;
            Effects = effects;
            EligibilityOptions = ["while-focused", "while-running"];
            _applicationName = snapshot.ApplicationName;
            _executables = snapshot.Executables;
            _selectedProfile = profiles.Single(profile => profile.Id == snapshot.ProfileId);
            _eligibility = snapshot.Eligibility;
            _priority = snapshot.Priority;
            _selectedEffect = effects.Single(effect => effect.DisplayName == snapshot.EffectName);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; }
        public IReadOnlyList<ProfileOptionViewModel> Profiles { get; }
        public IReadOnlyList<ResourceOptionViewModel> Effects { get; }
        public IReadOnlyList<string> EligibilityOptions { get; }
        public bool CanChooseLayout { get; }
        public string LayoutStatus { get; } = "Layout catalog unavailable in preview";

        public int Position
        {
            get => _position;
            private set => Set(ref _position, value, notifyEditor: false);
        }

        public string ApplicationName
        {
            get => _applicationName;
            set => Set(ref _applicationName, value);
        }

        public string Executables
        {
            get => _executables;
            set => Set(ref _executables, value);
        }

        public ProfileOptionViewModel SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                if (!Set(ref _selectedProfile, value))
                {
                    return;
                }

                var profileEffect = Effects.Single(effect => effect.DisplayName == value.EffectName);
                Set(ref _selectedEffect, profileEffect, nameof(SelectedEffect), notifyEditor: false);
            }
        }

        public string Eligibility
        {
            get => _eligibility;
            set => Set(ref _eligibility, value);
        }

        public int Priority
        {
            get => _priority;
            set => Set(ref _priority, value);
        }

        public ResourceOptionViewModel SelectedEffect
        {
            get => _selectedEffect;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                if (value.IsAvailable)
                {
                    Set(ref _selectedEffect, value);
                }
            }
        }

        internal MappingSnapshot CreateSnapshot() => new(
            Id,
            ApplicationName,
            Executables,
            SelectedProfile.Id,
            Eligibility,
            Priority,
            SelectedEffect.DisplayName);

        internal void SetPosition(int position) => Position = position;

        private bool Set<T>(
            ref T field,
            T value,
            [CallerMemberName] string? propertyName = null,
            bool notifyEditor = true)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            if (notifyEditor)
            {
                _editor.NotifyDraftChanged();
            }

            return true;
        }
    }
}
