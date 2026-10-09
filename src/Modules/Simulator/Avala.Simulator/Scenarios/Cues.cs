using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;

namespace Avala.Simulator.Scenarios;

internal sealed record Cues(SessionId Session, TurnId Turn)
{
    public TurnStarted Started => new(Session, Turn);

    public TurnCompleted Ended(TurnOutcome outcome) => new(Session, Turn, outcome);

    public ResumeTokenIssued Resumable(ResumeToken token) => new(Session, Turn, token);

    public ItemStarted Opened(ItemId item, ItemKind kind, string title) => new(Session, Turn, item, kind, title);

    public ItemProgressed Progressed(ItemId item, string text) => new(Session, Turn, item, text);

    public ItemCompleted Closed(ItemId item, ItemOutcome outcome) => new(Session, Turn, item, outcome);

    public PermissionRequested Asked(ItemId item, string title, ItemKind kind, string target) => new(Session, Turn, item, title, kind, target);

    public FormRequested Asked(ItemId item, AgentForm form) => new(Session, Turn, item, form);

    public PermissionResolved Answered(ItemId item, PermissionAnswer answer) => new(Session, Turn, item, answer);

    public FormAnswered Answered(ItemId item, FormAnswer answer) => new(Session, Turn, item, answer);

    public ToolCalled Called(ItemId item, string tool, string input) => new(Session, Turn, item, tool, input);

    public ToolReturned Returned(ItemId item, ToolResult result) => new(Session, Turn, item, result);

    public IEnumerable<IAgentEvent> Of(IStep step) => step switch
    {
        Say say => Stream(Opened(say.Item, say.Kind, say.Title), say.Item, say.Chunks),
        Draw draw => Stream(new CanvasStarted(Session, Turn, draw.Item, draw.Title, draw.MediaType), draw.Item, draw.Chunks),
        UpdatePlan plan => [new PlanUpdated(Session, Turn, plan.Steps)],
        ReportUsage usage => [new UsageReported(Session, Turn, usage.Tokens, usage.Cost)],
        ReportLimit limit => [new LimitReported(Session, Turn, limit.Limit)],
        Open open => [Opened(open.Item, open.Kind, open.Title)],
        Finish => [Ended(TurnOutcome.Finished)],
        _ => [],
    };

    public IEnumerable<IAgentEvent> Diverged(string reason, bool started) =>
    [
        .. started ? [] : new IAgentEvent[] { Started },
        Opened(DivergenceItem, ItemKind.Other, "Replay diverged"),
        Progressed(DivergenceItem, $"Replay diverged: {reason}."),
        Closed(DivergenceItem, ItemOutcome.Failed),
        Ended(TurnOutcome.Failed),
    ];

    public IAgentEvent Readdress(IAgentEvent recorded, ResumeToken token) => recorded switch
    {
        TurnStarted => Started,
        ResumeTokenIssued => Resumable(token),
        TurnCompleted completed => Ended(completed.Outcome),
        ItemStarted started => started with { Session = Session, Turn = Turn },
        CanvasStarted canvas => canvas with { Session = Session, Turn = Turn },
        ItemProgressed progressed => progressed with { Session = Session, Turn = Turn },
        ItemCompleted completed => completed with { Session = Session, Turn = Turn },
        PermissionRequested requested => requested with { Session = Session, Turn = Turn },
        PermissionResolved resolved => resolved with { Session = Session, Turn = Turn },
        FormRequested requested => requested with { Session = Session, Turn = Turn },
        FormAnswered answered => answered with { Session = Session, Turn = Turn },
        ToolCalled called => called with { Session = Session, Turn = Turn },
        ToolReturned returned => returned with { Session = Session, Turn = Turn },
        PlanUpdated plan => plan with { Session = Session, Turn = Turn },
        UsageReported usage => usage with { Session = Session, Turn = Turn },
        LimitReported limit => limit with { Session = Session, Turn = Turn },
        _ => recorded,
    };

    public static ItemId DivergenceItem { get; } = new("replay-divergence");

    private IEnumerable<IAgentEvent> Stream(IAgentEvent opened, ItemId item, IReadOnlyList<string> chunks) =>
        [opened, .. chunks.Select(chunk => Progressed(item, chunk)), Closed(item, ItemOutcome.Succeeded)];
}
