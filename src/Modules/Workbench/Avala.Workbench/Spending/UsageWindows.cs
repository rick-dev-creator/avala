using Avala.Observability.Contracts;

namespace Avala.Workbench.Spending;

internal enum UsageSpan
{
    Today,
    LastSevenDays,
}

internal sealed record UsageWindow(UsageSpan Span, UsagePeriod Period);

internal sealed class UsageWindows(IUsageHistory history, TimeProvider clock)
{
    public async ValueTask<IReadOnlyList<UsageWindow>> ReadAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, clock.LocalTimeZone).DateTime);
        var days = await history.DailyAsync(today, today, clock.LocalTimeZone, cancellationToken);
        var week = await history.WithinAsync(now.AddDays(-7), now, cancellationToken);

        return [.. days.Select(day => new UsageWindow(UsageSpan.Today, day)), new UsageWindow(UsageSpan.LastSevenDays, week)];
    }
}
