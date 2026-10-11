using Avala.Autopilot.Contracts;
using Avala.Sdk.Events;
using Avala.Triggers.Catalog;
using Avala.Triggers.Firing;

namespace Avala.Triggers.Looping;

internal sealed class TrippedLoops(IEnumerable<IAutopilot> autopilots, LoopQueue queue, RunJournal journal) : IHandle<BreakerTripped>
{
    public async ValueTask HandleAsync(BreakerTripped integrationEvent, CancellationToken cancellationToken)
    {
        var repositories = autopilots.Take(1)
            .SelectMany(autopilot => autopilot.Loops())
            .Where(loop => loop.Loop == integrationEvent.Loop)
            .Select(loop => Repositories.Key(loop.Repository))
            .ToHashSet(StringComparer.Ordinal);
        var left = queue.Queued
            .Where(task => repositories.Contains(task.Task.Repository) && journal.Runs.Any(run => run.Id == task.Run && run.At <= integrationEvent.Trip.At))
            .ToList();

        foreach (var task in left)
        {
            _ = await queue.TakeAsync(task.Task.Key, cancellationToken);
            await journal.DropAsync(task.Run, cancellationToken);
        }

        if (left.Count > 0)
        {
            await journal.ChangedAsync(cancellationToken);
        }
    }
}
