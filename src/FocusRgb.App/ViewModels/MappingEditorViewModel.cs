using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FocusRgb.Core.Configuration;

namespace FocusRgb.App.ViewModels
{
    /// <summary>Owns one ordered draft of application and launcher-scoped game mappings.</summary>
    public sealed class MappingEditorViewModel : INotifyPropertyChanged
    {
        private IReadOnlyList<MappingSnapshot> _appliedMappings;
        private readonly IReadOnlyList<RuleSettings> _sourceRules;
        private bool _isDirty;
        private bool _livePreview;
        private MappingEditorItemViewModel? _selectedMapping;
        private MappingEditorItemViewModel? _editingMapping;
        private string? _editingOriginalId;
        private MappingTargetViewModel? _newTarget;
        private ProfileOptionViewModel? _newProfile;
        private string _previewStatus = "No preview changes.";
        private MappingEditorItemViewModel[]? _reorderOriginal;

        private MappingEditorViewModel(
            IReadOnlyList<ProfileOptionViewModel> profiles,
            ObservableCollection<ApplicationGroupViewModel> applicationGroups,
            ObservableCollection<MappingTargetViewModel> targets,
            IReadOnlyList<MappingSnapshot> mappings,
            IReadOnlyList<RuleSettings> sourceRules)
        {
            Profiles = profiles;
            ApplicationGroups = applicationGroups;
            Targets = targets;
            _appliedMappings = mappings;
            _sourceRules = sourceRules;
            Mappings = [];
            NewTarget = Targets.FirstOrDefault();
            NewProfile = Profiles.Count > 0 ? Profiles[0] : null;
            RestoreDraft(mappings);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public IReadOnlyList<ProfileOptionViewModel> Profiles { get; }
        public ObservableCollection<ApplicationGroupViewModel> ApplicationGroups { get; }
        public ObservableCollection<MappingTargetViewModel> Targets { get; }
        public ObservableCollection<MappingEditorItemViewModel> Mappings { get; }
        public IReadOnlyList<MappingSnapshot> AppliedMappings => _appliedMappings;
        public int PreviewRevision { get; private set; }
        public int AppliedRevision { get; private set; }
        public MappingEditorItemViewModel? ReorderingMapping { get; private set; }
        public int ReorderIndex { get; private set; } = -1;

        public MappingEditorItemViewModel? SelectedMapping { get => _selectedMapping; set => Set(ref _selectedMapping, value); }
        public MappingEditorItemViewModel? EditingMapping
        {
            get => _editingMapping;
            private set
            {
                Set(ref _editingMapping, value);
                OnPropertyChanged(nameof(HasEditor));
                OnPropertyChanged(nameof(IsDirty));
            }
        }
        public bool HasEditor => EditingMapping is not null;
        public bool IsEmpty => Mappings.Count == 0;
        public MappingTargetViewModel? NewTarget { get => _newTarget; set => Set(ref _newTarget, value); }
        public ApplicationGroupViewModel? NewApplicationGroup
        {
            get => NewTarget?.ApplicationGroup;
            set => NewTarget = value is null ? null : Targets.Single(target => target.ApplicationGroup == value);
        }
        public ProfileOptionViewModel? NewProfile { get => _newProfile; set => Set(ref _newProfile, value); }

        public bool LivePreview
        {
            get => _livePreview;
            set
            {
                if (!Set(ref _livePreview, value) || !IsDirty) return;
                if (value) UpdatePreview("Draft preview updated in the editor; output is sent after Save.");
                else PreviewStatus = "Draft changes are waiting for Save.";
            }
        }

        public bool IsDirty
        {
            get => _isDirty || EditingMapping is { } editing && (_editingOriginalId is null
                || editing.CreateSnapshot() != Mappings.FirstOrDefault(item => item.Id == _editingOriginalId)?.CreateSnapshot());
            private set => Set(ref _isDirty, value);
        }
        public string PreviewStatus { get => _previewStatus; private set => Set(ref _previewStatus, value); }

        public static MappingEditorViewModel Create(
            SettingsDocument settings,
            IReadOnlyList<ProfileOptionViewModel> profiles,
            GameCatalogDocument? gameCatalog = null)
        {
            ArgumentNullException.ThrowIfNull(settings);
            gameCatalog ??= GameCatalogJson.CreateDefault();
            var groups = new ObservableCollection<ApplicationGroupViewModel>(settings.ApplicationGroups!.Select(group =>
                new ApplicationGroupViewModel(group.Id!, group.DisplayName!, group.Executables!)));
            var targets = new ObservableCollection<MappingTargetViewModel>(groups.Select(MappingTargetViewModel.ForApplication));
            targets.Add(MappingTargetViewModel.ForGame("*", "*", "Any known game"));
            foreach (var game in gameCatalog.Games!
                .OrderBy(game => game.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(game => game.Launcher, StringComparer.Ordinal)
                .ThenBy(game => game.Id, StringComparer.Ordinal))
            {
                targets.Add(MappingTargetViewModel.ForGame(
                    game.Launcher!, game.Id!, $"{game.DisplayName} ({game.Launcher})"));
            }
            var rules = settings.Rules!;
            foreach (var game in rules.Select(rule => rule.Match?.Game).Where(game => game is not null))
            {
                if (!targets.Any(target => target.GameLauncher == game!.Launcher && target.GameId == game.Id))
                    targets.Add(MappingTargetViewModel.ForGame(
                        game!.Launcher!, game.Id!, $"Missing game ({game.Launcher}:{game.Id})"));
            }
            var snapshots = rules.Select(rule => new MappingSnapshot(
                rule.Id!, rule.ApplicationGroup, rule.Match?.Game?.Launcher, rule.Match?.Game?.Id,
                profiles.Single(item => item.Id == rule.Profile).Id, rule.Eligibility!)).ToList();
            return new MappingEditorViewModel(profiles, groups, targets, snapshots, rules);
        }

        public ApplicationGroupViewModel AddApplicationGroup()
        {
            var number = ApplicationGroups.Count + 1;
            var id = $"application-group-{number}";
            while (ApplicationGroups.Any(group => group.Id == id)) id = $"application-group-{++number}";
            var group = new ApplicationGroupViewModel(id, $"Application Group {number}", []);
            ApplicationGroups.Add(group);
            AddApplicationGroupTarget(group);
            return group;
        }

        public void AddApplicationGroupTarget(ApplicationGroupViewModel group)
        {
            var target = MappingTargetViewModel.ForApplication(group);
            Targets.Insert(Math.Max(0, ApplicationGroups.Count - 1), target);
            NewTarget = target;
        }

        public void RemoveApplicationGroupTarget(string id)
        {
            var target = Targets.FirstOrDefault(item => item.ApplicationGroupId == id);
            if (target is not null) Targets.Remove(target);
            if (ReferenceEquals(NewTarget, target)) NewTarget = Targets.FirstOrDefault();
        }

        public void UpsertGameTarget(GameCatalogEntry game)
        {
            var existing = Targets.FirstOrDefault(target =>
                target.GameLauncher == game.Launcher && target.GameId == game.Id);
            var replacement = MappingTargetViewModel.ForGame(
                game.Launcher!, game.Id!, $"{game.DisplayName} ({game.Launcher})");
            if (existing is null)
            {
                Targets.Add(replacement);
                return;
            }
            ReplaceGameTarget(existing, replacement);
        }

        public void RemoveGameTarget(string launcher, string id)
        {
            var target = Targets.FirstOrDefault(item => item.GameLauncher == launcher && item.GameId == id);
            if (target is null) return;
            Targets.Remove(target);
            if (ReferenceEquals(NewTarget, target)) NewTarget = Targets.FirstOrDefault();
        }

        public void SynchronizeGameTargets(GameCatalogDocument catalog)
        {
            var known = catalog.Games!
                .Select(game => new GameIdentity(game.Launcher!, game.Id!))
                .ToHashSet();
            foreach (var game in catalog.Games!) UpsertGameTarget(game);
            foreach (var target in Targets.Where(target => target.IsGame
                && target.GameLauncher != "*"
                && !known.Contains(new GameIdentity(target.GameLauncher!, target.GameId!))).ToList())
            {
                if (Mappings.Any(mapping => mapping.SelectedTarget.GameLauncher == target.GameLauncher
                    && mapping.SelectedTarget.GameId == target.GameId))
                {
                    ReplaceGameTarget(target, MappingTargetViewModel.ForGame(
                        target.GameLauncher!, target.GameId!,
                        $"Missing game ({target.GameLauncher}:{target.GameId})"));
                }
                else
                {
                    RemoveGameTarget(target.GameLauncher!, target.GameId!);
                }
            }
        }

        public void SaveMappingAs()
        {
            if (NewTarget is null || NewProfile is null) return;
            var targetId = NewTarget.ApplicationGroupId
                ?? (NewTarget.GameLauncher == "*" && NewTarget.GameId == "*"
                    ? "any-game"
                    : $"{NewTarget.GameLauncher}-{NewTarget.GameId}");
            var baseId = NormalizeRuleId($"{targetId}-{NewProfile.Id}");
            var id = baseId;
            var suffix = 2;
            while (_sourceRules.Any(rule => rule.Id == id) || Mappings.Any(mapping => mapping.Id == id))
            {
                var ending = $"-{suffix++}";
                id = baseId[..Math.Min(baseId.Length, 64 - ending.Length)].TrimEnd('-') + ending;
            }
            var item = new MappingEditorItemViewModel(this, new MappingSnapshot(
                id, NewTarget.ApplicationGroupId, NewTarget.GameLauncher, NewTarget.GameId,
                NewProfile.Id, "while-focused"));
            Mappings.Add(item);
            UpdatePositions();
            SelectedMapping = item;
            NotifyDraftChanged();
        }

        public void BeginCreate()
        {
            if (Targets.Count == 0 || Profiles.Count == 0) return;
            _editingOriginalId = null;
            EditingMapping = new MappingEditorItemViewModel(this, new MappingSnapshot(
                "draft", Targets[0].ApplicationGroupId, Targets[0].GameLauncher, Targets[0].GameId,
                Profiles[0].Id, "while-focused"));
        }

        public void BeginEdit(MappingEditorItemViewModel item)
        {
            if (!Mappings.Contains(item)) return;
            _editingOriginalId = item.Id;
            EditingMapping = new MappingEditorItemViewModel(this, item.CreateSnapshot());
        }

        public bool CommitEditing(bool saveAs)
        {
            if (EditingMapping is not { } editing) return false;
            var snapshot = editing.CreateSnapshot();
            if (saveAs || _editingOriginalId is null)
            {
                NewTarget = editing.SelectedTarget;
                NewProfile = editing.SelectedProfile;
                SaveMappingAs();
                snapshot = snapshot with { Id = SelectedMapping!.Id };
                _editingOriginalId = snapshot.Id;
            }
            var index = Mappings.ToList().FindIndex(item => item.Id == _editingOriginalId);
            if (index < 0) return false;
            Mappings[index] = new MappingEditorItemViewModel(this, snapshot);
            EditingMapping = new MappingEditorItemViewModel(this, snapshot);
            SelectedMapping = Mappings[index];
            UpdatePositions();
            NotifyDraftChanged();
            return true;
        }

        private static string NormalizeRuleId(string value)
        {
            var normalized = new string(value.ToLowerInvariant().Select(character =>
                char.IsAsciiLetterOrDigit(character) ? character : '-').ToArray());
            while (normalized.Contains("--", StringComparison.Ordinal))
                normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
            normalized = normalized.Trim('-');
            if (normalized.Length == 0) normalized = "game-mapping";
            return normalized.Length > 56 ? normalized[..56].TrimEnd('-') : normalized;
        }

        private void ReplaceGameTarget(MappingTargetViewModel existing, MappingTargetViewModel replacement)
        {
            var index = Targets.IndexOf(existing);
            Targets[index] = replacement;
            foreach (var mapping in Mappings.Where(mapping => ReferenceEquals(mapping.SelectedTarget, existing)))
                mapping.SelectedTarget = replacement;
            if (ReferenceEquals(NewTarget, existing)) NewTarget = replacement;
        }

        public void Remove(MappingEditorItemViewModel item)
        {
            var index = Mappings.IndexOf(item);
            if (index < 0) return;
            Mappings.RemoveAt(index);
            if (_editingOriginalId == item.Id) EditingMapping = null;
            UpdatePositions();
            SelectedMapping = Mappings.Count == 0 ? null : Mappings[Math.Min(index, Mappings.Count - 1)];
            NotifyDraftChanged();
        }

        public void Apply()
        {
            EditingMapping = null;
            _appliedMappings = Mappings.Select(mapping => mapping.CreateSnapshot()).ToList();
            AppliedRevision++;
            OnPropertyChanged(nameof(AppliedMappings));
            OnPropertyChanged(nameof(AppliedRevision));
            IsDirty = false;
            UpdatePreview($"Saved {_appliedMappings.Count} Focuses.");
        }

        public List<RuleSettings> CreateRuleSettings() => Mappings.Select(mapping =>
        {
            var snapshot = mapping.CreateSnapshot();
            var source = _sourceRules.FirstOrDefault(rule => rule.Id == snapshot.Id);
            return new RuleSettings
            {
                Id = snapshot.Id,
                ApplicationGroup = snapshot.ApplicationGroupId,
                Profile = snapshot.ProfileId,
                Eligibility = snapshot.Eligibility,
                Enabled = source?.Enabled ?? true,
                Match = snapshot.GameLauncher is null ? null : new ApplicationMatch
                {
                    Game = new GameMatch
                    {
                        Launcher = snapshot.GameLauncher,
                        Id = snapshot.GameId,
                        AdditionalProperties = source?.Match?.Game?.AdditionalProperties
                    },
                    AdditionalProperties = source?.Match?.AdditionalProperties
                },
                AdditionalProperties = source?.AdditionalProperties
            };
        }).ToList();

        public void Cancel()
        {
            EditingMapping = null;
            var selectedId = SelectedMapping?.Id;
            RestoreDraft(_appliedMappings);
            SelectedMapping = Mappings.FirstOrDefault(mapping => mapping.Id == selectedId) ?? Mappings.FirstOrDefault();
            IsDirty = false;
            UpdatePreview("Draft restored to the last saved Focuses.");
        }

        public void ReportSaveFailure(string message) => PreviewStatus = message;

        public void AcceptSavedOrder()
        {
            var saved = _appliedMappings.ToDictionary(item => item.Id, StringComparer.Ordinal);
            _appliedMappings = Mappings.Select(item => saved[item.Id]).ToList();
            AppliedRevision++;
            IsDirty = false;
            OnPropertyChanged(nameof(AppliedMappings));
            OnPropertyChanged(nameof(AppliedRevision));
            OnPropertyChanged(nameof(IsDirty));
            PreviewStatus = "Focus order saved.";
        }

        public void RestoreSavedOrder()
        {
            var selected = SelectedMapping?.Id;
            RestoreDraft(_appliedMappings);
            SelectedMapping = Mappings.FirstOrDefault(item => item.Id == selected) ?? Mappings.FirstOrDefault();
            IsDirty = false;
            OnPropertyChanged(nameof(IsDirty));
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

        public bool BeginReorder(MappingEditorItemViewModel item)
        {
            if (ReorderingMapping is not null || !Mappings.Contains(item)) return false;
            _reorderOriginal = Mappings.ToArray();
            ReorderingMapping = item;
            ReorderIndex = Mappings.IndexOf(item);
            return true;
        }

        public void PreviewReorder(int index)
        {
            if (ReorderingMapping is null) return;
            ReorderIndex = Math.Clamp(index, 0, _reorderOriginal!.Length - 1);
        }

        public bool CommitReorder()
        {
            var item = ReorderingMapping;
            var index = ReorderIndex;
            var unchanged = _reorderOriginal is not null && Mappings.SequenceEqual(_reorderOriginal);
            CancelReorder();
            if (!unchanged || item is null || Mappings.IndexOf(item) == index) return false;
            Mappings.Move(Mappings.IndexOf(item), index);
            UpdatePositions();
            NotifyDraftChanged();
            return true;
        }

        public void CancelReorder()
        {
            _reorderOriginal = null;
            ReorderingMapping = null;
            ReorderIndex = -1;
        }

        public void MoveBefore(MappingEditorItemViewModel item, MappingEditorItemViewModel target) => MoveRelative(item, target, false);
        public void MoveAfter(MappingEditorItemViewModel item, MappingEditorItemViewModel target) => MoveRelative(item, target, true);
        public void MoveSelected(int offset) { if (SelectedMapping is not null) Move(SelectedMapping, offset); }
        public void MoveToTop(MappingEditorItemViewModel item) => Move(item, -Mappings.IndexOf(item));
        public void MoveToBottom(MappingEditorItemViewModel item) => Move(item, Mappings.Count - 1 - Mappings.IndexOf(item));

        internal void NotifyDraftChanged()
        {
            // Order saves automatically; only rule content and membership belong to the draft.
            IsDirty = !Mappings.Select(mapping => mapping.CreateSnapshot()).OrderBy(mapping => mapping.Id, StringComparer.Ordinal)
                .SequenceEqual(_appliedMappings.OrderBy(mapping => mapping.Id, StringComparer.Ordinal));
            OnPropertyChanged(nameof(IsDirty));
            PreviewStatus = IsDirty
                ? LivePreview ? "Draft preview updated in the editor; output is sent after Save." : "Draft changes are waiting for Save."
                : "Draft matches the last saved Focuses.";
            if (LivePreview) { PreviewRevision++; OnPropertyChanged(nameof(PreviewRevision)); }
        }

        private void MoveRelative(MappingEditorItemViewModel item, MappingEditorItemViewModel target, bool after)
        {
            var oldIndex = Mappings.IndexOf(item);
            var targetIndex = Mappings.IndexOf(target);
            if (oldIndex < 0 || targetIndex < 0 || oldIndex == targetIndex) return;
            var newIndex = oldIndex < targetIndex
                ? targetIndex - (after ? 0 : 1)
                : targetIndex + (after ? 1 : 0);
            if (oldIndex == newIndex) return;
            Mappings.Move(oldIndex, newIndex);
            UpdatePositions();
            NotifyDraftChanged();
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
            OnPropertyChanged(nameof(IsEmpty));
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
