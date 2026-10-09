using System.Collections.Immutable;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Observability.Usage;
using Avala.Sdk;

namespace Avala.Observability.Tracking;

internal sealed class UsageBook(IUsageStore store) : IUsage, IUsageSessions, IStartupTask
{
    private ImmutableDictionary<SessionId, SessionUsage> earlier = ImmutableDictionary<SessionId, SessionUsage>.Empty;
    private ImmutableDictionary<SessionId, SessionUsage> live = ImmutableDictionary<SessionId, SessionUsage>.Empty;

    private IEnumerable<SessionUsage> Sessions => Volatile.Read(ref earlier).Values.Concat(Volatile.Read(ref live).Values);

    public SessionUsage Of(SessionId session) => Volatile.Read(ref live).GetValueOrDefault(session) ?? new SessionUsage(session);

    public void Keep(SessionUsage usage) => ImmutableInterlocked.AddOrUpdate(ref live, usage.Session, usage, (_, _) => usage);

    public async Task AttributeAsync(SessionUsage usage, CancellationToken cancellationToken)
    {
        Keep(usage);
        await store.KeepSessionAsync(usage, cancellationToken);
    }

    public async Task RecordAsync(SessionUsage usage, UsageFact fact, CancellationToken cancellationToken)
    {
        Keep(usage);
        await store.RecordAsync(fact, cancellationToken);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var stored = await store.EarlierRunsAsync(cancellationToken);
        Volatile.Write(ref earlier, stored.Sessions.Recording(stored.Facts).ToImmutableDictionary(usage => usage.Session));
    }

    IReadOnlyList<UsageSession> IUsageSessions.Sessions() =>
        [.. Sessions.SelectMany(usage => usage.Seen.Match<UsageSession[]>(seen => [seen], () => [])).OrderBy(seen => seen.Opened)];

    public IReadOnlyList<ProviderUsage> ByProvider() => Sessions.ByProvider();

    public IReadOnlyList<AccountUsage> ByAccount() => Sessions.ByAccount();

    public IReadOnlyList<ConnectionUsage> ByConnection() => Sessions.ByConnection();

    public Option<UsageSummary> OfSession(SessionId session) =>
        Sessions.Where(usage => usage.Session == session).ToList() is { Count: > 0 } found ? found.Summary : Option<UsageSummary>.None;

    public Option<UsageSummary> OfJob(JobId job)
    {
        var worked = Sessions.Where(usage => usage.Job == Option<JobId>.Some(job)).ToList();

        return worked.Count == 0 ? Option<UsageSummary>.None : worked.Summary;
    }
}
