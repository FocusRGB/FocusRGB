namespace FocusRgb.Core.Commands
{
    /// <summary>Describes a temporary output override; persisted selection remains authoritative.</summary>
    public sealed record ProfilePreview(string Id, string ProfileId, DateTimeOffset ExpiresAt, string Source);
}
