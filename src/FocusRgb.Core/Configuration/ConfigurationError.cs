namespace FocusRgb.Core.Configuration
{
    /// <summary>Names an invalid JSON field so an editor can explain and locate the problem.</summary>
    public sealed class ConfigurationError
    {
        public required string Path { get; init; }
        public required string Message { get; init; }
    }

    /// <summary>Rejects invalid settings without exposing a partially validated document.</summary>
    public sealed class SettingsLoadResult
    {
        public bool IsSuccess => Settings is not null;
        public SettingsDocument? Settings { get; private init; }
        public IReadOnlyList<ConfigurationError> Errors { get; private init; } = [];

        public static SettingsLoadResult Ok(SettingsDocument settings) => new() { Settings = settings };
        public static SettingsLoadResult Fail(IReadOnlyList<ConfigurationError> errors) => new() { Errors = errors };
    }
}
