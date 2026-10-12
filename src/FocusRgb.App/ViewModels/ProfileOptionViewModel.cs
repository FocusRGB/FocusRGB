namespace FocusRgb.App.ViewModels
{
    /// <summary>Provides the small profile projection needed by the settings shell.</summary>
    public sealed class ProfileOptionViewModel
    {
        public required string Id { get; init; }
        public required string DisplayName { get; init; }
        public required string EffectName { get; init; }
        public required string LayoutName { get; init; }
    }
}
