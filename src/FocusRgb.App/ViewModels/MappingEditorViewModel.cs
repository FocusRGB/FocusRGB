using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FocusRgb.Core.Configuration;

namespace FocusRgb.App.ViewModels
{
    /// <summary>Owns ordered mapping drafts and their reusable application groups.</summary>
    public sealed class MappingEditorViewModel : INotifyPropertyChanged
    {
        private IReadOnlyList<MappingSnapshot> _appliedMappings;
        private bool _isDirty;
        private bool _livePreview;
        private MappingEditorItemViewModel? _selectedMapping;
        private ApplicationGroupViewModel? _newApplicationGroup;
        private ProfileOptionViewModel? _newProfile;
        private string _previewStatus = "No preview changes.";

        private MappingEditorViewModel(
            IReadOnlyList<ProfileOptionViewModel> profiles,
            ObservableCollection<ApplicationGroupViewModel> applicationGroups,
            IReadOnlyList<MappingSnapshot> mappings)
        {
            Profiles = profiles;
            ApplicationGroups = applicationGroups;
            _appliedMappings = mappings;
            Mappings = [];
            NewApplicationGroup = ApplicationGroups.Count > 0 ? ApplicationGroups[0] : null;
            NewProfile = Profiles.Count > 0 ? Profiles[0] : null;
            RestoreDraft(mappings);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public IReadOnlyList<ProfileOptionViewModel> Profiles { get; }
        public ObservableCollection<ApplicationGroupViewModel> ApplicationGroups { get; }
        public ObservableCollection<MappingEditorItemViewModel> Mappings { get; }
        public IReadOnlyList<MappingSnapshot> AppliedMappings => _appliedMappings;
        public int PreviewRevision { get; private set; }
        public int AppliedRevision { get; private set; }

        public MappingEditorItemViewModel? SelectedMapping { get => _selectedMapping; set => Set(ref _selectedMapping, value); }
        public ApplicationGroupViewModel? NewApplicationGroup { get => _newApplicationGroup; set => Set(ref _newApplicationGroup, value); }
        public ProfileOptionViewModel? NewProfile { get => _newProfile; set => Set(ref _newProfile, value); }

        public bool LivePreview
        {
            get => _livePreview;
            set
            {
                if (!Set(ref _livePreview, value) || !IsDirty) return;
                if (value) UpdatePreview("Live preview updated from the current draft.");
                else PreviewStatus = "Draft changes are waiting for Apply.";
            }
        }

        public bool IsDirty { get => _isDirty; private set => Set(ref _isDirty, value); }
        public string PreviewStatus { get => _previewStatus; private set => Set(ref _previewStatus, value); }

        public static MappingEditorViewModel Create(SettingsDocument settings, IReadOnlyList<ProfileOptionViewModel> profiles)
        {
            ArgumentNullException.ThrowIfNull(settings);
            var groups = new ObservableCollection<ApplicationGroupViewModel>();
            var snapshots = settings.Rules!.Select(rule =>
            {
                var profile = profiles.Single(item => item.Id == rule.Profile);
                groups.Add(new ApplicationGroupViewModel(rule.Id!, profile.DisplayName, rule.Match!.Executables!));
                return new MappingSnapshot(rule.Id!, rule.Id!, profile.Id, rule.Eligibility!);
            }).ToList();
            return new MappingEditorViewModel(profiles, groups, snapshots);
        }

        public ApplicationGroupViewModel AddApplicationGroup()
        {
            var number = ApplicationGroups.Count + 1;
            var id = $"application-group-{number}";
            while (ApplicationGroups.Any(group => group.Id == id)) id = $"application-group-{++number}";
            var group = new ApplicationGroupViewModel(id, $"Application Group {number}", []);
            ApplicationGroups.Add(group);
            NewApplicationGroup = group;
            return group;
        }

        public void AddMapping()
        {
            if (NewApplicationGroup is null || NewProfile is null) return;
            var baseId = $"{NewApplicationGroup.Id}-{NewProfile.Id}";
            var id = baseId;
            var suffix = 2;
            while (Mappings.Any(mapping => mapping.Id == id)) id = $"{baseId}-{suffix++}";
            var item = new MappingEditorItemViewModel(this,
                new MappingSnapshot(id, NewApplicationGroup.Id, NewProfile.Id, "while-focused"));
            Mappings.Add(item);
            UpdatePositions();
            SelectedMapping = item;
            NotifyDraftChanged();
        }

        public void Apply()
        {
            _appliedMappings = Mappings.Select(mapping => mapping.CreateSnapshot()).ToList();
            AppliedRevision++;
            OnPropertyChanged(nameof(AppliedMappings));
            OnPropertyChanged(nameof(AppliedRevision));
            IsDirty = false;
            UpdatePreview($"Applied {_appliedMappings.Count} mappings to preview state.");
        }

        public void Cancel()
        {
            var selectedId = SelectedMapping?.Id;
            RestoreDraft(_appliedMappings);
            SelectedMapping = Mappings.FirstOrDefault(mapping => mapping.Id == selectedId) ?? Mappings.FirstOrDefault();
            IsDirty = false;
            UpdatePreview("Preview restored to the last applied mappings.");
        }

        public void Move(MappingEditorItemViewModel item, int offset)
        {
            var currentIndex = Mappings.IndexOf(item);
            var newIndex = currentIndex + offset;
            if (currentIndex < 0 || newIndex < 0 || newIndex >= Mappings.Count) return;
            Mappings.Move(currentIndex, newIndex);
            UpdatePositions();
            NotifyDraftChanged();
        }

        public void MoveTo(MappingEditorItemViewModel item, MappingEditorItemViewModel target)
        {
            var oldIndex = Mappings.IndexOf(item);
            var newIndex = Mappings.IndexOf(target);
            if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex) return;
            Mappings.Move(oldIndex, newIndex);
            UpdatePositions();
            NotifyDraftChanged();
        }

        public void MoveSelected(int offset) { if (SelectedMapping is not null) Move(SelectedMapping, offset); }

        internal void NotifyDraftChanged()
        {
            IsDirty = !Mappings.Select(mapping => mapping.CreateSnapshot()).SequenceEqual(_appliedMappings);
            PreviewStatus = IsDirty
                ? LivePreview ? "Live preview updated from the current draft." : "Draft changes are waiting for Apply."
                : "Draft matches the last applied mappings.";
            if (LivePreview) { PreviewRevision++; OnPropertyChanged(nameof(PreviewRevision)); }
        }

        private void RestoreDraft(IReadOnlyList<MappingSnapshot> snapshots)
        {
            Mappings.Clear();
            foreach (var snapshot in snapshots) Mappings.Add(new MappingEditorItemViewModel(this, snapshot));
            UpdatePositions();
            SelectedMapping = Mappings.FirstOrDefault();
        }

        private void UpdatePositions()
        {
            for (var index = 0; index < Mappings.Count; index++) Mappings[index].SetPosition(index + 1);
        }

        private void UpdatePreview(string status)
        {
            PreviewRevision++;
            OnPropertyChanged(nameof(PreviewRevision));
            PreviewStatus = status;
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged(string? propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
