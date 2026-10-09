using Avala.Jobs.Contracts;
using Avala.Sdk.Events;
using Avala.Supervision.Contracts;

namespace Avala.Supervision.Supervising;

internal sealed class Intervener(SupervisionBook book, IJobs jobs, IEventBus bus, TimeProvider clock)
{
    public async Task StalledAsync(JobId job, SilenceMeasure silence, CancellationToken cancellationToken)
    {
        if (!(await jobs.HoldAsync(job, HoldReason.Stalled, cancellationToken)).TryGetValue(out var hold, out _))
        {
            return;
        }

        var intervention = new SupervisionIntervention(hold, silence, clock.GetUtcNow());
        await book.RecordAsync(intervention, cancellationToken);
        await bus.PublishAsync(new SupervisorIntervened(intervention), cancellationToken);
    }
}
