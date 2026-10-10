using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Linking;
using Avala.Workbench.ModelChoices;
using Avala.Workbench.Presenting;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Conversation;

internal static class ConversationPhrases
{
    public static string Origin(AttemptOrigin origin) => origin switch
    {
        AttemptOrigin.Initial => "Instruction",
        AttemptOrigin.Retry => "Verification feedback",
        AttemptOrigin.Hint => "You",
        AttemptOrigin.SendBack => "Sent back",
        AttemptOrigin.Handoff => "Handed off",
        _ => "Resumed after a restart",
    };

    public static string Origin(PromptEntry prompt) => prompt.Handoff.Match(HandoffPhrases.Moved, () => Origin(prompt.Origin));

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

    public static string Placeholder(JobStatus status, bool takesMessagesMidTurn) => status switch
    {
        JobStatus.Running when takesMessagesMidTurn => "Message the agent while it works…",
        JobStatus.Running => "This agent takes no message mid-turn · queue one for when it stops…",
        JobStatus.NeedsHelp => "Continue the job with a message…",
        JobStatus.AwaitingReview => "Send the agent more to do before you review…",
        JobStatus.Checking => "The checks are running · queue a message for when they end…",
        JobStatus.Draft or JobStatus.Preparing => "The agent is starting · queue a message for when it stops…",
        _ => "This job has ended",
    };

    public static string QueuedCaption(JobStatus status) =>
        status == JobStatus.AwaitingReview
            ? "Queued · waits in the review: send back with it, or withdraw it"
            : "Queued · continues the job when it next needs you; at review, you decide";

    public static string SendHint(JobStatus status, bool takesMessagesMidTurn) => status switch
    {
        JobStatus.Running when takesMessagesMidTurn => "Send into the running turn (Ctrl+Enter)",
        JobStatus.Draft or JobStatus.Preparing or JobStatus.Running or JobStatus.Checking => "Queue for when the agent stops (Ctrl+Enter)",
        _ => "Send (Ctrl+Enter)",
    };

    public static string Pill(BoardJob job) => job.Group switch
    {
        JobGroup.NeedsYou => job.Hold.Match(
            reason => job.PendingDecisions == 0 ? $"Held · {job.Wait.Match(HandoffPhrases.Waits, () => Held(reason))}" : "Needs you",
            () => job.Wait.Match(wait => $"Held · {HandoffPhrases.Waits(wait)}", () => "Needs you")),
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

    public static string Place(JobSummary job) => Place(job, Option<ModelReported>.None);

    public static string Place(JobSummary job, Option<ModelReported> ran)
    {
        var repository = job.Repository.TrimEnd('/', '\\');
        var name = repository[(repository.LastIndexOfAny(['/', '\\']) + 1)..];
        var place = job.Connection.Match(connection => $"{name} · {connection.Value}", () => name);

        return ran.Match(reported => $"{place} · {ModelPhrases.Ran(reported)}", () => place);
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
        JobRejection.NotSteerable => "This agent takes no message while it works.",
        JobRejection.NotApproved => "The job is not approved, so it cannot be reopened.",
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

    public static string Link(LinkRefusal refusal, string link) => refusal switch
    {
        LinkRefusal.NotAWebLink => $"Avala opens only web links, so {link} was not opened.",
        LinkRefusal.Unavailable => $"No browser is available to open {link}.",
        _ => $"The platform refused to open {link}.",
    };

    private static string Duration(TimeSpan duration) =>
        duration.TotalSeconds < 60
            ? string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, (int)Math.Round(duration.TotalSeconds))}s")
            : string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalMinutes}m {duration.Seconds}s");
}
