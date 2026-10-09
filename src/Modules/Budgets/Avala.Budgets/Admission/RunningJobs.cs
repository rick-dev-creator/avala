using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Budgets.Admission;

internal interface IMachineBudgetFile
{
    ValueTask<MachineBudget> LoadAsync(CancellationToken cancellationToken);
}

internal sealed class RunningJobs(IMachineBudgetFile machine, IEventBus bus) : IJobAdmission, IHandle<JobProgressed>, IAsyncDisposable
{
    private readonly SerialExecutor owner = new();
    private readonly HashSet<JobId> running = [];
    private readonly HashSet<JobId> ended = [];
    private readonly List<(JobId Job, TaskCompletionSource Admitted)> waiting = [];

    public async ValueTask AdmitAsync(JobId job, CancellationToken cancellationToken) =>
        await (await machine.LoadAsync(cancellationToken)).RunningJobs.Match(
            limit => WaitForSlotAsync(job, limit, cancellationToken),
            () => Task.CompletedTask);

    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        var limit = (await machine.LoadAsync(cancellationToken)).RunningJobs;
        await owner.RunAsync(
            _ =>
            {
                Progress(integrationEvent.Job, integrationEvent.Status);
                Fill(limit);

                return Task.CompletedTask;
            },
            cancellationToken);
    }

    public ValueTask DisposeAsync() => owner.DisposeAsync();

    private async Task WaitForSlotAsync(JobId job, int limit, CancellationToken cancellationToken)
    {
        var (turn, occupied) = await owner.RunAsync(_ => Task.FromResult(Request(job, limit)), cancellationToken);

        if (turn.IsCompleted)
        {
            return;
        }

        await bus.PublishAsync(new JobQueued(job, occupied, limit), cancellationToken);
        await turn.WaitAsync(cancellationToken);
        await bus.PublishAsync(new JobAdmitted(job), cancellationToken);
    }

    private (Task Turn, int Occupied) Request(JobId job, int limit)
    {
        if (ended.Contains(job))
        {
            return (Task.CompletedTask, running.Count);
        }

        if (running.Count < limit)
        {
            running.Add(job);

            return (Task.CompletedTask, running.Count);
        }

        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        waiting.Add((job, admitted));

        return (admitted.Task, running.Count);
    }

    private void Progress(JobId job, JobStatus status)
    {
        if (status is JobStatus.Running or JobStatus.Checking)
        {
            running.Add(job);
        }
        else if (status != JobStatus.Preparing)
        {
            running.Remove(job);
        }

        if (status is JobStatus.Approved or JobStatus.Discarded or JobStatus.Failed)
        {
            ended.Add(job);
        }
    }

    private void Fill(Option<int> limit)
    {
        foreach (var gone in waiting.Where(queued => ended.Contains(queued.Job)).ToList())
        {
            waiting.Remove(gone);
            gone.Admitted.TrySetResult();
        }

        while (waiting.Count > 0 && limit.Match(most => running.Count < most, () => true))
        {
            var next = waiting[0];
            waiting.RemoveAt(0);
            running.Add(next.Job);
            next.Admitted.TrySetResult();
        }
    }
}
