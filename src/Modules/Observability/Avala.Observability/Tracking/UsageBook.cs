using System.Collections.Immutable;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Observability.Usage;
using Avala.Sdk;

namespace Avala.Observability.Tracking;

internal sealed class UsageBook : IUsage
{
    private ImmutableDictionary<SessionId, SessionUsage> sessions = ImmutableDictionary<SessionId, SessionUsage>.Empty;

    private ImmutableDictionary<SessionId, SessionUsage> Sessions => Volatile.Read(ref sessions);

    public SessionUsage Of(SessionId session) => Sessions.GetValueOrDefault(session) ?? new SessionUsage(session);

    public void Keep(SessionUsage usage) => ImmutableInterlocked.AddOrUpdate(ref sessions, usage.Session, usage, (_, _) => usage);

    public IReadOnlyList<ProviderUsage> ByProvider() =>
    [
        .. Sessions.Values
            .SelectMany(usage => usage.Provider.Match<ProviderInfo[]>(provider => [provider], () => []), (usage, provider) => (usage, provider))
            .GroupBy(pair => pair.provider, pair => pair.usage)
            .OrderBy(provider => provider.Key.Id, StringComparer.Ordinal)
            .Select(provider => new ProviderUsage(provider.Key, provider.ToList().Summary)),
    ];

    public Option<UsageSummary> OfSession(SessionId session) =>
        Sessions.TryGetValue(session, out var usage) ? new[] { usage }.Summary : Option<UsageSummary>.None;

    public Option<UsageSummary> OfJob(JobId job)
    {
        var worked = Sessions.Values.Where(usage => usage.Job == Option<JobId>.Some(job)).ToList();

        return worked.Count == 0 ? Option<UsageSummary>.None : worked.Summary;
    }
}
