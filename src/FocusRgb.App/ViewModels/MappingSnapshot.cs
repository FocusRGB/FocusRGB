namespace FocusRgb.App.ViewModels
{
    /// <summary>Captures an applied mapping independently from the editor's current draft.</summary>
    public sealed record MappingSnapshot(
        string Id,
        string ApplicationName,
        string Executables,
        string ProfileId,
        string Eligibility,
        int Priority);
}
