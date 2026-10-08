using Avala.Agents.Contracts.Events;
using Avala.Observability.Contracts;

namespace Avala.Observability.Usage;

internal static class UsageRollup
{
    extension(IReadOnlyCollection<SessionUsage> sessions)
    {
        public UsageSummary Summary => new(
            sessions.Aggregate(default(TokenUsage), (sum, session) => sum + session.Tokens),
            [
                .. sessions
                    .SelectMany(session => session.Costs)
                    .GroupBy(cost => cost.Key, cost => cost.Value, StringComparer.Ordinal)
                    .OrderBy(currency => currency.Key, StringComparer.Ordinal)
                    .Select(currency => new Cost(currency.Sum(), currency.Key)),
            ],
            sessions.Sum(session => session.UnpricedReports),
            sessions.Aggregate(default(TurnTally), (sum, session) => sum + session.Turns),
            [
                .. sessions
                    .SelectMany(session => session.Limits.Values)
                    .GroupBy(reading => reading.Limit.Window, StringComparer.Ordinal)
                    .OrderBy(window => window.Key, StringComparer.Ordinal)
                    .Select(window => window.MaxBy(reading => reading.At).Limit),
            ]);
    }
}
