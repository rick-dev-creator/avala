using Avala.Observability.Usage;

namespace Avala.Observability.Tracking;

internal sealed record StoredUsage(IReadOnlyList<SessionUsage> Sessions, IReadOnlyList<UsageFact> Facts);

internal interface IUsageStore
{
    Task KeepSessionAsync(SessionUsage session, CancellationToken cancellationToken);

    Task RecordAsync(UsageFact fact, CancellationToken cancellationToken);

    Task<StoredUsage> EarlierRunsAsync(CancellationToken cancellationToken);

    Task<StoredUsage> WithinAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
