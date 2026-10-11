using Avala.Autopilot.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Triggers.Catalog;
using Avala.Triggers.Firing;

namespace Avala.Triggers.Looping;

internal sealed class TrippedLoops(IEnumerable<IAutopilot> autopilots, LoopQueue queue, RunJournal journal) : IHandle<BreakerTripped>
{
    public ValueTask HandleAsync(BreakerTripped integrationEvent, CancellationToken cancellationToken) =>
        new(DropAsync([integrationEvent], cancellationToken));

    public Task SweepAsync(CancellationToken cancellationToken) => DropAsync([.. autopilots.Take(1).SelectMany(Tripped)], cancellationToken);

    private async Task DropAsync(IReadOnlyList<BreakerTripped> trips, CancellationToken cancellationToken)
    {
        var loops = autopilots.Take(1).SelectMany(autopilot => autopilot.Loops()).ToDictionary(loop => loop.Loop, loop => Repositories.Key(loop.Repository));
        var left = queue.Queued
            .Where(task => trips.Any(trip => loops.TryGetValue(trip.Loop, out var repository) && repository == task.Task.Repository && QueuedBefore(task, trip.Trip.At)))
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

    private static IEnumerable<BreakerTripped> Tripped(IAutopilot autopilot) =>
        autopilot.Loops()
            .Where(loop => loop.Ending == Option<LoopEnding>.Some(LoopEnding.BreakerTripped))
            .SelectMany(loop => autopilot.DigestOf(loop.Loop)
                .Match<IEnumerable<BreakerTrip>>(digest => digest.Breakers.TakeLast(1), () => [])
                .Select(trip => new BreakerTripped(loop.Loop, trip)));

    private bool QueuedBefore(QueuedTask task, DateTimeOffset tripped) => journal.Runs.Any(run => run.Id == task.Run && run.At <= tripped);
}
