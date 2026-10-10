using Avala.Triggers.Declarations;

namespace Avala.Triggers.Tests.Declarations;

public sealed class ScheduleClockTests
{
    private static readonly TimeZoneInfo Zone = Zones.Madrid;

    private static readonly TriggerSchedule EveryDayAtHalfPastTwo = TriggerSchedule.Daily(new TimeOnly(2, 30), []);

    [Fact]
    public void AnIntervalFiresItsMinutesAfterItsAnchor()
    {
        var anchor = new DateTimeOffset(2026, 3, 29, 0, 45, 0, TimeSpan.Zero);

        Assert.Equal(anchor.AddMinutes(90), ScheduleClock.NextAfter(TriggerSchedule.Every(90), anchor, Zone));
    }

    [Fact]
    public void AFixedTimeFiresAtTheFirstLocalOccurrenceAfterOnAnAllowedWeekday()
    {
        var weekdays = TriggerSchedule.Daily(new TimeOnly(2, 30), [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday]);
        var fridayFire = new DateTimeOffset(2026, 10, 9, 2, 30, 0, TimeSpan.FromHours(2));

        Assert.Equal(new DateTimeOffset(2026, 10, 12, 2, 30, 0, TimeSpan.FromHours(2)), ScheduleClock.NextAfter(weekdays, fridayFire, Zone));
        Assert.Equal(fridayFire, ScheduleClock.NextAfter(weekdays, fridayFire.AddMinutes(-1), Zone));
    }

    [Fact]
    public void ATimeTheSpringGapSkipsFiresOnceShiftedByTheGap()
    {
        var saturday = new DateTimeOffset(2026, 3, 28, 2, 30, 0, TimeSpan.FromHours(1));

        var gapDay = ScheduleClock.NextAfter(EveryDayAtHalfPastTwo, saturday, Zone);
        var monday = ScheduleClock.NextAfter(EveryDayAtHalfPastTwo, gapDay, Zone);

        Assert.Equal(new DateTimeOffset(2026, 3, 29, 3, 30, 0, TimeSpan.FromHours(2)), gapDay);
        Assert.Equal(new DateTimeOffset(2026, 3, 30, 2, 30, 0, TimeSpan.FromHours(2)), monday);
    }

    [Fact]
    public void ATimeThatOccursTwiceWhenClocksFallBackFiresOnceAtItsFirstOccurrence()
    {
        var saturday = new DateTimeOffset(2026, 10, 24, 2, 30, 0, TimeSpan.FromHours(2));

        var doubled = ScheduleClock.NextAfter(EveryDayAtHalfPastTwo, saturday, Zone);
        var after = ScheduleClock.NextAfter(EveryDayAtHalfPastTwo, doubled, Zone);

        Assert.Equal(new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(2)), doubled);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 2, 30, 0, TimeSpan.FromHours(1)), after);
    }

    [Fact]
    public void ADailyTimeFiresAtTheSameLocalTimeOnBothSidesOfAChange()
    {
        var nine = TriggerSchedule.Daily(new TimeOnly(9, 0), []);
        var before = new DateTimeOffset(2026, 3, 28, 9, 0, 0, TimeSpan.FromHours(1));

        var after = ScheduleClock.NextAfter(nine, before, Zone);

        Assert.Equal(new DateTimeOffset(2026, 3, 29, 9, 0, 0, TimeSpan.FromHours(2)), after);
        Assert.Equal(TimeSpan.FromHours(23), after - before);
    }

    [Fact]
    public void ANextFireStillAheadMissesNothing()
    {
        var anchor = Triggered.Monday;

        Assert.Equal(new CatchUpPlan(0, anchor, anchor.AddHours(1)), ScheduleClock.Plan(TriggerSchedule.Every(60), anchor, anchor.AddMinutes(59), Zone));
    }

    [Fact]
    public void MissedOccurrencesAreCountedAndTheNextFireIsTheScheduleAfterNow()
    {
        var anchor = Triggered.Monday;
        var mondayNine = new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.FromHours(2));
        var nine = TriggerSchedule.Daily(new TimeOnly(9, 0), []);

        Assert.Equal(
            new CatchUpPlan(3, anchor.AddHours(3), anchor.AddHours(4)),
            ScheduleClock.Plan(TriggerSchedule.Every(60), anchor, anchor.AddHours(3).AddMinutes(10), Zone));
        Assert.Equal(
            new CatchUpPlan(3, mondayNine.AddDays(3), mondayNine.AddDays(4)),
            ScheduleClock.Plan(nine, mondayNine, mondayNine.AddDays(3).AddHours(1), Zone));
    }

    [Fact]
    public void MissedOccurrencesAreCountedUpToTheirLimitAndTheAnchorIsStillTheLatest()
    {
        var mondayNine = new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.FromHours(2));
        var now = mondayNine.AddDays(3000).AddHours(1);

        var plan = ScheduleClock.Plan(TriggerSchedule.Daily(new TimeOnly(9, 0), []), mondayNine, now, Zone);

        Assert.Equal(ScheduleClock.MostMissedCounted, plan.Missed);
        Assert.True(plan.Anchor <= now && plan.Next > now && plan.Next - plan.Anchor <= TimeSpan.FromHours(25), $"{plan.Anchor} {plan.Next}");
    }
}
