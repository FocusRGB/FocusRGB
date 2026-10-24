namespace FocusRgb.App.ViewModels
{
    /// <summary>Captures an applied mapping independently from the editor's current draft.</summary>
    public sealed record MappingSnapshot(
        string Id,
        string? ApplicationGroupId,
        string? GameLauncher,
        string? GameId,
        string ProfileId,
        string Eligibility);
}
