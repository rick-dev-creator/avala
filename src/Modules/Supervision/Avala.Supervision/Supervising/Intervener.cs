using Avala.Agents.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Supervision.Contracts;

namespace Avala.Supervision.Supervising;

internal sealed class Intervener(SupervisionBook book, IJobs jobs, IEventBus bus, TimeProvider clock)
{
    public Task StalledAsync(JobId job, SilenceMeasure silence, CancellationToken cancellationToken) =>
        InterveneAsync(job, HoldReason.Stalled, silence, Option<SessionEnding>.None, cancellationToken);

    public Task LostAsync(JobId job, SessionEnding ending, CancellationToken cancellationToken) =>
        InterveneAsync(job, HoldReason.SessionLost, Option<SilenceMeasure>.None, ending, cancellationToken);

    private async Task InterveneAsync(
        JobId job,
        HoldReason reason,
        Option<SilenceMeasure> silence,
        Option<SessionEnding> ending,
        CancellationToken cancellationToken)
    {
        if (!(await jobs.HoldAsync(job, reason, cancellationToken)).TryGetValue(out var hold, out _))
        {
            return;
        }

        var intervention = new SupervisionIntervention(hold, silence, ending, clock.GetUtcNow());
        book.Record(intervention);
        await bus.PublishAsync(new SupervisorIntervened(intervention), cancellationToken);
    }
}
