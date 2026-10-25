using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Resources.History;

internal sealed class WorktreeHistory(IWorktreeHistoryStore store) : IWorktreeHistory, IHandle<ResourcesSampled>, IHandle<WorktreeFreed>, IHandle<WorktreeReclaimed>
{
    private Option<WorktreeDay> today;

    public ValueTask<WorktreeReport> ReportAsync(DateOnly from, CancellationToken cancellationToken) => new(store.ReadAsync(from, cancellationToken));

    public async ValueTask HandleAsync(ResourcesSampled integrationEvent, CancellationToken cancellationToken)
    {
        var sample = integrationEvent.Sample;
        var day = await DayAsync(Day(sample.At), cancellationToken);
        var present = sample.Worktrees.Where(folder => folder.Bytes > 0).ToList();
        var created = 0;

        foreach (var folder in present)
        {
            created += await store.SeeAsync(folder.Path, sample.At, cancellationToken) ? 1 : 0;
        }

        var next = day with
        {
            Peak = Math.Max(day.Peak, present.Sum(folder => folder.Bytes)),
            Worktrees = Math.Max(day.Worktrees, present.Count),
            Created = day.Created + created,
        };

        await KeepAsync(day, next, cancellationToken);
    }

    public async ValueTask HandleAsync(WorktreeFreed integrationEvent, CancellationToken cancellationToken)
    {
        var freed = integrationEvent.Freed;
        await RecordAsync(new WorktreeEvent(freed.Job, freed.Path, freed.Status, freed.Moment, freed.Bytes, freed.At), cancellationToken);
        var day = await DayAsync(Day(freed.At), cancellationToken);
        await KeepAsync(day, day with { Freed = day.Freed + 1, Recovered = day.Recovered + freed.Bytes }, cancellationToken);
    }

    public async ValueTask HandleAsync(WorktreeReclaimed integrationEvent, CancellationToken cancellationToken)
    {
        var reclaimed = integrationEvent.Reclaimed;
        await RecordAsync(new WorktreeEvent(reclaimed.Job, reclaimed.Path, reclaimed.Status, Option<WorktreeMoment>.None, 0, reclaimed.At), cancellationToken);
    }

    private async Task RecordAsync(WorktreeEvent recorded, CancellationToken cancellationToken)
    {
        var seen = await store.FirstSeenAsync(recorded.Path, cancellationToken);
        await store.RecordAsync(recorded with { Lifetime = seen.Map(first => recorded.At - first) }, cancellationToken);
    }

    private async Task<WorktreeDay> DayAsync(DateOnly day, CancellationToken cancellationToken)
    {
        if (today.Match(kept => kept.Day == day, () => false))
        {
            return today.Match(kept => kept, () => throw new InvalidOperationException());
        }

        var kept = (await store.DayAsync(day, cancellationToken)).Match(found => found, () => new WorktreeDay(day, 0, 0, 0, 0, 0));
        today = kept;

        return kept;
    }

    private async Task KeepAsync(WorktreeDay before, WorktreeDay after, CancellationToken cancellationToken)
    {
        today = after;

        if (after != before)
        {
            await store.SaveDayAsync(after, cancellationToken);
        }
    }

    private static DateOnly Day(DateTimeOffset at) => DateOnly.FromDateTime(at.ToLocalTime().DateTime);
}
