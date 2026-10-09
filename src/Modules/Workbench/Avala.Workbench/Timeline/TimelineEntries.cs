using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Canvas.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Timeline;

internal interface ITimelineEntry
{
    string Key { get; }
}

internal sealed record PromptEntry(string Key, int Attempt, AttemptOrigin Origin, Option<string> Text, Option<AttemptOutcome> Outcome) : ITimelineEntry;

internal sealed record RestartEntry(string Key, bool Kept) : ITimelineEntry;

internal sealed record InterjectionEntry(string Key, string Text) : ITimelineEntry;

internal sealed record MessageEntry(string Key, string Text, Option<ItemOutcome> Outcome) : ITimelineEntry;

internal sealed record ReasoningEntry(string Key, string Text, DateTimeOffset Started, Option<TimeSpan> Duration, Option<ItemOutcome> Outcome) : ITimelineEntry;

internal sealed record ToolEntry(string Key, ItemKind Kind, string Title, string Output, Option<ItemOutcome> Outcome) : ITimelineEntry
{
    public Option<string> Input { get; init; }
}

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

    public bool Closed { get; init; }

    public bool Withdrawn { get; init; }

    public bool WentToHuman => Decision.Match(decision => decision.Delivery != DecisionDelivery.Answered, () => false);

    public bool AwaitsHuman => WentToHuman && Resolution.IsNone && !Closed && !Withdrawn;
}

internal sealed record FormEntry(string Key, SessionId Session, TurnId Turn, ItemId Item, AgentForm Form) : ITimelineEntry
{
    public Option<FormDecision> Decision { get; init; }

    public Option<FormAnswer> Answer { get; init; }

    public Option<ItemOutcome> Outcome { get; init; }

    public bool Closed { get; init; }

    public bool Withdrawn { get; init; }

    public bool WentToHuman => Decision.Match(decision => decision.Answer.IsNone || decision.Delivery != DecisionDelivery.Answered, () => false);

    public bool AwaitsHuman => WentToHuman && Answer.IsNone && Outcome.IsNone && !Closed && !Withdrawn;
}

internal sealed record TurnEndEntry(string Key, TurnOutcome Outcome, TimeSpan Duration, TokenUsage Tokens, IReadOnlyList<Cost> Costs) : ITimelineEntry;
