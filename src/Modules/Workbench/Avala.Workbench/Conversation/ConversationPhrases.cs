using System.Collections.Frozen;
using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Drafting;
using Avala.Workbench.Linking;
using Avala.Workbench.Steering;
using Avala.Workbench.ModelChoices;
using Avala.Workbench.Presenting;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Conversation;

internal static class ConversationPhrases
{
    private static readonly FrozenDictionary<HoldReason, string> Holds = new Dictionary<HoldReason, string>
    {
        [HoldReason.Stalled] = "stalled",
        [HoldReason.SessionLost] = "session lost",
        [HoldReason.BudgetExceeded] = "over budget",
        [HoldReason.LimitNearlyReached] = "near its limit",
        [HoldReason.InvalidBudget] = "invalid budget",
        [HoldReason.InvalidChecks] = "invalid checks",
        [HoldReason.MemoryExceeded] = "out of memory",
        [HoldReason.Stopped] = "stopped",
        [HoldReason.NotResumable] = "its conversation cannot resume",
        [HoldReason.CouldNotStart] = "could not start",
        [HoldReason.TurnFailed] = "its agent's turn failed",
    }.ToFrozenDictionary();

    public const string NotCopied = "The conversation could not be copied.";

    public const string BinaryChanges = "A binary file.";

    public const string NoChanges = "No changes left in this file.";

    public const string ChangesUnreadable = "The changes of this file could not be read.";

    public const string Preview = "Preview";

    public const string OpenInBrowser = "Open in your browser";

    public const string ShowInPreview = "Show in the preview";

    public const string PageKind = "Page";

    public const string ModelKind = "3D model";

    public static string OpenTheJob(string name) => $"Open this job's conversation to see {name}.";

    public static string Missing(string name) => $"{name} is no longer in the workspace or the repository.";

    public static string MoreLines(int count) => count == 1 ? "… 1 more line" : $"… {count.ToString(System.Globalization.CultureInfo.InvariantCulture)} more lines";

    public const string AllChanges = "All changes to this file";

    public static string Cold(ColdConversation cold) =>
        $"Idle for {Idle(cold.Idle)}, so the next message re-reads about {Thousands(cold.Tokens)} tokens of this conversation.";

    private static string Idle(TimeSpan idle) =>
        (int)idle.TotalHours switch
        {
            0 => $"{Math.Max(1, (int)idle.TotalMinutes).ToString(System.Globalization.CultureInfo.InvariantCulture)} minutes",
            1 => "over an hour",
            var hours => $"over {hours.ToString(System.Globalization.CultureInfo.InvariantCulture)} hours",
        };

    private static string Thousands(long tokens) =>
        tokens >= 1_000 ? $"{Math.Round(tokens / 1_000d).ToString(System.Globalization.CultureInfo.InvariantCulture)}k" : tokens.ToString(System.Globalization.CultureInfo.InvariantCulture);

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
        AttemptOrigin.Device => "From your phone",
        AttemptOrigin.Woken => "The agent went on by itself",
        _ => "Resumed after a restart",
    };

    public static string Origin(PromptEntry prompt) =>
        prompt.Handoff.Match(HandoffPhrases.Moved, () => prompt.Author == MessageAuthor.Avala && prompt.Origin is AttemptOrigin.Hint or AttemptOrigin.SendBack ? "Avala" : Origin(prompt.Origin));

    public static string Outcome(int attempt, AttemptOutcome outcome) =>
        string.Create(CultureInfo.InvariantCulture, $"attempt {attempt} {Went(outcome)}");

    public static string Outcome(ItemKind kind, ItemOutcome outcome, bool denied) =>
        (kind, outcome, denied) switch
        {
            (ItemKind.Subagent, ItemOutcome.Abandoned, _) => "lost",
            (ItemKind.Subagent, ItemOutcome.Cancelled or ItemOutcome.Expired, false) => "stopped",
            _ => Outcome(outcome, denied),
        };

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

    public static bool Stopped(ItemKind kind, Option<ItemOutcome> outcome) =>
        kind == ItemKind.Subagent && outcome.Match(found => found is ItemOutcome.Cancelled or ItemOutcome.Expired, () => false);

    public static string Tools(IEnumerable<ItemKind> kinds) =>
        string.Join(", ", kinds.GroupBy(Tool).Select(kind => Counted(kind.Count(), kind.Key)));

    private static (string One, string Many) Tool(ItemKind kind) => kind switch
    {
        ItemKind.Command => ("command", "commands"),
        ItemKind.FileEdit => ("edit", "edits"),
        ItemKind.Search => ("search", "searches"),
        ItemKind.Web => ("web lookup", "web lookups"),
        ItemKind.Mcp => ("MCP call", "MCP calls"),
        _ => ("other tool", "other tools"),
    };

    private static string Counted(int count, (string One, string Many) noun) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? noun.One : noun.Many)}");

    private static string Went(AttemptOutcome outcome) => outcome switch
    {
        AttemptOutcome.Running => "running",
        AttemptOutcome.AwaitingCheck => "awaiting its checks",
        AttemptOutcome.Passed => "passed its checks",
        AttemptOutcome.Rejected => "did not pass its checks",
        _ => "interrupted",
    };

    public static bool Reported(TokenUsage tokens) => tokens != default;

    public static string Checkpoint(int files) => files switch
    {
        0 => "Checkpoint · no file changed",
        1 => "Checkpoint · 1 file changed",
        _ => string.Create(CultureInfo.InvariantCulture, $"Checkpoint · {files} files changed"),
    };

    public static string Change(Avala.Workspaces.Contracts.FileChange change) =>
        change.Kind switch
        {
            Avala.Workspaces.Contracts.ChangeKind.Added => $"Added {change.Path}",
            Avala.Workspaces.Contracts.ChangeKind.Deleted => $"Deleted {change.Path}",
            _ => $"Changed {change.Path}",
        };

    public static string Restored(CheckpointRestored restored) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"I restored the worktree to how it was at the end of attempt {restored.Attempt}. {(restored.Files.Count == 0 ? "No file changed." : $"These files changed back: {string.Join(", ", restored.Files.Select(file => file.Path))}.")} Continue from there.");

    public static string Breakdown(TokenUsage tokens, Option<string> model, IReadOnlyList<Cost> costs)
    {
        if (!Reported(tokens) && costs.Count == 0)
        {
            return "The harness reported no usage for this turn.";
        }

        var lines = new[]
        {
            ("Input", tokens.Input),
            ("Output", tokens.Output),
            ("Cache read", tokens.CacheRead),
            ("Cache write", tokens.CacheWrite),
            ("Reasoning", tokens.Reasoning),
        }
            .Where(line => line.Item2 > 0)
            .Select(line => string.Create(CultureInfo.InvariantCulture, $"{line.Item1}: {line.Item2:N0} tokens"))
            .Concat(model.Match<string[]>(name => [$"Model: {name}"], () => []))
            .Concat(costs.Count > 0 ? [$"Cost of the whole turn: {Presenting.Amounts.Costs(costs)}"] : []);

        return string.Join("\n", lines);
    }

    public const string WokenForChecks = "Avala woke the agent to fix them";

    public static string Ci(CiEntry ci)
    {
        var failing = ci.Checks.Where(check => check.Status == CheckStatus.Failed).ToList();

        return failing.Count switch
        {
            0 => string.Create(CultureInfo.InvariantCulture, $"{(ci.Checks.Count == 1 ? "The check" : $"All {ci.Checks.Count} checks")} of pull request #{ci.PullRequest} passed"),
            1 => $"CI failed: {failing[0].Name}",
            _ => string.Create(CultureInfo.InvariantCulture, $"CI failed: {failing.Count} checks"),
        };
    }

    public static string FixChecks(CiEntry ci)
    {
        var lines = ci.Checks
            .Where(check => check.Status == CheckStatus.Failed)
            .Select(check => $"- {check.Name}{(check.Summary.Length > 0 ? $": {check.Summary}" : string.Empty)}{check.Link.Match(link => $" ({link})", () => string.Empty)}");

        return string.Create(CultureInfo.InvariantCulture, $"The checks of pull request #{ci.PullRequest} failed on {ci.Head[..Math.Min(7, ci.Head.Length)]}:\n{string.Join("\n", lines)}\nFix what makes them fail.");
    }

    public static string Lost(IReadOnlyList<string> titles) => Lost("The turn ended", titles);

    public static string Lost(string when, IReadOnlyList<string> titles) =>
        titles.Count == 0
            ? string.Empty
            : $"{when} while {string.Join(", ", titles.Select(title => $"“{title}”"))} still worked in the background, so {(titles.Count == 1 ? "its" : "their")} work is lost";

    public static string Turn(TurnOutcome outcome, TimeSpan duration) => outcome switch
    {
        TurnOutcome.Finished => string.Create(CultureInfo.InvariantCulture, $"Worked for {Duration(duration)}"),
        TurnOutcome.Interrupted => string.Create(CultureInfo.InvariantCulture, $"Interrupted after {Duration(duration)}"),
        TurnOutcome.LimitReached => string.Create(CultureInfo.InvariantCulture, $"Ran out of its usage window after {Duration(duration)}"),
        _ => string.Create(CultureInfo.InvariantCulture, $"Failed after {Duration(duration)}"),
    };

    public static string Placeholder(JobStatus status, bool takesMessagesMidTurn, Option<PullRequestWatchState> watch, Option<HoldReason> hold, Option<JobFailure> failure) => status switch
    {
        JobStatus.Approved => watch.Bind(Watched).Match(phrase => phrase, () => Placeholder(status, takesMessagesMidTurn)),
        JobStatus.NeedsHelp => hold.Match(HeldPlaceholder, () => Placeholder(status, takesMessagesMidTurn)),
        JobStatus.Failed => failure.Match(reason => $"This job failed · {Failed(reason)}", () => Placeholder(status, takesMessagesMidTurn)),
        _ => Placeholder(status, takesMessagesMidTurn),
    };

    public static string Failed(JobFailure failure) => failure switch
    {
        JobFailure.WorkspaceUnavailable => "its worktree could not be prepared or checkpointed",
        JobFailure.ConnectionUnavailable => "no connection could run it: check its connection in Settings and the repository's .avala/jobs.json",
        JobFailure.ModelUnavailable => "its connection does not offer the model or effort it asked for",
        JobFailure.AgentFailed => "its agent's turn failed",
        _ => "its agent could not take the message",
    };

    public static Option<string> Closed(BoardJob job) =>
        job.Status != JobStatus.Approved || job.TakesMessages ? Option<string>.None
        : job.Summary.Parent.IsSome ? "Approved · its work is in its parent"
        : "Approved · its worktree was removed, so it takes no more messages";

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

    public const string ChoosingChecks = "Avala proposes checks for this job · use or skip them above the conversation…";

    public const string FindingChecks = "Avala is finding how to check the work · queue a message for when the checks end…";

    private static string Placeholder(JobStatus status, bool takesMessagesMidTurn) => status switch
    {
        JobStatus.Running when takesMessagesMidTurn => "Message the agent while it works…",
        JobStatus.Running => "This agent takes no message mid-turn · queue one for when it stops…",
        JobStatus.NeedsHelp => "Continue the job with a message…",
        JobStatus.AwaitingReview => "Send the agent more to do before you review…",
        JobStatus.Checking => "The checks are running · queue a message for when they end…",
        JobStatus.Draft or JobStatus.Preparing => "The agent is starting · queue a message for when it stops…",
        JobStatus.Discarded => "This job was discarded",
        JobStatus.Failed => "This job failed",
        _ => "Approved · send a message to continue the job…",
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
        JobGroup.Delivered => "Delivered",
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

    public static string Undelivered(Option<UndeliveredMessage> why) =>
        why.Match(
            message => message.Status == JobStatus.Discarded
                ? "Your queued message was not delivered, so it is back here: the job was discarded."
                : message.Rejection.Match(
                    rejection => $"Your queued message was not delivered, so it is back here. {Rejection(rejection)}",
                    () => "Your queued message was not delivered, so it is back here: the job failed."),
            () => "Your queued message was not delivered, so it is back here: Avala closed before the job could take it.");

    public static string Rejection(JobRejection rejection) => rejection switch
    {
        JobRejection.NotHeld => "The job takes a message only when it needs you, awaits review or was approved.",
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
        JobRejection.NotRoot => "This sub-agent's work is already in its parent, so it takes no more messages.",
        JobRejection.NeverStarted => "The job never started, so it takes no message: retry it from the inspector's worktree section.",
        _ => "The job refused the command.",
    };

    private static string Held(HoldReason reason) => Holds.GetValueOrDefault(reason, "interrupted");

    public static string Link(LinkRefusal refusal, string link) => refusal switch
    {
        LinkRefusal.NotAWebLink => $"Avala opens only web links, so {link} was not opened.",
        LinkRefusal.Unavailable => $"No browser is available to open {link}.",
        _ => $"The platform refused to open {link}.",
    };

    public static string Duration(TimeSpan duration) =>
        duration.TotalSeconds < 60
            ? string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, (int)Math.Round(duration.TotalSeconds))}s")
            : string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalMinutes}m {duration.Seconds}s");
}
