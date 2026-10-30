namespace FocusRgb.App.ViewModels
{
    /// <summary>One provider resource shown by the profile editor.</summary>
    public sealed class ProviderResourceOptionViewModel
    {
        public required string? ResourceId { get; init; }
        public required string DisplayName { get; init; }
        public required bool IsAvailable { get; init; }
        public string? UnavailableReason { get; init; }
        public string DisplayLabel => IsAvailable
            ? DisplayName
            : $"{DisplayName} (unavailable: {UnavailableReason ?? "not reported by the provider"})";
    }
}
