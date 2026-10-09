using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Workbench.Board;

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

    public static string Placeholder(JobStatus status) => status switch
    {
        JobStatus.Running => "The agent is working · interrupt it to step in",
        JobStatus.NeedsHelp => "Continue the job with a message…",
        JobStatus.AwaitingReview => "Send the agent more to do before you review…",
        JobStatus.Checking => "The checks are running…",
        JobStatus.Draft or JobStatus.Preparing => "The agent is starting…",
        _ => "This job has ended",
    };

    public static string Pill(BoardJob job) => job.Group switch
    {
        JobGroup.NeedsYou => job.Hold.Match(reason => job.PendingDecisions == 0 ? $"Held · {Held(reason)}" : "Needs you", () => "Needs you"),
        JobGroup.ReadyForReview => "Ready for review",
        JobGroup.Done => job.Status switch
        {
            JobStatus.Approved => "Approved",
            JobStatus.Discarded => "Discarded",
            _ => "Failed",
        },
        _ => job.Status switch
        {
            JobStatus.Checking => "Checking",
            JobStatus.Draft or JobStatus.Preparing => "Starting",
            _ => "Running",
        },
    };

    public static string Place(JobSummary job)
    {
        var repository = job.Repository.TrimEnd('/', '\\');
        var name = repository[(repository.LastIndexOfAny(['/', '\\']) + 1)..];

        return job.Connection.Match(connection => $"{name} · {connection.Value}", () => name);
    }

    public static string Thought(TimeSpan duration) =>
        string.Create(CultureInfo.InvariantCulture, $"Thought for {Duration(duration)}");

    public static string ThoughtUnshared(TimeSpan duration) => $"{Thought(duration)} · content not shared by the harness";

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

    private static string Held(HoldReason reason) => reason switch
    {
        HoldReason.Stalled => "stalled",
        HoldReason.SessionLost => "session lost",
        HoldReason.BudgetExceeded => "over budget",
        HoldReason.LimitNearlyReached => "near its limit",
        HoldReason.InvalidBudget => "invalid budget",
        HoldReason.MemoryExceeded => "out of memory",
        HoldReason.Stopped => "stopped",
        HoldReason.NotResumable => "its conversation cannot resume",
        _ => "interrupted",
    };

    private static string Duration(TimeSpan duration) =>
        duration.TotalSeconds < 60
            ? string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, (int)Math.Round(duration.TotalSeconds))}s")
            : string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalMinutes}m {duration.Seconds}s");
}
