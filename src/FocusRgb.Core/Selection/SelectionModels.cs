namespace FocusRgb.Core.Selection
{
    /// <summary>Chooses whether a user-selected profile or application rules control selection.</summary>
    public enum SelectionMode
    {
        Manual,
        Automatic
    }

    /// <summary>Supplies one platform observation without coupling the core to process APIs.</summary>
    public sealed class ApplicationObservation
    {
        public IReadOnlyCollection<string> RunningExecutables { get; init; } = [];
        public string? ForegroundExecutable { get; init; }
    }

    /// <summary>Explains why the selector chose a profile so the UI can report the decision.</summary>
    public sealed class SelectionReason
    {
        public required string Kind { get; init; }
        public string? RuleId { get; init; }

        public static SelectionReason Manual() => new() { Kind = "manual" };

        public static SelectionReason Rule(string ruleId) => new()
        {
            Kind = "rule",
            RuleId = ruleId
        };

        public static SelectionReason UnmatchedDefault() => new() { Kind = "unmatched-default" };

        public static SelectionReason FirstRun() => new() { Kind = "first-run" };

        public static SelectionReason Restored() => new() { Kind = "restored" };

        public static SelectionReason InvalidStateReset() => new() { Kind = "invalid-state-reset" };
    }

    /// <summary>Returns the desired profile and the reason independently of any output provider.</summary>
    public sealed class SelectionDecision
    {
        public required string ProfileId { get; init; }
        public required SelectionReason Reason { get; init; }
    }
}
