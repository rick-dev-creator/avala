using Avala.Components.UI.Elapsed;
using Avala.Testing.UI;
using Avalonia.Controls;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Components.UI.Tests;

public sealed class ElapsedTextScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AShownElapsedTextCountsOnEverySecondAsync() =>
        ui.RunAsync(() =>
        {
            var clock = new RecordingClock(DateTimeOffset.UnixEpoch.AddSeconds(23));
            var (elapsed, view) = Shown(clock);

            clock.Advance(TimeSpan.FromSeconds(1));
            view.Settle();

            Assert.Equal("24s", elapsed.Text);
            Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)], clock.Dues);
        }, Cancellation);

    [Fact]
    public Task PastAnHourAnElapsedTextWakesOnlyWhenItsMinuteTurnsAsync() =>
        ui.RunAsync(() =>
        {
            var clock = new RecordingClock(DateTimeOffset.UnixEpoch.AddSeconds(3730));
            var (elapsed, view) = Shown(clock);

            clock.Advance(TimeSpan.FromSeconds(50));
            view.Settle();

            Assert.Equal("1h 3m", elapsed.Text);
            Assert.Equal([TimeSpan.FromSeconds(50), TimeSpan.FromMinutes(1)], clock.Dues);
        }, Cancellation);

    [Fact]
    public Task AnElapsedTextStopsTickingWhileItsParentIsHiddenAndCatchesUpWhenShownAsync() =>
        ui.RunAsync(() =>
        {
            var clock = new RecordingClock(DateTimeOffset.UnixEpoch.AddSeconds(23));
            var (elapsed, view) = Shown(clock);
            var parent = (Control)elapsed.Parent!;

            parent.IsVisible = false;
            view.Settle();
            var hidden = clock.Armed;
            clock.Advance(TimeSpan.FromSeconds(5));
            parent.IsVisible = true;
            view.Settle();

            Assert.Equal((0, 1), (hidden, clock.Armed));
            Assert.Equal("28s", elapsed.Text);
        }, Cancellation);

    private static (ElapsedText Elapsed, ViewScript View) Shown(RecordingClock clock)
    {
        var elapsed = new ElapsedText { Since = DateTimeOffset.UnixEpoch };
        var parent = new Border { Child = elapsed };
        ElapsedText.SetClock(parent, clock);

        return (elapsed, ViewScript.Show(parent, new object()));
    }

    private sealed class RecordingClock(DateTimeOffset start) : TimeProvider
    {
        private readonly FakeTimeProvider time = new(start);

        public List<TimeSpan> Dues { get; } = [];

        public int Armed { get; private set; }

        public void Advance(TimeSpan by) => time.Advance(by);

        public override DateTimeOffset GetUtcNow() => time.GetUtcNow();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Dues.Add(dueTime);
            Armed++;

            return new Counted(time.CreateTimer(callback, state, dueTime, period), () => Armed--);
        }

        private sealed class Counted(ITimer timer, Action disposed) : ITimer
        {
            private bool done;

            public bool Change(TimeSpan dueTime, TimeSpan period) => timer.Change(dueTime, period);

            public void Dispose()
            {
                timer.Dispose();

                if (!done)
                {
                    done = true;
                    disposed();
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();

                return ValueTask.CompletedTask;
            }
        }
    }
}
