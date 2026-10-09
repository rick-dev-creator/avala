using Avala.Agents.Contracts.Events;
using Avala.Observability.Contracts;
using Avala.Workbench.Following;

namespace Avala.Workbench.Spending;

internal sealed record LimitReading(UsageLimit Limit, bool Expired)
{
    public double Used => Expired ? 0 : Limit.UsedFraction;
}

internal sealed class LimitReadings(IUsage usage, Pulse pulse, TimeProvider clock) : IDisposable
{
    private Alarm? alarm;

    public IReadOnlyList<ConnectionUsage> ByConnection() => usage.ByConnection();

    public IReadOnlyList<LimitReading> Judged(IReadOnlyList<UsageLimit> limits)
    {
        var now = clock.GetUtcNow();
        IReadOnlyList<LimitReading> judged = [.. limits.Select(limit => new LimitReading(limit, limit.ResetsAt.Match(resets => resets <= now, () => false)))];

        foreach (var reset in judged.Where(reading => !reading.Expired).SelectMany(Resets).Order().Take(1))
        {
            WakeAt(reset, now);
        }

        return judged;
    }

    public void Dispose() => Volatile.Read(ref alarm)?.Bell.Dispose();

    private static DateTimeOffset[] Resets(LimitReading reading) =>
        reading.Limit.ResetsAt.Match<DateTimeOffset[]>(resets => [resets], () => []);

    private void WakeAt(DateTimeOffset reset, DateTimeOffset now)
    {
        if (Volatile.Read(ref alarm) is { } set && set.At > now && set.At <= reset)
        {
            return;
        }

        var bell = clock.CreateTimer(_ => pulse.Beat(), null, reset - now, Timeout.InfiniteTimeSpan);
        Interlocked.Exchange(ref alarm, new Alarm(reset, bell))?.Bell.Dispose();
    }

    private sealed record Alarm(DateTimeOffset At, ITimer Bell);
}
