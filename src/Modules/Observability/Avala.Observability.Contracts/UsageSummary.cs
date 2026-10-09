using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;

namespace Avala.Observability.Contracts;

public sealed record UsageSummary(
    TokenUsage Tokens,
    IReadOnlyList<Cost> Costs,
    int UnpricedReports,
    TurnTally Turns,
    IReadOnlyList<UsageLimit> Limits);

public readonly record struct TurnTally(int Finished, int Interrupted, int Failed, TimeSpan Duration);

public sealed record ProviderUsage(ProviderInfo Provider, UsageSummary Usage);

public sealed record AccountUsage(ProviderInfo Provider, AgentAccount Account, UsageSummary Usage);
