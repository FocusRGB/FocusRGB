using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FocusRgb.App.ViewModels
{
    /// <summary>Edits one ordered application-or-game mapping draft.</summary>
    public sealed class MappingEditorItemViewModel : INotifyPropertyChanged
    {
        private readonly MappingEditorViewModel _editor;
        private MappingTargetViewModel _selectedTarget;
        private ProfileOptionViewModel _selectedProfile;
        private string _eligibility;
        private int _position;

        internal MappingEditorItemViewModel(MappingEditorViewModel editor, MappingSnapshot snapshot)
        {
            _editor = editor;
            Id = snapshot.Id;
            Targets = editor.Targets;
            Profiles = editor.Profiles;
            EligibilityOptions = ["while-focused", "while-running"];
            _selectedTarget = Targets.Single(target => snapshot.ApplicationGroupId is not null
                ? target.ApplicationGroupId == snapshot.ApplicationGroupId
                : target.GameLauncher == snapshot.GameLauncher && target.GameId == snapshot.GameId);
            _selectedProfile = Profiles.Single(profile => profile.Id == snapshot.ProfileId);
            _eligibility = snapshot.Eligibility;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public string Id { get; }
        public IReadOnlyList<MappingTargetViewModel> Targets { get; }
        public IReadOnlyList<ApplicationGroupViewModel> ApplicationGroups => _editor.ApplicationGroups;
        public IReadOnlyList<ProfileOptionViewModel> Profiles { get; }
        public IReadOnlyList<string> EligibilityOptions { get; }
        public int Position { get => _position; private set { Set(ref _position, value, false); NotifyAccessibility(); } }
        public MappingTargetViewModel SelectedTarget
        {
            get => _selectedTarget;
            set { Set(ref _selectedTarget, value); NotifyAccessibility(); PropertyChanged?.Invoke(this, new(nameof(SelectedApplicationGroup))); }
        }
        public ApplicationGroupViewModel SelectedApplicationGroup
        {
            get => SelectedTarget.ApplicationGroup!;
            set => SelectedTarget = Targets.Single(target => target.ApplicationGroup == value);
        }
        public ProfileOptionViewModel SelectedProfile
        {
            get => _selectedProfile;
            set { Set(ref _selectedProfile, value); NotifyAccessibility(); }
        }
        public string Eligibility
        {
            get => _eligibility;
            set { Set(ref _eligibility, value); NotifyAccessibility(); }
        }

        public string AccessibleName => $"{SelectedTarget.DisplayName} maps to {SelectedProfile.DisplayName}, {Eligibility}";
        public string MoveUpAccessibleName => $"Move {SelectedTarget.DisplayName} Focus up";
        public string MoveDownAccessibleName => $"Move {SelectedTarget.DisplayName} Focus down";
        public bool CanMoveUp => Position > 1;
        public bool CanMoveDown => Position > 0 && Position < _editor.Mappings.Count;
        public string EditAccessibleName => $"Edit {SelectedTarget.DisplayName} Focus";
        public string RemoveAccessibleName => $"Remove {SelectedTarget.DisplayName} Focus";
        public override string ToString() => AccessibleName;

        internal MappingSnapshot CreateSnapshot() => new(
            Id, SelectedTarget.ApplicationGroupId, SelectedTarget.GameLauncher, SelectedTarget.GameId,
            SelectedProfile.Id, Eligibility);

        internal void SetPosition(int position) => Position = position;

        private void NotifyAccessibility()
        {
            foreach (var property in new[]
            {
                nameof(AccessibleName), nameof(MoveUpAccessibleName), nameof(MoveDownAccessibleName),
                nameof(CanMoveUp), nameof(CanMoveDown),
                nameof(EditAccessibleName), nameof(RemoveAccessibleName)
            })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }

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
