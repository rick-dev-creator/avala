using Microsoft.Extensions.Time.Testing;

namespace Avala.Host.Tests;

internal sealed class SystemTimerClock(DateTimeOffset start) : FakeTimeProvider(start)
{
    private static readonly TimeSpan LongestWait = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Supported(dueTime, nameof(dueTime));
        Supported(period, nameof(period));

        return base.CreateTimer(callback, state, dueTime, period);
    }

    private static void Supported(TimeSpan wait, string name)
    {
        if (wait != Timeout.InfiniteTimeSpan && (wait < TimeSpan.Zero || wait > LongestWait))
        {
            throw new ArgumentOutOfRangeException(name, wait, $"A system timer waits at most {LongestWait}.");
        }
    }
}
