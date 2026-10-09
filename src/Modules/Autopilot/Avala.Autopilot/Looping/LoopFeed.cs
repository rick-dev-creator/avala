using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Autopilot.Looping;

internal sealed class LoopFeed(LoopRegistry registry)
    : IHandle<JobProgressed>, IHandle<JobHeld>, IHandle<PermissionDecided>, IHandle<FormDecided>
{
    public ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        Each(runner => runner.Progressed(integrationEvent));

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(JobHeld integrationEvent, CancellationToken cancellationToken)
    {
        Each(runner => runner.Held(integrationEvent.Hold));

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(PermissionDecided integrationEvent, CancellationToken cancellationToken)
    {
        LeftToAPerson(integrationEvent.Decision.Delivery, integrationEvent.Decision.Job);

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(FormDecided integrationEvent, CancellationToken cancellationToken)
    {
        LeftToAPerson(integrationEvent.Decision.Delivery, integrationEvent.Decision.Job);

        return ValueTask.CompletedTask;
    }

    private void LeftToAPerson(DecisionDelivery delivery, Option<JobId> job)
    {
        foreach (var asked in delivery == DecisionDelivery.LeftToHuman ? job.Match<JobId[]>(known => [known], () => []) : [])
        {
            Each(runner => runner.AskedAPerson(asked));
        }
    }

    private void Each(Action<LoopRunner> deliver)
    {
        foreach (var runner in registry.Running)
        {
            deliver(runner);
        }
    }
}
