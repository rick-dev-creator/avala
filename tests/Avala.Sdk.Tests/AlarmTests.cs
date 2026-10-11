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
}
