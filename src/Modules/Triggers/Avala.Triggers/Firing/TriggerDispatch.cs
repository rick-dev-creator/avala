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

        var task = new QueuedTask(run, trigger.Id, new SourcedTask(LoopQueue.Source, run.ToString("N"), trigger.Repository, instruction))
        {
            Connection = trigger.Connection,
            Autonomy = autonomy,
            Attempts = trigger.Attempts,
        };
        await queue.AddAsync(task, cancellationToken);
        await StartUnlessRunningAsync(autopilot, task, cancellationToken);

        return run;
    }

    public async Task ResumeAsync(CancellationToken cancellationToken)
    {
        foreach (var autopilot in autopilots.Take(1))
        {
            foreach (var oldest in queue.Queued.GroupBy(task => task.Task.Repository, StringComparer.Ordinal).Select(waiting => waiting.First()))
            {
                await StartUnlessRunningAsync(autopilot, oldest, cancellationToken);
            }
        }
    }

    private static async Task StartUnlessRunningAsync(IAutopilot autopilot, QueuedTask task, CancellationToken cancellationToken)
    {
        if (!autopilot.Loops().Any(loop => loop.Status != LoopStatus.Ended && Repositories.Key(loop.Repository) == task.Task.Repository))
        {
            _ = await autopilot.StartAsync(task.Loop, cancellationToken);
        }
    }
}
