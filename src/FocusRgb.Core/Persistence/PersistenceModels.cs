using FocusRgb.Core.Configuration;
using FocusRgb.Core.Selection;

namespace FocusRgb.Core.Persistence
{
    /// <summary>Reports a persistence failure without replacing the last valid file.</summary>
    public sealed class PersistenceError
    {
        public required string FileName { get; init; }
        public required string Message { get; init; }
    }

    /// <summary>Reports whether one validated document was saved atomically.</summary>
    public sealed class SaveResult
    {
        public bool IsSuccess => Error is null;
        public PersistenceError? Error { get; private init; }

        public static SaveResult Ok() => new();
        public static SaveResult Fail(PersistenceError error) => new() { Error = error };
    }

    /// <summary>Identifies an invalid file retained during explicit startup recovery.</summary>
    public sealed class RecoveryNotice
    {
        public required string FileName { get; init; }
        public required string ArchivedPath { get; init; }
        public required string Message { get; init; }
    }

    /// <summary>Collects validated startup documents and restored desired selection.</summary>
    public sealed class StartupSnapshot
    {
        public required SettingsDocument Settings { get; init; }
        public required SelectionStateDocument State { get; init; }
        public required NativeSetupDocument NativeSetup { get; init; }
        public required SelectionDecision Desired { get; init; }
        public IReadOnlyList<RecoveryNotice> Recoveries { get; init; } = [];
    }

    /// <summary>Returns either a complete startup snapshot or one actionable persistence error.</summary>
    public sealed class StartupResult
    {
        public bool IsSuccess => Snapshot is not null;
        public StartupSnapshot? Snapshot { get; private init; }
        public PersistenceError? Error { get; private init; }

        public static StartupResult Ok(StartupSnapshot snapshot) => new() { Snapshot = snapshot };
        public static StartupResult Fail(PersistenceError error) => new() { Error = error };
    }
}
