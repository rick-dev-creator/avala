using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Delegation.Contracts;
using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Linking;
using Avala.Workbench.ModelChoices;
using Avala.Workbench.Presenting;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Conversation;

internal static class ConversationPhrases
{
    public const string NotCopied = "The conversation could not be copied.";

    public const string BinaryChanges = "A binary file.";

    public const string NoChanges = "No changes left in this file.";

    public const string ChangesUnreadable = "The changes of this file could not be read.";

    public const string Preview = "Preview";

    public const string OpenInBrowser = "Open in your browser";

    public static string Missing(string name) => $"{name} is no longer in the workspace or the repository.";

    public static string MoreLines(int count) => count == 1 ? "… 1 more line" : $"… {count.ToString(System.Globalization.CultureInfo.InvariantCulture)} more lines";

    public const string AllChanges = "All changes to this file";

    public static string NotInWorktree(string path) => $"{path} is not in this job's worktree.";

    public static string NotText(string name) => $"{name} is not a text file.";

    public static string Reference(Avala.Workbench.Linking.ReferenceRefusal refusal) => refusal == Avala.Workbench.Linking.ReferenceRefusal.NoWebAddress
        ? "This repository has no web address to open it on."
        : "The platform refused to open it.";

    public static string Code(Avala.Workbench.Linking.CodeRefusal refusal) => refusal switch
    {
        Avala.Workbench.Linking.CodeRefusal.Cancelled => string.Empty,
        Avala.Workbench.Linking.CodeRefusal.Unwritable => "The code could not be written to a file.",
        Avala.Workbench.Linking.CodeRefusal.NoEditor => "No app on this computer opens this kind of file.",
        _ => "Saving and opening files is not available here.",
    };

    public static string NotOpened(string name) => $"{name} could not be opened.";

    public static string TooLargeToPreview(string name) => $"{name} is too large to preview.";

    public static string NotExported(FileSaveError error) => error switch
    {
        FileSaveError.Cancelled => string.Empty,
        FileSaveError.Unavailable => "There is nowhere to save the conversation right now.",
        _ => "The conversation could not be saved there.",
    };

    public static string Origin(AttemptOrigin origin) => origin switch
    {
        AttemptOrigin.Initial => "Instruction",
        AttemptOrigin.Retry => "Verification feedback",
        AttemptOrigin.Hint => "You",
        AttemptOrigin.SendBack => "Sent back",
        AttemptOrigin.Handoff => "Handed off",
        AttemptOrigin.Reopen => "Pull request feedback",
        _ => "Resumed after a restart",
    };

    public static string Origin(PromptEntry prompt) => prompt.Handoff.Match(HandoffPhrases.Moved, () => Origin(prompt.Origin));

    public static string Outcome(int attempt, AttemptOutcome outcome) =>
        string.Create(CultureInfo.InvariantCulture, $"attempt {attempt} {Went(outcome)}");

    public static string Outcome(ItemOutcome outcome, bool denied) =>
        denied && outcome != ItemOutcome.Succeeded ? "denied" : Outcome(outcome);

    public static string Outcome(ItemOutcome outcome) => outcome switch
    {
        ItemOutcome.Succeeded => "done",
        ItemOutcome.Failed => "failed",
        ItemOutcome.Cancelled => "cancelled",
        ItemOutcome.Abandoned => "abandoned",
        _ => "expired",
    };

    private static string Went(AttemptOutcome outcome) => outcome switch
    {
        AttemptOutcome.Running => "running",
        AttemptOutcome.AwaitingCheck => "awaiting its checks",
        AttemptOutcome.Passed => "passed its checks",
        AttemptOutcome.Rejected => "did not pass its checks",
        _ => "interrupted",
    };

    public static string Turn(TurnOutcome outcome, TimeSpan duration) => outcome switch
    {
        TurnOutcome.Finished => string.Create(CultureInfo.InvariantCulture, $"Worked for {Duration(duration)}"),
        TurnOutcome.Interrupted => string.Create(CultureInfo.InvariantCulture, $"Interrupted after {Duration(duration)}"),
        TurnOutcome.LimitReached => string.Create(CultureInfo.InvariantCulture, $"Ran out of its usage window after {Duration(duration)}"),
        _ => string.Create(CultureInfo.InvariantCulture, $"Failed after {Duration(duration)}"),
    };

    public static string Placeholder(JobStatus status, bool takesMessagesMidTurn, Option<PullRequestWatchState> watch, Option<HoldReason> hold) => status switch
    {
        JobStatus.Approved => watch.Bind(Watched).Match(phrase => phrase, () => Placeholder(status, takesMessagesMidTurn)),
        JobStatus.NeedsHelp => hold.Match(HeldPlaceholder, () => Placeholder(status, takesMessagesMidTurn)),
        _ => Placeholder(status, takesMessagesMidTurn),
    };

    private static string HeldPlaceholder(HoldReason reason) =>
        reason == HoldReason.CouldNotStart
            ? "Could not start · retry it from the inspector's worktree section"
            : $"Held · {Held(reason)} · send a message to continue the job…";

    private static Option<string> Watched(PullRequestWatchState state) => state.Status switch
    {
        WatchStatus.Ended => Option<string>.None,
        WatchStatus.NeedsPerson => string.Create(CultureInfo.InvariantCulture, $"Approved · Avala stopped waking the agent: pull request #{state.PullRequest.Number} needs you"),
        _ => state.Policy switch
        {
            OnPullRequest.WakeOnCi => string.Create(CultureInfo.InvariantCulture, $"Approved · Avala watches pull request #{state.PullRequest.Number} and wakes the agent if CI fails or it conflicts"),
            OnPullRequest.WakeOnCiAndReviews => string.Create(CultureInfo.InvariantCulture, $"Approved · Avala watches pull request #{state.PullRequest.Number} and wakes the agent if CI fails, it conflicts or a review asks for changes"),
            _ => string.Create(CultureInfo.InvariantCulture, $"Approved · Avala watches pull request #{state.PullRequest.Number} until it is merged or closed"),
        },
    };

    private static string Placeholder(JobStatus status, bool takesMessagesMidTurn) => status switch
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
        JobStatus.Running when takesMessagesMidTurn => "Send into the running turn",
        JobStatus.Draft or JobStatus.Preparing or JobStatus.Running or JobStatus.Checking => "Queue for when the agent stops",
        _ => "Send",
    };

    public static string Pill(BoardJob job) => job.Group switch
    {
        JobGroup.NeedsYou or JobGroup.Waiting => job.Hold.Match(
            reason => job.PendingDecisions == 0 ? $"Held · {job.Wait.Match(HandoffPhrases.Until, () => Held(reason))}" : "Needs you",
            () => job.Wait.Match(wait => $"Held · {HandoffPhrases.Until(wait)}", () => "Needs you")),
        JobGroup.ReadyForReview => "Ready for review",
        JobGroup.Done => job.Status switch
        {
            JobStatus.Approved => "Approved",
            JobStatus.Discarded => "Discarded",
            _ => "Failed",
        },
        _ => job.Status switch
        {
            _ when job.Fact.Kind == FactKind.WaitsForParent => "Waiting for its parent",
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

    public static string Delegated(DelegationRecord delegation) =>
        delegation.Refusal.Match(
            refusal => $"Delegation refused · {DelegationPhrases.Refusal(refusal)}",
            () => $"Delegated to {FactPhrases.Title(delegation.Instruction)} · {delegation.Report.Match(report => DelegationPhrases.Outcome(report.Outcome), () => "job started")}");

    public static string Waited(Option<DelegationRecord> awaited, bool ended) =>
        awaited.Match(
            delegation => ended
                ? $"Waited for {FactPhrases.Title(delegation.Instruction)}{delegation.Report.Match(report => $" · {DelegationPhrases.Outcome(report.Outcome)}", () => string.Empty)}"
                : $"Waiting for {FactPhrases.Title(delegation.Instruction)}…",
            () => ended ? "Waited for a sub-agent" : "Waiting for a sub-agent…");

    public static string Drafting(string title, int received) =>
        $"{Verb(title)} · {Size(received)}";

    public static string Size(int characters) =>
        characters < 1024 ? string.Create(CultureInfo.InvariantCulture, $"{characters} B")
        : characters < 1024 * 1024 ? string.Create(CultureInfo.InvariantCulture, $"{characters / 1024.0:0.0} KB")
        : string.Create(CultureInfo.InvariantCulture, $"{characters / (1024.0 * 1024):0.0} MB");

    private static string Verb(string title) =>
        title.StartsWith("Write ", StringComparison.Ordinal) ? $"Writing {title["Write ".Length..]}"
        : title.StartsWith("Edit ", StringComparison.Ordinal) ? $"Editing {title["Edit ".Length..]}"
        : title;

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
        JobRejection.NeverStarted => "The job never started, so it takes no message: retry it from the inspector's worktree section.",
        _ => "The job refused the command.",
    };

    private static string Held(HoldReason reason) => reason switch
    {
        HoldReason.Stalled => "stalled",
        HoldReason.SessionLost => "session lost",
        HoldReason.BudgetExceeded => "over budget",
        HoldReason.LimitNearlyReached => "near its limit",
        HoldReason.InvalidBudget => "invalid budget",
        HoldReason.InvalidChecks => "invalid checks",
        HoldReason.MemoryExceeded => "out of memory",
        HoldReason.Stopped => "stopped",
        HoldReason.NotResumable => "its conversation cannot resume",
        HoldReason.CouldNotStart => "could not start",
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
