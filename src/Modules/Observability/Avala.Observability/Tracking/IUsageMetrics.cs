using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Observability.Tracking;

internal interface IUsageMetrics
{
    void RecordUsage(Option<ProviderInfo> provider, TokenUsage tokens, Option<Cost> cost);

    void RecordTurn(Option<ProviderInfo> provider, TurnOutcome outcome, Option<TimeSpan> duration);

    void RecordLimit(Option<ProviderInfo> provider, UsageLimit limit);
}
