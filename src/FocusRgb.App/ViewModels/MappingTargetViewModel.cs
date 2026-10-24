using System.ComponentModel;

namespace FocusRgb.App.ViewModels
{
    /// <summary>Projects an application group, any known game, or one launcher-scoped game.</summary>
    public sealed class MappingTargetViewModel : INotifyPropertyChanged
    {
        private readonly ApplicationGroupViewModel? _applicationGroup;
        private readonly string _displayName;

        private MappingTargetViewModel(
            string displayName,
            ApplicationGroupViewModel? applicationGroup,
            string? gameLauncher,
            string? gameId)
        {
            _displayName = displayName;
            _applicationGroup = applicationGroup;
            GameLauncher = gameLauncher;
            GameId = gameId;
            if (applicationGroup is not null)
                applicationGroup.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(ApplicationGroupViewModel.DisplayName))
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
                };
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public string DisplayName => _applicationGroup?.DisplayName ?? _displayName;
        public string? ApplicationGroupId => _applicationGroup?.Id;
        public ApplicationGroupViewModel? ApplicationGroup => _applicationGroup;
        public string? GameLauncher { get; }
        public string? GameId { get; }
        public bool IsGame => GameLauncher is not null;

        public static MappingTargetViewModel ForApplication(ApplicationGroupViewModel group) =>
            new(group.DisplayName, group, null, null);

        public static MappingTargetViewModel ForGame(string launcher, string id, string displayName) =>
            new(displayName, null, launcher, id);

        public override string ToString() => DisplayName;
    }
}
