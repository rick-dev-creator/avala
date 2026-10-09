using Avala.Agents.Contracts.Events;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Following;
using Avala.Workbench.Spending;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Workbench.Tests.Spending;

public sealed class LimitReadingsTests
{
    private static readonly DateTimeOffset Nine = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider clock = new(Nine);
    private readonly Pulse pulse = new(new JobBoard());

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void AReadingIsExpiredOnceItsResetTimeHasPassedOnTheClockAndAReadingWithoutAResetNeverIs()
    {
        using var readings = new LimitReadings(new FakeUsage(), pulse, clock);
        UsageLimit[] limits =
        [
            new("5h", 0.95, Nine.AddMinutes(-1)),
            new("weekly", 0.4, Nine.AddDays(2)),
            new("usage", 0.7, Option<DateTimeOffset>.None),
        ];

        var judged = readings.Judged(limits);

        Assert.Equal([true, false, false], judged.Select(reading => reading.Expired));
        Assert.Equal([0, 0.4, 0.7], judged.Select(reading => reading.Used));
    }

    [Fact]
    public async Task APageFollowingThePulseIsToldWhenTheEarliestCurrentWindowResetsAsync()
    {
        using var readings = new LimitReadings(new FakeUsage(), pulse, clock);
        using var following = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        await using var changes = pulse.ChangesAsync(following.Token).GetAsyncEnumerator(Cancellation);
        Assert.True(await changes.MoveNextAsync());

        _ = readings.Judged([new UsageLimit("weekly", 0.4, Nine.AddDays(2)), new UsageLimit("5h", 0.95, Nine.AddSeconds(2))]);
        clock.Advance(TimeSpan.FromSeconds(1));
        var early = changes.MoveNextAsync();
        Assert.False(early.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.True(await early.AsTask().WaitAsync(TimeSpan.FromSeconds(30), Cancellation));
        Assert.Equal([true, false], readings.Judged([new UsageLimit("5h", 0.95, Nine.AddSeconds(2)), new UsageLimit("weekly", 0.4, Nine.AddDays(2))]).Select(reading => reading.Expired));
        await following.CancelAsync();
    }
}
