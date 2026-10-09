using Avala.Agents.Contracts.Events;
using Avala.Canvas.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Storage;
using Avala.Transcripts.Contracts;

namespace Avala.Transcripts.Storage;

internal sealed class StoredFact
{
    public long Key { get; init; }

    public Guid Job { get; init; }

    public Guid Run { get; init; }

    public long At { get; init; }

    public string Kind { get; init; } = string.Empty;

    public string Slot { get; init; } = string.Empty;

    public int Attempt { get; init; }

    public string Fact { get; init; } = string.Empty;

    public static StoredFact Of(Guid run, PendingFact pending) => new()
    {
        Job = pending.Job.Value,
        Run = run,
        At = pending.At.UtcTicks,
        Kind = KindOf(pending.Fact),
        Slot = pending.Fact is CanvasDrawn drawn ? SlotOf(drawn.Snapshot.Canvas) : string.Empty,
        Attempt = pending.Fact is AttemptBegan began ? began.Attempt : 0,
        Fact = pending.Fact switch
        {
            AgentActed acted => StoredJson.Write<object>(acted.Event),
            CanvasDrawn canvas => StoredJson.Write(canvas.Snapshot),
            PermissionRuled ruled => StoredJson.Write(ruled.Decision),
            FormRuled ruled => StoredJson.Write(ruled.Decision),
            var other => StoredJson.Write<object>(other),
        },
    };

    public static string SlotOf(CanvasId canvas) => $"{canvas.Turn.Value}:{canvas.Item.Value}";

    public Option<KeptFact> Read() =>
        (Kind switch
        {
            nameof(AttemptBegan) => Option<ITranscriptFact>.Some(new AttemptBegan(Attempt)),
            nameof(CanvasDrawn) => Option<ITranscriptFact>.Some(new CanvasDrawn(StoredJson.Read<CanvasSnapshot>(Fact))),
            nameof(PermissionRuled) => Option<ITranscriptFact>.Some(new PermissionRuled(StoredJson.Read<PolicyDecision>(Fact))),
            nameof(FormRuled) => Option<ITranscriptFact>.Some(new FormRuled(StoredJson.Read<FormDecision>(Fact))),
            _ => Activity().Map<ITranscriptFact>(activity => new AgentActed(activity)),
        }).Map(fact => new KeptFact(Run, new DateTimeOffset(At, TimeSpan.Zero), fact));

    private static string KindOf(ITranscriptFact fact) => fact is AgentActed acted ? acted.Event.GetType().Name : fact.GetType().Name;

    private Option<IAgentEvent> Activity() => Kind switch
    {
        nameof(TurnStarted) => StoredJson.Read<TurnStarted>(Fact),
        nameof(ItemStarted) => StoredJson.Read<ItemStarted>(Fact),
        nameof(CanvasStarted) => StoredJson.Read<CanvasStarted>(Fact),
        nameof(ItemProgressed) => StoredJson.Read<ItemProgressed>(Fact),
        nameof(ItemCompleted) => StoredJson.Read<ItemCompleted>(Fact),
        nameof(PermissionRequested) => StoredJson.Read<PermissionRequested>(Fact),
        nameof(PermissionResolved) => StoredJson.Read<PermissionResolved>(Fact),
        nameof(FormRequested) => StoredJson.Read<FormRequested>(Fact),
        nameof(FormAnswered) => StoredJson.Read<FormAnswered>(Fact),
        nameof(RequestWithdrawn) => StoredJson.Read<RequestWithdrawn>(Fact),
        nameof(ToolCalled) => StoredJson.Read<ToolCalled>(Fact),
        nameof(ToolReturned) => StoredJson.Read<ToolReturned>(Fact),
        nameof(MessageQueued) => StoredJson.Read<MessageQueued>(Fact),
        nameof(PlanUpdated) => StoredJson.Read<PlanUpdated>(Fact),
        nameof(UsageReported) => StoredJson.Read<UsageReported>(Fact),
        nameof(TurnCompleted) => StoredJson.Read<TurnCompleted>(Fact),
        _ => Option<IAgentEvent>.None,
    };
}

internal sealed record PendingFact(JobId Job, DateTimeOffset At, ITranscriptFact Fact);
