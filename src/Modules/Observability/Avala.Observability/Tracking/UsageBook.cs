using System.Collections.Concurrent;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Observability.Usage;
using Avala.Sdk;

namespace Avala.Observability.Tracking;

internal sealed class UsageBook : IUsage
{
    private readonly ConcurrentDictionary<SessionId, SessionUsage> sessions = new();

    public SessionUsage Of(SessionId session) => sessions.GetValueOrDefault(session) ?? new SessionUsage(session);

    public void Keep(SessionUsage usage) => sessions[usage.Session] = usage;

    public IReadOnlyList<ProviderUsage> ByProvider() =>
    [
        .. sessions.Values
            .SelectMany(usage => usage.Provider.Match<ProviderInfo[]>(provider => [provider], () => []), (usage, provider) => (usage, provider))
            .GroupBy(pair => pair.provider, pair => pair.usage)
            .OrderBy(provider => provider.Key.Id, StringComparer.Ordinal)
            .Select(provider => new ProviderUsage(provider.Key, provider.ToList().Summary)),
    ];

    public Option<UsageSummary> OfSession(SessionId session) =>
        sessions.TryGetValue(session, out var usage) ? new[] { usage }.Summary : Option<UsageSummary>.None;

    public Option<UsageSummary> OfJob(JobId job)
    {
        var worked = sessions.Values.Where(usage => usage.Job == Option<JobId>.Some(job)).ToList();

        return worked.Count == 0 ? Option<UsageSummary>.None : worked.Summary;
    }
}
