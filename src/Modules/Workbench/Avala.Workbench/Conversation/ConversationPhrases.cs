using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;

namespace Avala.Workbench.Conversation;

internal static class ConversationPhrases
{
    public static string Origin(AttemptOrigin origin) => origin switch
    {
        AttemptOrigin.Initial => "Instruction",
        AttemptOrigin.Retry => "Verification feedback",
        AttemptOrigin.Hint => "You",
        AttemptOrigin.SendBack => "Sent back",
        _ => "Resumed after a restart",
    };

    public static string Outcome(AttemptOutcome outcome) => outcome switch
    {
        AttemptOutcome.Running => "running",
        AttemptOutcome.AwaitingCheck => "awaiting its checks",
        AttemptOutcome.Passed => "passed",
        AttemptOutcome.Rejected => "rejected",
        _ => "interrupted",
    };

    public static string Outcome(ItemOutcome outcome) => outcome switch
    {
        ItemOutcome.Succeeded => "done",
        ItemOutcome.Failed => "failed",
        ItemOutcome.Cancelled => "cancelled",
        ItemOutcome.Abandoned => "abandoned",
        _ => "expired",
    };

    public static string Turn(TurnOutcome outcome, TimeSpan duration) => outcome switch
    {
        TurnOutcome.Finished => string.Create(CultureInfo.InvariantCulture, $"Worked for {Duration(duration)}"),
        TurnOutcome.Interrupted => string.Create(CultureInfo.InvariantCulture, $"Interrupted after {Duration(duration)}"),
        _ => string.Create(CultureInfo.InvariantCulture, $"Failed after {Duration(duration)}"),
    };

    public static string Thought(TimeSpan duration) =>
        string.Create(CultureInfo.InvariantCulture, $"Thought for {Duration(duration)}");

    public static string Rejection(JobRejection rejection) => rejection switch
    {
        JobRejection.NotHeld => "The job takes a message only when it needs you or awaits review.",
        JobRejection.NotRunning => "The job is not running.",
        JobRejection.NotDiscardable => "The job already ended.",
        JobRejection.EmptyMessage => "Write a message first.",
        JobRejection.UnknownJob => "The job no longer exists.",
        JobRejection.WorkspaceUnavailable => "The job's worktree is gone.",
        JobRejection.UnknownConnection or JobRejection.UnusableConnection => "The job's connection cannot be used.",
        JobRejection.AgentUnavailable => "The agent could not start.",
        JobRejection.NotAwaitingReview => "The job no longer awaits review.",
        _ => "The job refused the command.",
    };

    private static string Duration(TimeSpan duration) =>
        duration.TotalSeconds < 60
            ? string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, (int)Math.Round(duration.TotalSeconds))}s")
            : string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalMinutes}m {duration.Seconds}s");
}
