using Avala.Server.Activity.Domain;

namespace Avala.Server.Activity.Tests.Domain;

public sealed class EventTimeTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ATimeWithAnOffsetIsKeptAsTheSameInstantInUtc()
    {
        var time = EventTime.Create(new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.FromHours(9)), Noon).Value;

        Assert.Equal((new DateTimeOffset(2026, 10, 25, 1, 0, 0, TimeSpan.Zero), TimeSpan.Zero), (time.At, time.At.Offset));
    }

    [Fact]
    public void ATimeAfterTheReceptionIsClampedToIt() =>
        Assert.Equal(new EventTime(Noon, Clamped: true), EventTime.Create(Noon.AddHours(3), Noon).Value);

    [Fact]
    public void ATimeBeforeTheReceptionIsKept() =>
        Assert.Equal(new EventTime(Noon.AddMinutes(-5), Clamped: false), EventTime.Create(Noon.AddMinutes(-5), Noon).Value);

    [Fact]
    public void ATimeBefore2025IsRefused() =>
        Assert.Equal("the time is before 2025", EventTime.Create(new DateTimeOffset(2024, 12, 31, 23, 59, 59, TimeSpan.Zero), Noon).Refusal);
}
