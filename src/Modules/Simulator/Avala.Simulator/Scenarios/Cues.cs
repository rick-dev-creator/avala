using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Simulator.Scenarios;

internal sealed record Cues(SessionId Session, TurnId Turn)
{
    public string Told { get; init; } = string.Empty;

    public IReadOnlyList<Attachment> Attached { get; init; } = [];

    public TurnStarted Started => new(Session, Turn);

    public TurnCompleted Ended(TurnOutcome outcome) => new(Session, Turn, outcome);

    public ResumeTokenIssued Resumable(ResumeToken token) => new(Session, Turn, token);

    public ModelReported Ran(string model, Option<string> effort) => new(Session, Turn, model) { Effort = effort };

    public ItemStarted Opened(ItemId item, ItemKind kind, string title) => new(Session, Turn, item, kind, title);

    public ItemStarted Opened(ItemId item, ItemKind kind, string title, string input) => Opened(item, kind, title) with { Input = input };

    public ItemStarted Opened(ItemId item, ItemKind kind, string title, string input, Option<ItemId> parent) => Opened(item, kind, title, input) with { Parent = parent };

    public ItemTokensReported Metered(ItemId item, long tokens) => new(Session, Turn, item, tokens);

    public const int DraftStride = 512;

    public ItemDrafted Drafted(ItemId item, ItemKind kind, string title, int received) => new(Session, Turn, item, kind, title, received);

    public ItemProgressed Progressed(ItemId item, string text) => new(Session, Turn, item, text);

    public ItemCompleted Closed(ItemId item, ItemOutcome outcome) => new(Session, Turn, item, outcome);

    public PermissionRequested Asked(ItemId item, string title, ItemKind kind, string target) => new(Session, Turn, item, title, kind, target);

    public FormRequested Asked(ItemId item, AgentForm form) => new(Session, Turn, item, form);

    public PermissionResolved Answered(ItemId item, PermissionAnswer answer) => new(Session, Turn, item, answer);

    public FormAnswered Answered(ItemId item, FormAnswer answer) => new(Session, Turn, item, answer);

    public RequestWithdrawn Withdrawn(ItemId item) => new(Session, Turn, item);

    public ToolCalled Called(ItemId item, string tool, string input) => new(Session, Turn, item, tool, input);

    public ToolReturned Returned(ItemId item, ToolResult result) => new(Session, Turn, item, result);

    public IEnumerable<IAgentEvent> Of(IStep step) => step switch
    {
        Say say => Stream(Opened(say.Item, say.Kind, say.Title), say.Item, say.Chunks),
        Draw draw => Stream(new CanvasStarted(Session, Turn, draw.Item, draw.Title, draw.MediaType), draw.Item, draw.Chunks),
        UpdatePlan plan => [new PlanUpdated(Session, Turn, plan.Steps)],
        ReportUsage usage => [new UsageReported(Session, Turn, usage.Tokens, usage.Cost)],
        ReportLimit limit => [new LimitReported(Session, Turn, limit.Limit)],
        Retry retry => [new HarnessRetrying(Session, Turn, retry.Attempt, retry.Most, retry.Wait, retry.Reason)],
        ReportContext context => [new ContextReported(Session, Turn, context.Used, context.Window) { Compacted = context.Compacted }],
        Open open => [Opened(open.Item, open.Kind, open.Title)],
        Recall recall => Stream(Opened(recall.Item, ItemKind.Message, "Reply"), recall.Item, ["I was told: ", Told]),
        RecallAttachments attachments => Stream(Opened(attachments.Item, ItemKind.Message, "Reply"), attachments.Item, [Attached.Count == 0 ? "Nothing was attached." : $"I received {string.Join(", ", Attached.Select(attached => $"{attached.Name} ({attached.MediaType})"))}."]),
        FromDevice device when device.Text == Told => [new RemoteMessageArrived(Session, Turn, device.Text) { BeganTurn = true }],
        Finish => [Ended(TurnOutcome.Finished)],
        _ => Delegated(step),
    };

    private IEnumerable<IAgentEvent> Delegated(IStep step) => step switch
    {
        StartSubagent agent => [Opened(agent.Item, ItemKind.Subagent, $"Subagent: {agent.Name}", agent.Prompt)],
        MeterSubagent meter => [Metered(meter.Item, meter.Tokens)],
        EndSubagent agent => [Progressed(agent.Item, agent.Output), Closed(agent.Item, ItemOutcome.Succeeded)],
        RunTold run => [Opened(run.Item, ItemKind.Command, $"Run {Told}", Told), Closed(run.Item, ItemOutcome.Succeeded)],
        ReturnTold use => [Opened(use.Item, ItemKind.Mcp, use.Tool, string.Empty), Progressed(use.Item, Told), Closed(use.Item, ItemOutcome.Succeeded)],
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
        PlanUpdated plan => plan with { Session = Session, Turn = Turn },
        UsageReported usage => usage with { Session = Session, Turn = Turn },
        LimitReported limit => limit with { Session = Session, Turn = Turn },
        HarnessRetrying retrying => retrying with { Session = Session, Turn = Turn },
        ContextReported context => context with { Session = Session, Turn = Turn },
        ModelReported model => model with { Session = Session, Turn = Turn },
        _ => ReaddressItem(recorded),
    };

    public static ItemId DivergenceItem { get; } = new("replay-divergence");

    private IAgentEvent ReaddressItem(IAgentEvent recorded) => recorded switch
    {
        ItemStarted started => started with { Session = Session, Turn = Turn },
        ItemDrafted drafted => drafted with { Session = Session, Turn = Turn },
        CanvasStarted canvas => canvas with { Session = Session, Turn = Turn },
        ItemProgressed progressed => progressed with { Session = Session, Turn = Turn },
        ItemCompleted completed => completed with { Session = Session, Turn = Turn },
        ItemTokensReported tokens => tokens with { Session = Session, Turn = Turn },
        ToolCalled called => called with { Session = Session, Turn = Turn },
        ToolReturned returned => returned with { Session = Session, Turn = Turn },
        _ => ReaddressExchange(recorded),
    };

    private IAgentEvent ReaddressExchange(IAgentEvent recorded) => recorded switch
    {
        PermissionRequested requested => requested with { Session = Session, Turn = Turn },
        PermissionResolved resolved => resolved with { Session = Session, Turn = Turn },
        FormRequested requested => requested with { Session = Session, Turn = Turn },
        FormAnswered answered => answered with { Session = Session, Turn = Turn },
        RequestWithdrawn withdrawn => withdrawn with { Session = Session, Turn = Turn },
        _ => recorded,
    };

    private IEnumerable<IAgentEvent> Stream(IAgentEvent opened, ItemId item, IReadOnlyList<string> chunks) =>
        [opened, .. chunks.Select(chunk => Progressed(item, chunk)), Closed(item, ItemOutcome.Succeeded)];
}
