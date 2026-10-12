namespace FocusRgb.App.ViewModels
{
    /// <summary>Captures an applied mapping independently from the editor's current draft.</summary>
    public sealed record MappingSnapshot(
        string Id,
        string ApplicationName,
        string Executable,
        string ProfileId,
        string Eligibility,
        int Priority,
        string EffectName);
}
