using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;

namespace Avala.Agents.Tests.Conformance;

internal static class DeclaredCapabilities
{
    public static IReadOnlyList<string> Breaches(CapabilitySet declared, IReadOnlyList<IAgentEvent> events)
    {
        IEnumerable<string> breaches =
        [
            .. events.OfType<UsageReported>().SelectMany(usage => Usage(declared, usage)),
            .. events.OfType<LimitReported>().SelectMany(limit => Limit(declared, limit.Limit)),
            .. Undeclared<ExposesReasoning>(declared, Started(events, ItemKind.Reasoning))
                .Select(item => $"the reasoning {item.Value} was exposed although the provider does not declare ExposesReasoning"),
            .. Undeclared<StreamsPartialOutput>(declared, Started(events, ItemKind.Message).Where(item => events.OfType<ItemProgressed>().Count(progressed => progressed.Item == item) > 1))
                .Select(item => $"the message {item.Value} streamed in parts although the provider does not declare StreamsPartialOutput"),
        ];

        return [.. breaches.Distinct(StringComparer.Ordinal)];
    }

    private static IEnumerable<string> Usage(CapabilitySet declared, UsageReported usage) =>
    [
        .. declared.Has<ReportsUsage>() ? [] : new[] { "usage was reported although the provider does not declare ReportsUsage" },
        .. usage.Cost.Match(
            cost => declared.Get<ReportsCost>().Match<IEnumerable<string>>(
                reports => reports.Currency == cost.Currency ? [] : [$"a cost was reported in {cost.Currency} although the provider declares {reports.Currency}"],
                () => ["a cost was reported although the provider does not declare ReportsCost"]),
            () => []),
    ];

    private static IEnumerable<string> Limit(CapabilitySet declared, UsageLimit limit) =>
        declared.Get<ReportsLimits>().Match<IEnumerable<string>>(
            reports => reports.Covers(limit.Window) ? [] : [$"a limit was reported for the window {limit.Window}, which the provider does not declare"],
            () => ["a limit was reported although the provider does not declare ReportsLimits"]);

    private static IEnumerable<ItemId> Started(IReadOnlyList<IAgentEvent> events, ItemKind kind) =>
        events.OfType<ItemStarted>().Where(started => started.Kind == kind).Select(started => started.Item);

    private static IEnumerable<ItemId> Undeclared<T>(CapabilitySet declared, IEnumerable<ItemId> items)
        where T : ICapability =>
        declared.Has<T>() ? [] : items;
}
