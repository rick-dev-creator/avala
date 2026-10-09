using Avala.Observability.Contracts;

namespace Avala.Workbench.Spending;

internal enum UsageSpan
{
    Today,
    LastSevenDays,
    LastThirtyDays,
}

internal sealed record UsageRange(UsageSpan Span, DateOnly First, DateOnly Today, UsagePeriod Total, IReadOnlyList<UsagePeriod> Days);

internal sealed class UsageWindows(IUsageHistory history, TimeProvider clock)
{
    public async ValueTask<UsageRange> ReadAsync(UsageSpan span, CancellationToken cancellationToken)
    {
        var zone = clock.LocalTimeZone;
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var first = today.AddDays(1 - Length(span));
        var start = new DateTimeOffset(first.ToDateTime(TimeOnly.MinValue), zone.GetUtcOffset(first.ToDateTime(TimeOnly.MinValue)));
        var days = await history.DailyAsync(first, today, zone, cancellationToken);
        var total = await history.WithinAsync(start, now, cancellationToken);

        return new UsageRange(span, first, today, total, days);
    }

    public static int Length(UsageSpan span) => span switch
    {
        UsageSpan.Today => 1,
        UsageSpan.LastSevenDays => 7,
        _ => 30,
    };
}
