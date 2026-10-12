using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Preview.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.Processes;

namespace Avala.Preview.Running;

internal sealed record Run(JobId Job, string Name, Option<ProcessTreeId> Tree);

internal sealed class RunBook(IEventBus bus)
{
    private ImmutableDictionary<JobId, Run> runs = ImmutableDictionary<JobId, Run>.Empty;

    public Option<Run> Of(JobId job) => Volatile.Read(ref runs).TryGetValue(job, out var run) ? run : Option<Run>.None;

    public Option<JobId> OwnerOf(ProcessTreeId tree) =>
        Volatile.Read(ref runs).Values.FirstOrDefault(run => run.Tree == Option<ProcessTreeId>.Some(tree)).ToOption().Map(run => run.Job);

    public ValueTask StartedAsync(Run run, CancellationToken cancellationToken)
    {
        ImmutableInterlocked.AddOrUpdate(ref runs, run.Job, run, (_, _) => run);

        return bus.PublishAsync(new PreviewRunChanged(run.Job, Running: true), cancellationToken);
    }

    public async ValueTask<Option<Run>> EndedAsync(JobId job, Func<Run, bool> which, CancellationToken cancellationToken)
    {
        var ended = Option<Run>.None;
        ImmutableInterlocked.Update(ref runs, current =>
        {
            ended = current.TryGetValue(job, out var run) && which(run) ? run : Option<Run>.None;

            return ended.IsSome ? current.Remove(job) : current;
        });

        if (ended.IsSome)
        {
            await bus.PublishAsync(new PreviewRunChanged(job, Running: false), cancellationToken);
        }

        return ended;
    }
}
