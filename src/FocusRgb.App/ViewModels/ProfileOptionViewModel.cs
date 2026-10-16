namespace FocusRgb.App.ViewModels
{
    /// <summary>Provides the small profile projection needed by the settings shell.</summary>
    public sealed class ProfileOptionViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        private string _displayName = "";
        private string _effectName = "";
        private string _layoutName = "";

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public override string ToString() => DisplayName;

        public required string Id { get; init; }
        public required string DisplayName { get => _displayName; set => Set(ref _displayName, value, nameof(DisplayName)); }
        public required string EffectName { get => _effectName; set => Set(ref _effectName, value, nameof(EffectName)); }
        public required string LayoutName { get => _layoutName; set => Set(ref _layoutName, value, nameof(LayoutName)); }

        private void Set(ref string field, string value, string propertyName)
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }
}
