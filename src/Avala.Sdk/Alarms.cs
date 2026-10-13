namespace Avala.Sdk;

public static class Alarms
{
    public static TimeSpan LongestTimerWait { get; } = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    extension(TimeProvider clock)
    {
        public ITimer CreateAlarm(Action ring, TimeSpan wait) =>
            clock.CreateAlarm(ring, After(clock.GetUtcNow(), wait));

        public ITimer CreateAlarm(Action ring, DateTimeOffset due)
        {
            var alarm = new Alarm(clock, ring, due);
            alarm.Arm();

            return alarm;
        }
    }

    private static DateTimeOffset After(DateTimeOffset now, TimeSpan wait) =>
        wait <= TimeSpan.Zero ? now
        : wait >= DateTimeOffset.MaxValue - now ? DateTimeOffset.MaxValue
        : now + wait;

    private sealed class Alarm(TimeProvider clock, Action ring, DateTimeOffset due) : ITimer
    {
        private static readonly TimeSpan Lead = TimeSpan.FromMilliseconds(1);
        private long dueTicks = due.UtcTicks;
        private ITimer? current;
        private int disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (period != Timeout.InfiniteTimeSpan)
            {
                throw new NotSupportedException("An alarm rings once, so it takes no period.");
            }

            if (Volatile.Read(ref disposed) == 1)
            {
                return false;
            }

            if (dueTime == Timeout.InfiniteTimeSpan)
            {
                Replace(null);

                return true;
            }

            Volatile.Write(ref dueTicks, After(clock.GetUtcNow(), dueTime).UtcTicks);
            Arm();

            return true;
        }

        public void Arm()
        {
            var previous = Volatile.Read(ref current);
            var wait = Wait(Left);
            var next = clock.CreateTimer(Ring, null, wait, Timeout.InfiniteTimeSpan);

            if (Interlocked.CompareExchange(ref current, next, previous) != previous)
            {
                next.Dispose();
                Arm();

                return;
            }

            previous?.Dispose();

            if (Volatile.Read(ref disposed) == 1)
            {
                Interlocked.Exchange(ref current, null)?.Dispose();

                return;
            }

            if (Overslept(wait))
            {
                Arm();
            }
        }

        public void Dispose()
        {
            Volatile.Write(ref disposed, 1);
            Interlocked.Exchange(ref current, null)?.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            Volatile.Write(ref disposed, 1);

            if (Interlocked.Exchange(ref current, null) is { } timer)
            {
                await timer.DisposeAsync();
            }
        }

        private static TimeSpan Wait(TimeSpan left) =>
            left > LongestTimerWait ? LongestTimerWait
            : left > Lead ? left - Lead
            : left > TimeSpan.Zero ? left
            : TimeSpan.Zero;

        private bool Overslept(TimeSpan wait)
        {
            var left = Left;

            return wait > TimeSpan.Zero && (left <= TimeSpan.Zero || wait - left > Lead);
        }

        private TimeSpan Left => new DateTimeOffset(Volatile.Read(ref dueTicks), TimeSpan.Zero) - clock.GetUtcNow();

        private void Ring(object? ignored)
        {
            if (Volatile.Read(ref disposed) == 1)
            {
                return;
            }

            if (Left > TimeSpan.Zero)
            {
                Arm();

                return;
            }

            ring();
        }

        private void Replace(ITimer? next)
        {
            Interlocked.Exchange(ref current, next)?.Dispose();

            if (Volatile.Read(ref disposed) == 1)
            {
                Interlocked.Exchange(ref current, null)?.Dispose();
            }
        }
    }
}
