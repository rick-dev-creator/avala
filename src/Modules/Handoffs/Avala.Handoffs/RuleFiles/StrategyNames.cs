using Avala.Handoffs.Contracts;
using Avala.Sdk;

namespace Avala.Handoffs.RuleFiles;

internal static class StrategyNames
{
    private static readonly (HandoffStrategy Strategy, string Name)[] Names =
    [
        (HandoffStrategy.MostHeadroom, "most-headroom"),
        (HandoffStrategy.InOrder, "in-order"),
        (HandoffStrategy.SoonestReset, "soonest-reset"),
        (HandoffStrategy.RoundRobin, "round-robin"),
    ];

    public static string Of(HandoffStrategy strategy) => Names.First(named => named.Strategy == strategy).Name;

    public static Option<HandoffStrategy> Read(string name) =>
        Names.Where(named => named.Name == name).Select(named => Option<HandoffStrategy>.Some(named.Strategy)).FirstOrDefault();
}
