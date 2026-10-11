using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Supervision.Contracts;

namespace Avala.Supervision.Supervising;

internal sealed class SilenceAlarms(TimeProvider clock, IEventBus bus) : IAsyncDisposable
{
    private readonly Dictionary<JobId, ITimer> pending = [];

    public DateTimeOffset Now => clock.GetUtcNow();

    public void Set(JobId job, DateTimeOffset due)
    {
        if (pending.ContainsKey(job))
        {
            return;
        }

        pending[job] = clock.CreateAlarm(
            () => _ = bus.PublishAsync(new SilenceNoticed(job), CancellationToken.None).AsTask(),
            due);
    }

    public async ValueTask RangAsync(JobId job)
    {
        if (pending.Remove(job, out var rung))
        {
            await rung.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var timer in pending.Values)
        {
            await timer.DisposeAsync();
        }

        pending.Clear();
    }
}
