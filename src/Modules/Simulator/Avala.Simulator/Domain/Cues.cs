using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;

namespace Avala.Simulator.Domain;

internal sealed record Cues(SessionId Session, TurnId Turn)
{
    public TurnStarted Started => new(Session, Turn);

    public TurnCompleted Ended(TurnOutcome outcome) => new(Session, Turn, outcome);

    public ItemStarted Opened(ItemId item, ItemKind kind, string title) => new(Session, Turn, item, kind, title);

    public ItemProgressed Progressed(ItemId item, string text) => new(Session, Turn, item, text);

    public ItemCompleted Closed(ItemId item, ItemOutcome outcome) => new(Session, Turn, item, outcome);

    public PermissionRequested Asked(ItemId item, string title) => new(Session, Turn, item, title);

    public PermissionResolved Answered(ItemId item, PermissionAnswer answer) => new(Session, Turn, item, answer);

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

    private IEnumerable<IAgentEvent> Stream(IAgentEvent opened, ItemId item, IReadOnlyList<string> chunks) =>
        [opened, .. chunks.Select(chunk => Progressed(item, chunk)), Closed(item, ItemOutcome.Succeeded)];
}
