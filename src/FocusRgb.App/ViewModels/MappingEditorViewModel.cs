using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FocusRgb.Core.Configuration;

namespace FocusRgb.App.ViewModels
{
    /// <summary>Owns draft, preview, and applied state for the profile mapping editor.</summary>
    public sealed class MappingEditorViewModel : INotifyPropertyChanged
    {
        private readonly IReadOnlyList<ResourceOptionViewModel> _effects;
        private IReadOnlyList<MappingSnapshot> _appliedMappings;
        private bool _isDirty;
        private bool _livePreview;
        private MappingEditorItemViewModel? _selectedMapping;
        private string _previewStatus = "No preview changes.";

        private MappingEditorViewModel(
            IReadOnlyList<ProfileOptionViewModel> profiles,
            IReadOnlyList<ResourceOptionViewModel> effects,
            IReadOnlyList<MappingSnapshot> mappings)
        {
            Profiles = profiles;
            _effects = effects;
            _appliedMappings = mappings;
            Mappings = [];
            RestoreDraft(mappings);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public IReadOnlyList<ProfileOptionViewModel> Profiles { get; }
        public ObservableCollection<MappingEditorItemViewModel> Mappings { get; }
        public IReadOnlyList<MappingSnapshot> AppliedMappings => _appliedMappings;
        public int PreviewRevision { get; private set; }
        public int AppliedRevision { get; private set; }

        public MappingEditorItemViewModel? SelectedMapping
        {
            get => _selectedMapping;
            set => Set(ref _selectedMapping, value);
        }

        public bool LivePreview
        {
            get => _livePreview;
            set
            {
                if (!Set(ref _livePreview, value) || !IsDirty)
                {
                    return;
                }

                if (value)
                {
                    UpdatePreview("Live preview updated from the current draft.");
                }
                else
                {
                    PreviewStatus = "Draft changes are waiting for Apply.";
                }
            }
        }

        public bool IsDirty
        {
            get => _isDirty;
            private set => Set(ref _isDirty, value);
        }

        public string PreviewStatus
        {
            get => _previewStatus;
            private set => Set(ref _previewStatus, value);
        }

        public static MappingEditorViewModel Create(
            SettingsDocument settings,
            IReadOnlyList<ProfileOptionViewModel> profiles)
        {
            ArgumentNullException.ThrowIfNull(settings);
            var effects = profiles
                .Select(profile => profile.EffectName)
                .Distinct(StringComparer.Ordinal)
                .Select(name => new ResourceOptionViewModel { DisplayName = name, IsAvailable = true })
                .ToList();
            var snapshots = settings.Rules!.Select(rule =>
            {
                var profile = profiles.Single(item => item.Id == rule.Profile);
                return new MappingSnapshot(
                    rule.Id!,
                    profile.DisplayName,
                    string.Join(", ", rule.Match!.Executables!),
                    profile.Id,
                    rule.Eligibility!,
                    rule.Priority!.Value,
                    profile.EffectName);
            }).ToList();
            return new MappingEditorViewModel(profiles, effects, snapshots);
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

        public void MoveSelected(int offset)
        {
            if (SelectedMapping is null)
            {
                return;
            }

            var currentIndex = Mappings.IndexOf(SelectedMapping);
            var newIndex = currentIndex + offset;
            if (newIndex < 0 || newIndex >= Mappings.Count)
            {
                return;
            }

            Mappings.Move(currentIndex, newIndex);
            UpdatePositions();
            NotifyDraftChanged();
        }

        internal void NotifyDraftChanged()
        {
            IsDirty = !Mappings.Select(mapping => mapping.CreateSnapshot()).SequenceEqual(_appliedMappings);
            PreviewStatus = IsDirty
                ? LivePreview
                    ? "Live preview updated from the current draft."
                    : "Draft changes are waiting for Apply."
                : "Draft matches the last applied mappings.";
            if (LivePreview)
            {
                PreviewRevision++;
                OnPropertyChanged(nameof(PreviewRevision));
            }
        }

        private void RestoreDraft(IReadOnlyList<MappingSnapshot> snapshots)
        {
            Mappings.Clear();
            foreach (var snapshot in snapshots)
            {
                Mappings.Add(new MappingEditorItemViewModel(this, snapshot, Profiles, _effects));
            }

            UpdatePositions();
            SelectedMapping = Mappings.FirstOrDefault();
        }

        private void UpdatePositions()
        {
            for (var index = 0; index < Mappings.Count; index++)
            {
                Mappings[index].SetPosition(index + 1);
            }
        }

        private void UpdatePreview(string status)
        {
            PreviewRevision++;
            OnPropertyChanged(nameof(PreviewRevision));
            PreviewStatus = status;
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged(string? propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
