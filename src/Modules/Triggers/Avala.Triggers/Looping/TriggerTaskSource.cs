using System.Collections.Immutable;
using Avala.Autopilot.Contracts;
using Avala.Sdk;
using Avala.Triggers.Catalog;
using Avala.Triggers.Contracts;
using Avala.Triggers.Firing;

namespace Avala.Triggers.Looping;

internal sealed record QueuedTask(Guid Run, TriggerId Trigger, SourcedTask Task);

internal sealed class LoopQueue
{
    public const string Source = "trigger";

    private ImmutableList<QueuedTask> queued = [];

    public IReadOnlyList<QueuedTask> Queued => Volatile.Read(ref queued);

    public int CountOf(TriggerId trigger) => Queued.Count(task => task.Trigger == trigger);

    public void Add(QueuedTask task) => ImmutableInterlocked.Update(ref queued, known => known.Add(task));

    public Option<QueuedTask> Take(string key)
    {
        var taken = Option<QueuedTask>.None;
        ImmutableInterlocked.Update(ref queued, known =>
        {
            var found = known.FirstOrDefault(task => task.Task.Key == key);
            taken = found.ToOption();

            return found is null ? known : known.Remove(found);
        });

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
        foreach (var taken in queue.Take(task.Key).Match<IEnumerable<QueuedTask>>(found => [found], () => []))
        {
            await mark.Job.Match(job => journal.AttachAsync(taken.Run, job, cancellationToken), () => Task.CompletedTask);
        }
    }
}
