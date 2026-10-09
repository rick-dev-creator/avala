using Avala.Jobs.Contracts;
using Avala.Sdk.Events;
using Avala.Supervision.Contracts;

namespace Avala.Supervision.Supervising;

internal sealed class SilenceAlarms(TimeProvider clock, IEventBus bus) : IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly Dictionary<JobId, ITimer> pending = [];

    public DateTimeOffset Now => clock.GetUtcNow();

    public void Set(JobId job, DateTimeOffset due)
    {
        lock (gate)
        {
            if (pending.ContainsKey(job))
            {
                return;
            }

            var wait = due - clock.GetUtcNow();
            pending[job] = clock.CreateTimer(
                _ => _ = bus.PublishAsync(new SilenceNoticed(job), CancellationToken.None).AsTask(),
                null,
                wait > TimeSpan.Zero ? wait : TimeSpan.Zero,
                Timeout.InfiniteTimeSpan);
        }
    }

    public async ValueTask RangAsync(JobId job)
    {
        ITimer? rung;

        lock (gate)
        {
            pending.Remove(job, out rung);
        }

        if (rung is not null)
        {
            await rung.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<ITimer> timers;

        lock (gate)
        {
            timers = [.. pending.Values];
            pending.Clear();
        }

        foreach (var timer in timers)
        {
            await timer.DisposeAsync();
        }
    }
}
