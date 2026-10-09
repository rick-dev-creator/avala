using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Observability.Tracking;

internal sealed record UsageSource(Option<ProviderInfo> Provider, Option<ConnectionName> Connection);

internal interface IUsageMetrics
{
    void RecordUsage(UsageSource source, TokenUsage tokens, Option<Cost> cost);

    void RecordTurn(UsageSource source, TurnOutcome outcome, Option<TimeSpan> duration);

    void RecordLimit(UsageSource source, UsageLimit limit);
}
