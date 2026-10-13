using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FocusRgb.App.ViewModels
{
    /// <summary>Edits one application-group-to-profile mapping in the in-memory draft.</summary>
    public sealed class MappingEditorItemViewModel : INotifyPropertyChanged
    {
        private readonly MappingEditorViewModel _editor;
        private ApplicationGroupViewModel _selectedApplicationGroup;
        private ProfileOptionViewModel _selectedProfile;
        private string _eligibility;
        private int _position;

        internal MappingEditorItemViewModel(MappingEditorViewModel editor, MappingSnapshot snapshot)
        {
            _editor = editor;
            Id = snapshot.Id;
            ApplicationGroups = editor.ApplicationGroups;
            Profiles = editor.Profiles;
            EligibilityOptions = ["while-focused", "while-running"];
            _selectedApplicationGroup = ApplicationGroups.Single(group => group.Id == snapshot.ApplicationGroupId);
            _selectedProfile = Profiles.Single(profile => profile.Id == snapshot.ProfileId);
            _eligibility = snapshot.Eligibility;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; }
        public IReadOnlyList<ApplicationGroupViewModel> ApplicationGroups { get; }
        public IReadOnlyList<ProfileOptionViewModel> Profiles { get; }
        public IReadOnlyList<string> EligibilityOptions { get; }

        public int Position
        {
            get => _position;
            private set => Set(ref _position, value, false);
        }

        public ApplicationGroupViewModel SelectedApplicationGroup
        {
            get => _selectedApplicationGroup;
            set => Set(ref _selectedApplicationGroup, value);
        }

        public ProfileOptionViewModel SelectedProfile
        {
            get => _selectedProfile;
            set => Set(ref _selectedProfile, value);
        }

        public string Eligibility
        {
            get => _eligibility;
            set => Set(ref _eligibility, value);
        }

        internal MappingSnapshot CreateSnapshot() =>
            new(Id, SelectedApplicationGroup.Id, SelectedProfile.Id, Eligibility);

        internal void SetPosition(int position) => Position = position;

        private void Set<T>(ref T field, T value, bool notifyEditor = true,
            [CallerMemberName] string? propertyName = null)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            if (notifyEditor) _editor.NotifyDraftChanged();
        }
    }
}
