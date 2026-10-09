using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Workbench.Board;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Upkeep;

internal sealed record TreeState(TreeUsage Tree, Option<BoardJob> Job);

internal sealed record ResourceState(
    ResourceUsage Global,
    IReadOnlyList<TreeState> Trees,
    IReadOnlyList<OrphanReport> Orphans,
    IReadOnlyList<PortLease> Leases,
    IReadOnlyList<PortConflict> Conflicts,
    WorktreeReconciliation Stale)
{
    public IReadOnlyList<OrphanReport> LeftRunning => [.. Orphans.Where(report => report.Disposal == OrphanDisposal.LeftRunning)];

    public int Leftovers => LeftRunning.Count + Stale.Strays.Count + Stale.Missing.Count;
}

internal sealed class Leftovers : IHandle<WorktreesReconciled>
{
    private WorktreeReconciliation stale = new([], []);

    public WorktreeReconciliation Stale => Volatile.Read(ref stale);

    public ValueTask HandleAsync(WorktreesReconciled integrationEvent, CancellationToken cancellationToken)
    {
        Volatile.Write(ref stale, integrationEvent.Cleaned ? new WorktreeReconciliation([], []) : integrationEvent.Found);

        return ValueTask.CompletedTask;
    }
}

internal sealed class ResourceReader(IResources resources, IOrphans orphans, Leftovers leftovers, JobBoard board)
{
    public ResourceState Read() =>
        new(
            resources.Global(),
            resources.Latest.Match(
                sample => sample.Trees.Select(tree => new TreeState(tree, tree.Job.Bind(board.Find))).ToList(),
                () => []),
            [.. orphans.Audit().GroupBy(report => report.Tree).Select(reports => reports.Last()).OrderByDescending(report => report.At)],
            resources.Leases(),
            resources.Conflicts(),
            leftovers.Stale);
}

internal sealed class Housekeeping(IOrphans orphans, IWorktreeHousekeeping worktrees)
{
    public async ValueTask<Result<int, ResourceError>> ReapAsync(JobId job, CancellationToken cancellationToken) =>
        (await orphans.ReapAsync(job, cancellationToken)).Map(reaped => reaped.Count);

    public ValueTask<WorktreeReconciliation> ReconcileAsync(CancellationToken cancellationToken) => worktrees.ReconcileAsync(cancellationToken);

    public ValueTask<WorktreeReconciliation> CleanAsync(CancellationToken cancellationToken) => worktrees.CleanAsync(cancellationToken);
}
