using System.Collections.Immutable;
using Avala.Agents.Contracts.Connections;
using Avala.Autopilot.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Triggers.Catalog;
using Avala.Triggers.Contracts;
using Avala.Triggers.Firing;

namespace Avala.Triggers.Looping;

internal sealed record QueuedTask(Guid Run, TriggerId Trigger, SourcedTask Task)
{
    public Option<ConnectionName> Connection { get; init; }

    public Autonomy Autonomy { get; init; }

    public int Attempts { get; init; } = 3;

    public LoopRequest Loop => new(Task.Repository) { Connection = Connection, Autonomy = Autonomy, AttemptsPerRound = Attempts };
}

internal interface IQueueStore
{
    Task<IReadOnlyList<QueuedTask>> QueuedAsync(CancellationToken cancellationToken);

    Task QueueAsync(QueuedTask task, CancellationToken cancellationToken);

    Task TakeAsync(Guid run, CancellationToken cancellationToken);
}

internal sealed class LoopQueue(IQueueStore store)
{
    public const string Source = "trigger";

    private ImmutableList<QueuedTask> queued = [];

    public IReadOnlyList<QueuedTask> Queued => Volatile.Read(ref queued);

    public int CountOf(TriggerId trigger) => Queued.Count(task => task.Trigger == trigger);

    public bool Holds(Guid run) => Queued.Any(task => task.Run == run);

    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        var stored = await store.QueuedAsync(cancellationToken);
        ImmutableInterlocked.Update(ref queued, known => known.AddRange(stored.Where(task => !known.Any(other => other.Run == task.Run))));
    }

    public async Task AddAsync(QueuedTask task, CancellationToken cancellationToken)
    {
        await store.QueueAsync(task, cancellationToken);
        ImmutableInterlocked.Update(ref queued, known => known.Add(task));
    }

    public async Task<Option<QueuedTask>> TakeAsync(string key, CancellationToken cancellationToken)
    {
        var taken = Option<QueuedTask>.None;
        ImmutableInterlocked.Update(ref queued, known =>
        {
            var found = known.FirstOrDefault(task => task.Task.Key == key);
            taken = found.ToOption();

            return found is null ? known : known.Remove(found);
        });

        foreach (var found in taken.Match<QueuedTask[]>(task => [task], () => []))
        {
            await store.TakeAsync(found.Run, cancellationToken);
        }

        return taken;
    }
}

internal sealed class TriggerTaskSource(LoopQueue queue, RunJournal journal) : IJobSource
{
    public string Name => LoopQueue.Source;

    public ValueTask<Result<SourceAnswer, AutopilotError>> NextAsync(SourceRequest request, CancellationToken cancellationToken)
    {
        var repository = Repositories.Key(request.Repository);
        var next = queue.Queued.FirstOrDefault(task => task.Task.Repository == repository).ToOption();

        return ValueTask.FromResult(Result<SourceAnswer, AutopilotError>.Success(
            next.Match(task => new SourceAnswer(task.Task, Option<DateTimeOffset>.None), () => SourceAnswer.Nothing)));
    }

    public async ValueTask MarkAsync(SourcedTask task, TaskMark mark, CancellationToken cancellationToken)
    {
        foreach (var taken in (await queue.TakeAsync(task.Key, cancellationToken)).Match<IEnumerable<QueuedTask>>(found => [found], () => []))
        {
            await mark.Job.Match(job => journal.AttachAsync(taken.Run, job, cancellationToken), () => Task.CompletedTask);
        }
    }
}
