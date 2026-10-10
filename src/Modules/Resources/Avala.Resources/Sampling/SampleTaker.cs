using Avala.Resources.Contracts;
using Avala.Resources.Leases;
using Avala.Resources.Tracking;
using Avala.Resources.Usage;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.Processes;

namespace Avala.Resources.Sampling;

internal sealed class SampleTaker(ProcessReadings readings, ResourceBook book, IFolderSizes folders, IEventBus bus)
{
    private readonly Dictionary<ProcessTreeId, (DateTimeOffset At, IReadOnlyDictionary<int, TimeSpan> Cpu)> previous = [];
    private readonly HashSet<(int Port, string Worktree, int? Process)> reported = [];
    private (IReadOnlyList<FolderUsage> Worktrees, long Data) disks = ([], 0);

    public async Task TakeAsync(DateTimeOffset at, bool measureDisks, CancellationToken cancellationToken)
    {
        var attribution = book.Attribution;
        var (trees, listeners) = await readings.ReadOpenAsync(cancellationToken);
        var usage = trees.Select(read => attribution.Attribute(Usage(read.Tree, ProcessReadings.Usage(read.Members, listeners), at))).ToList();

        foreach (var gone in previous.Keys.Where(tree => usage.All(used => used.Tree != tree)).ToList())
        {
            previous.Remove(gone);
        }

        if (measureDisks)
        {
            disks = await DisksAsync(attribution, cancellationToken);
        }

        var holders = usage
            .SelectMany(tree => tree.Processes.Select(process => (process.Id, tree.Tree, tree.Home)))
            .DistinctBy(held => held.Id)
            .ToDictionary(held => held.Id);
        var sample = new ResourceSample(at, usage, disks.Worktrees, disks.Data);
        book.Keep(sample);

        foreach (var (port, lease, process) in PortBook.Conflicts(
            book.Leases(),
            listeners.Select(listener => (listener.Port, listener.Process)),
            holders.ToDictionary(held => held.Key, held => held.Value.Home),
            Environment.ProcessId))
        {
            if (reported.Add((port, lease.Worktree, process.Match<int?>(id => id, () => null))))
            {
                var conflict = new PortConflict(port, lease, at)
                {
                    Process = process,
                    Tree = process.Bind(id => holders.TryGetValue(id, out var held) ? held.Tree : Option<ProcessTreeId>.None),
                };
                book.Observe(conflict);
                await bus.PublishAsync(new PortConflictObserved(conflict), cancellationToken);
            }
        }

        await bus.PublishAsync(new ResourcesSampled(sample), cancellationToken);
    }

    private TreeUsage Usage(IProcessTree tree, IReadOnlyList<ProcessUsage> processes, DateTimeOffset at)
    {
        var load = previous.TryGetValue(tree.Id, out var before) ? Tallies.Load(before.Cpu, processes, at - before.At) : 0;
        previous[tree.Id] = (at, processes.ToDictionary(process => process.Id, process => process.CpuTime));

        return new TreeUsage(tree.Id, Folders.Key(tree.Home), processes, load);
    }

    private async Task<(IReadOnlyList<FolderUsage> Worktrees, long Data)> DisksAsync(Attribution attribution, CancellationToken cancellationToken)
    {
        var worktrees = new List<FolderUsage>();

        foreach (var home in attribution.Sessions.Values.Select(facts => facts.Home).Distinct(StringComparer.Ordinal))
        {
            worktrees.Add(new FolderUsage(home, await folders.SizeAsync(home, cancellationToken)) { Job = attribution.JobAt(home) });
        }

        return (worktrees, await folders.DataFolderAsync(cancellationToken));
    }
}
