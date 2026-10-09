using System.Collections.Concurrent;
using Avala.Observability.Tracking;
using Avala.Observability.Usage;

namespace Avala.Observability.Tests.Tracking;

internal sealed class InMemoryUsageStore : IUsageStore
{
    private readonly ConcurrentQueue<SessionUsage> sessions = new();
    private readonly ConcurrentQueue<UsageFact> facts = new();

    public IReadOnlyList<UsageFact> Facts => [.. facts];

    public IReadOnlyList<SessionUsage> Sessions => [.. sessions];

    public Task KeepSessionAsync(SessionUsage session, CancellationToken cancellationToken)
    {
        sessions.Enqueue(session);

        return Task.CompletedTask;
    }

    public Task RecordAsync(UsageFact fact, CancellationToken cancellationToken)
    {
        facts.Enqueue(fact);

        return Task.CompletedTask;
    }

    public StoredUsage Earlier { get; set; } = new([], []);

    public Task<StoredUsage> EarlierRunsAsync(CancellationToken cancellationToken) => Task.FromResult(Earlier);

    public Task<StoredUsage> WithinAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        Task.FromResult(new StoredUsage(
            [.. sessions.GroupBy(session => session.Session).Select(session => session.Last())],
            [.. facts.Where(fact => fact.At >= from && fact.At < to)]));
}
