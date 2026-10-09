namespace Avala.Observability.Contracts;

public interface IUsageHistory
{
    ValueTask<UsagePeriod> WithinAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<UsagePeriod>> DailyAsync(DateOnly first, DateOnly last, TimeZoneInfo zone, CancellationToken cancellationToken);
}

public sealed record UsagePeriod(
    DateTimeOffset From,
    DateTimeOffset To,
    UsageSummary Usage,
    IReadOnlyList<ProviderUsage> ByProvider,
    IReadOnlyList<ConnectionUsage> ByConnection);
