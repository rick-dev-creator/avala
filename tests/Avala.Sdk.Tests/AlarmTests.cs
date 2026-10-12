using Microsoft.Extensions.Time.Testing;

namespace Avala.Sdk.Tests;

public sealed class AlarmTests
{
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero));
    private int rings;

    [Fact]
    public void AChangedAlarmRingsOnlyAtItsNewDueTime()
    {
        using var alarm = clock.CreateAlarm(() => rings++, TimeSpan.FromMinutes(10));

        var changed = alarm.Change(TimeSpan.FromMinutes(30), Timeout.InfiniteTimeSpan);
        clock.Advance(TimeSpan.FromMinutes(29));
        var early = rings;
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal((true, 0, 1), (changed, early, rings));
    }

    [Fact]
    public void AnAlarmRingsWhenTheClockReachesItsDueTimeThoughReadingTheClockMovesIt()
    {
        var moving = new FakeTimeProvider(clock.GetUtcNow()) { AutoAdvanceAmount = TimeSpan.FromTicks(1) };
        var due = moving.GetUtcNow() + TimeSpan.FromMinutes(1);
        using var alarm = moving.CreateAlarm(() => rings++, due);

        moving.SetUtcNow(due);

        Assert.Equal(1, rings);
    }

    [Fact]
    public void AnAlarmWhoseTimerFiresEarlyBeforeItIsKeptStillRingsAtItsDueTime()
    {
        var hasty = new HastyClock(clock.GetUtcNow());
        var due = hasty.GetUtcNow() + TimeSpan.FromMinutes(1);
        using var alarm = hasty.CreateAlarm(() => rings++, due);

        hasty.SetUtcNow(due);

        Assert.Equal(1, rings);
    }

    [Fact]
    public void AnAlarmChangedToAnInfiniteWaitNeverRings()
    {
        using var alarm = clock.CreateAlarm(() => rings++, TimeSpan.FromMinutes(10));

        var changed = alarm.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        clock.Advance(TimeSpan.FromDays(1));

        Assert.Equal((true, 0), (changed, rings));
    }

    [Fact]
    public void ADisposedAlarmCannotBeChangedAndNeverRings()
    {
        var alarm = clock.CreateAlarm(() => rings++, TimeSpan.FromMinutes(10));
        alarm.Dispose();

        var changed = alarm.Change(TimeSpan.FromMinutes(1), Timeout.InfiniteTimeSpan);
        clock.Advance(TimeSpan.FromDays(1));

        Assert.Equal((false, 0), (changed, rings));
    }

    [Fact]
    public void AnAlarmRingsOnceSoItTakesNoPeriod()
    {
        using var alarm = clock.CreateAlarm(() => rings++, TimeSpan.FromMinutes(10));

        Assert.Throws<NotSupportedException>(() => alarm.Change(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1)));
    }

    private sealed class HastyClock(DateTimeOffset start) : FakeTimeProvider(start)
    {
        private bool hurried;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);

            if (!hurried)
            {
                hurried = true;
                Advance(dueTime);
            }

            return timer;
        }
    }
}
