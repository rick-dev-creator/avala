using Avala.Autopilot.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Triggers.Catalog;
using Avala.Triggers.Declarations;
using Avala.Triggers.Looping;

namespace Avala.Triggers.Firing;

internal sealed class TriggerDispatch(IJobs jobs, IEnumerable<IAutopilot> autopilots, LoopQueue queue)
{
    public async Task<Result<JobId, JobRejection>> SubmitAsync(TriggerDeclaration trigger, string instruction, Autonomy autonomy, CancellationToken cancellationToken) =>
        await jobs.SubmitAsync(
            new JobRequest(trigger.Repository, instruction, trigger.Attempts) { Autonomy = autonomy, Connection = trigger.Connection },
            cancellationToken);

    public async Task<Result<Guid, JobRejection>> EnqueueAsync(Guid run, TriggerDeclaration trigger, string instruction, Autonomy autonomy, CancellationToken cancellationToken)
    {
        var autopilot = autopilots.FirstOrDefault();

        if (autopilot is null)
        {
            return JobRejection.InvalidRequest;
        }

        queue.Add(new QueuedTask(run, trigger.Id, new SourcedTask(LoopQueue.Source, run.ToString("N"), trigger.Repository, instruction)));

        if (!autopilot.Loops().Any(loop => loop.Status != LoopStatus.Ended && Repositories.Key(loop.Repository) == trigger.Repository))
        {
            _ = await autopilot.StartAsync(
                new LoopRequest(trigger.Repository) { Connection = trigger.Connection, Autonomy = autonomy, AttemptsPerRound = trigger.Attempts },
                cancellationToken);
        }

        return run;
    }
}
