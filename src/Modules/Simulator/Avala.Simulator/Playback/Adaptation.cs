using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Simulator.Playback;

internal sealed class Adaptation(CapabilitySet declared)
{
    private readonly HashSet<ItemId> hidden = [];
    private readonly Dictionary<ItemId, List<string>> held = [];

    public IEnumerable<IAgentEvent> Adapt(IAgentEvent cue) => cue switch
    {
        LimitReported when !declared.Has<ReportsLimits>() => [],
        UsageReported when !declared.Has<ReportsUsage>() => [],
        UsageReported usage when !declared.Has<ReportsCost>() => [usage with { Cost = Option<Cost>.None }],
        ResumeTokenIssued when !declared.Has<Resumable>() => [],
        ItemStarted { Kind: ItemKind.Reasoning } started when !declared.Has<ExposesReasoning>() => Hide(started.Item),
        ItemStarted { Kind: ItemKind.Message } started when !declared.Has<StreamsPartialOutput>() => Hold(started),
        ItemProgressed progressed when hidden.Contains(progressed.Item) => [],
        ItemProgressed progressed when held.TryGetValue(progressed.Item, out var parts) => Gather(parts, progressed.Text),
        ItemCompleted completed when hidden.Remove(completed.Item) => [],
        ItemCompleted completed when held.Remove(completed.Item, out var parts) => Release(completed, parts),
        _ => [cue],
    };

    private IEnumerable<IAgentEvent> Hide(ItemId item)
    {
        hidden.Add(item);

        return [];
    }

    private IEnumerable<IAgentEvent> Hold(ItemStarted started)
    {
        held[started.Item] = [];

        return [started];
    }

    private static IEnumerable<IAgentEvent> Gather(List<string> parts, string text)
    {
        parts.Add(text);

        return [];
    }

    private static IEnumerable<IAgentEvent> Release(ItemCompleted completed, List<string> parts) =>
        parts.Count == 0
            ? [completed]
            : [new ItemProgressed(completed.Session, completed.Turn, completed.Item, string.Concat(parts)), completed];
}
