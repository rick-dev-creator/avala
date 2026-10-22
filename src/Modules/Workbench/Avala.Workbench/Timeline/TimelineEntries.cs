using System.Collections.Immutable;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Canvas.Contracts;
using Avala.Delegation.Contracts;
using Avala.Documentation.Contracts;
using Avala.Forges.Contracts;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Timeline;

internal interface ITimelineEntry
{
    string Key { get; }
}

internal sealed record PromptEntry(string Key, int Attempt, AttemptOrigin Origin, Option<string> Text, Option<AttemptOutcome> Outcome) : ITimelineEntry
{
    public IReadOnlyList<Attachment> Attachments { get; init; } = [];

    public Option<HandoffRecord> Handoff { get; init; }

    public MessageAuthor Author { get; init; } = MessageAuthor.Person;

    public PromptEntry WrittenAs(AttemptRecord attempt) => this with { Author = attempt.Author };

    public bool IsFromPerson => Author == MessageAuthor.Person && Origin is AttemptOrigin.Initial or AttemptOrigin.Hint or AttemptOrigin.SendBack or AttemptOrigin.Device;
}

internal sealed record RetryEntry(string Key, int Attempt, int Most, TimeSpan Wait, string Reason) : ITimelineEntry
{
    public bool Recovered { get; init; }

    public bool GaveUp { get; init; }
}

internal sealed record RestartEntry(string Key, bool Kept) : ITimelineEntry
{
    public IReadOnlyList<string> Lost { get; init; } = [];
}

internal sealed record CiEntry(string Key, int PullRequest, string Head, IReadOnlyList<CheckRun> Checks) : ITimelineEntry
{
    public bool Woken { get; init; }

    public bool Failed => Checks.Any(check => check.Status == CheckStatus.Failed);
}

internal sealed record DocsEntry(string Key, string Feature, FeatureVersion Version) : ITimelineEntry;

internal sealed record ChangeEntry(string Key, ChangeRequest Request) : ITimelineEntry;

internal sealed record InterjectionEntry(string Key, string Text) : ITimelineEntry
{
    public bool FromDevice { get; init; }

    public IReadOnlyList<Attachment> Attachments { get; init; } = [];

    public MessageAuthor Author { get; init; } = MessageAuthor.Person;

    public InterjectionEntry WrittenAs(MessageQueued queued) => this with { Author = queued.Author };
}

internal sealed record MessageEntry(string Key, string Text, Option<ItemOutcome> Outcome) : ITimelineEntry;

internal sealed record ReasoningEntry(string Key, string Text, DateTimeOffset Started, Option<TimeSpan> Duration, Option<ItemOutcome> Outcome) : ITimelineEntry;

internal sealed record ToolEntry(string Key, ItemKind Kind, string Title, string Output, Option<ItemOutcome> Outcome) : ITimelineEntry
{
    public Option<int> Drafted { get; init; }

    public Option<string> Input { get; init; }

    public Option<ToolCalled> Called { get; init; }

    public Option<DelegationRecord> Delegation { get; init; }

    public bool Denied { get; init; }

    public Option<JobId> WaitsFor { get; init; }

    public Option<DelegationRecord> Awaited { get; init; }

    public int Steps { get; init; }

    public string Doing { get; init; } = string.Empty;

    public long Tokens { get; init; }

    public Option<DateTimeOffset> Started { get; init; }

    public ImmutableList<string> Trail { get; init; } = [];

    public bool Backgrounded { get; init; }

    public string Named => Title.StartsWith(SubagentPrefix, StringComparison.Ordinal) ? Title[SubagentPrefix.Length..] : Title;

    private const string SubagentPrefix = "Subagent: ";

    public ImmutableHashSet<ItemId> Children { get; init; } = [];

    public ImmutableDictionary<ItemId, string> ChildTitles { get; init; } = ImmutableDictionary<ItemId, string>.Empty;

    public ImmutableList<BackgroundStep> Background { get; init; } = [];
}

internal sealed record BackgroundStep(ItemId Item, string Title, Option<ItemOutcome> Outcome);

internal sealed record PlanEntry(string Key, IReadOnlyList<PlanStep> Steps) : ITimelineEntry
{
    public int Done => Steps.Count(step => step.Status == PlanStepStatus.Done);

    public int Total => Steps.Count;
}

internal sealed record CanvasEntry(string Key, string Title, string MediaType, string Content, CanvasStatus Status) : ITimelineEntry
{
    public bool IsOffered { get; init; } = true;
}

internal sealed record PermissionEntry(string Key, SessionId Session, TurnId Turn, ItemId Item, ItemKind Kind, string Title, string Target) : ITimelineEntry
{
    public Option<PolicyDecision> Decision { get; init; }

    public Option<PermissionAnswer> Resolution { get; init; }

    public Option<HumanAnswer> Answered { get; init; }

    public bool Closed { get; init; }

    public bool Withdrawn { get; init; }

    public bool Denied =>
        Resolution == Option<PermissionAnswer>.Some(PermissionAnswer.Deny)
        || Decision.Match(decision => decision.Answer == PolicyAnswer.Deny, () => false)
        || Answered.Match(answer => answer.Answer == PermissionAnswer.Deny, () => false);

    public bool WentToHuman => Decision.Match(decision => decision.Delivery is not (DecisionDelivery.Answered or DecisionDelivery.LeftToParent), () => false);

    public bool WentToParent => Decision.Match(decision => decision.Parent.IsSome, () => false);

    public bool AwaitsHuman => WentToHuman && Resolution.IsNone && !Closed && !Withdrawn;

    public bool AwaitsParent => Decision.Match(decision => decision.Delivery == DecisionDelivery.LeftToParent, () => false) && Resolution.IsNone && !Closed && !Withdrawn;
}

internal sealed record DeniedEntry(string Key, PolicyDecision Decision) : ITimelineEntry
{
    public Option<HumanAnswer> Allowed { get; init; }

    public static bool Shows(PolicyDecision decision) =>
        decision is { Delivery: DecisionDelivery.Answered, Answer: PolicyAnswer.Deny, Autonomy: Autonomy.Autonomous, Job.IsSome: true };
}

internal sealed record FormEntry(string Key, SessionId Session, TurnId Turn, ItemId Item, AgentForm Form) : ITimelineEntry
{
    public Option<FormDecision> Decision { get; init; }

    public Option<FormAnswer> Answer { get; init; }

    public Option<ItemOutcome> Outcome { get; init; }

    public bool Closed { get; init; }

    public bool Withdrawn { get; init; }

    public bool WentToHuman => Decision.Match(decision => decision.Delivery != DecisionDelivery.LeftToParent && (decision.Answer.IsNone || decision.Delivery != DecisionDelivery.Answered), () => false);

    public bool WentToParent => Decision.Match(decision => decision.Parent.IsSome, () => false);

    public bool AwaitsHuman => WentToHuman && Answer.IsNone && Outcome.IsNone && !Closed && !Withdrawn;

    public bool AwaitsParent => Decision.Match(decision => decision.Delivery == DecisionDelivery.LeftToParent, () => false) && Answer.IsNone && Outcome.IsNone && !Closed && !Withdrawn;
}

internal enum ChildAskingState
{
    Waiting,
    Answered,
    Declined,
    Allowed,
    Denied,
    Withdrawn,
}

internal sealed record ChildAskingEntry(string Key, SessionId Session, ItemId Item, string Child, string Asking) : ITimelineEntry
{
    public Option<string> Harness { get; init; }

    public ChildAskingState State { get; init; }

    public Option<ChildPermission> Permission { get; init; }

    public bool ForYou => Permission.IsSome;
}

internal sealed record ChildPermission(ItemKind Kind, string Target);

internal sealed record TurnEndEntry(string Key, TurnOutcome Outcome, TimeSpan Duration, TokenUsage Tokens, IReadOnlyList<Cost> Costs) : ITimelineEntry
{
    public Option<string> Model { get; init; }

    public IReadOnlyList<string> Lost { get; init; } = [];
}
