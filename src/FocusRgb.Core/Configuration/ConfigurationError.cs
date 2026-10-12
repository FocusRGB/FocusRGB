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

    /// <summary>Rejects invalid selection intent without returning a partial state document.</summary>
    public sealed class StateLoadResult
    {
        public bool IsSuccess => State is not null;
        public SelectionStateDocument? State { get; private init; }
        public IReadOnlyList<ConfigurationError> Errors { get; private init; } = [];

        public static StateLoadResult Ok(SelectionStateDocument state) => new() { State = state };
        public static StateLoadResult Fail(IReadOnlyList<ConfigurationError> errors) => new() { Errors = errors };
    }

    /// <summary>Rejects an invalid native fallback snapshot without exposing partial bindings.</summary>
    public sealed class NativeSetupLoadResult
    {
        public bool IsSuccess => NativeSetup is not null;
        public NativeSetupDocument? NativeSetup { get; private init; }
        public IReadOnlyList<ConfigurationError> Errors { get; private init; } = [];

        public static NativeSetupLoadResult Ok(NativeSetupDocument nativeSetup) => new()
        {
            NativeSetup = nativeSetup
        };

        public static NativeSetupLoadResult Fail(IReadOnlyList<ConfigurationError> errors) => new()
        {
            Errors = errors
        };
    }
}
