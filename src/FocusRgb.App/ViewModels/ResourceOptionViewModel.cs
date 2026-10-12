namespace FocusRgb.App.ViewModels
{
    /// <summary>Represents one resource returned by the preview capability catalog.</summary>
    public sealed class ResourceOptionViewModel
    {
        public required string DisplayName { get; init; }
        public bool IsAvailable { get; init; }
    }
}
