using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
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

    extension(IEnumerable<SessionUsage> sessions)
    {
        public IReadOnlyList<ProviderUsage> ByProvider() =>
        [
            .. sessions
                .SelectMany(usage => usage.Provider.Match<ProviderInfo[]>(provider => [provider], () => []), (usage, provider) => (usage, provider))
                .GroupBy(pair => pair.provider, pair => pair.usage)
                .OrderBy(provider => provider.Key.Id, StringComparer.Ordinal)
                .Select(provider => new ProviderUsage(provider.Key, provider.ToList().Summary)),
        ];

        public IReadOnlyList<AccountUsage> ByAccount() =>
        [
            .. sessions
                .SelectMany(
                    usage => usage.Provider.Bind(provider => usage.Account.Map(account => (provider, account)))
                        .Match<(ProviderInfo Provider, AgentAccount Account)[]>(owner => [owner], () => []),
                    (usage, owner) => (usage, owner))
                .GroupBy(pair => pair.owner, pair => pair.usage)
                .OrderBy(owner => owner.Key.Provider.Id, StringComparer.Ordinal)
                .ThenBy(owner => owner.Key.Account.Id, StringComparer.Ordinal)
                .Select(owner => new AccountUsage(owner.Key.Provider, owner.Key.Account, owner.ToList().Summary)),
        ];

        public IReadOnlyList<ConnectionUsage> ByConnection() =>
        [
            .. sessions
                .SelectMany(
                    usage => usage.Connection.Bind(connection => usage.Provider.Map(provider => (connection, provider)))
                        .Match<(ConnectionName Connection, ProviderInfo Provider)[]>(owner => [owner], () => []),
                    (usage, owner) => (usage, owner))
                .GroupBy(pair => pair.owner, pair => pair.usage)
                .OrderBy(owner => owner.Key.Connection.Value, StringComparer.Ordinal)
                .Select(owner => new ConnectionUsage(owner.Key.Connection, owner.Key.Provider, owner.ToList().Summary)),
        ];

        public IReadOnlyList<SessionUsage> Recording(IEnumerable<UsageFact> facts)
        {
            var folded = sessions.ToDictionary(session => session.Session);

            foreach (var fact in facts.OrderBy(fact => fact.At))
            {
                folded[fact.Session] = (folded.GetValueOrDefault(fact.Session) ?? new SessionUsage(fact.Session)).Recorded(fact);
            }

            return [.. folded.Values];
        }
    }
}
