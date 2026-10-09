using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Resources.Tracking;
using Avala.Resources.Usage;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Workspaces.Contracts;

namespace Avala.Resources.Housekeeping;

internal sealed class WorktreeHousekeeper(IWorkspaces workspaces, IResourceSettings settings, IEventBus bus, TimeProvider clock)
    : IWorktreeHousekeeping, IStartupTask
{
    private readonly List<Retained> retained = [];
    private ImmutableList<ReclaimedWorktree> reclaimed = [];

    public IReadOnlyList<ReclaimedWorktree> Reclaimed() => Volatile.Read(ref reclaimed);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if ((await settings.LoadAsync(cancellationToken)).Reconcile == ReconcilePolicy.Clean)
        {
            _ = await CleanAsync(cancellationToken);
        }
        else
        {
            _ = await ReconcileAsync(cancellationToken);
        }
    }

    public async ValueTask<WorktreeReconciliation> ReconcileAsync(CancellationToken cancellationToken)
    {
        var found = await workspaces.ReconcileAsync(cancellationToken);
        await bus.PublishAsync(new WorktreesReconciled(found, Cleaned: false), cancellationToken);

        return found;
    }

    public async ValueTask<WorktreeReconciliation> CleanAsync(CancellationToken cancellationToken)
    {
        var cleaned = await workspaces.CleanAsync(await workspaces.ReconcileAsync(cancellationToken), cancellationToken);
        await bus.PublishAsync(new WorktreesReconciled(cleaned, Cleaned: true), cancellationToken);

        return cleaned;
    }

    public async Task RetainAsync(JobId job, JobStatus status, IReadOnlyList<string> worktrees, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var kept = (await settings.LoadAsync(cancellationToken)).Retention.KeptFor(status);

        retained.AddRange(kept.Match<IEnumerable<Retained>>(
            span => worktrees.Select(worktree => new Retained(job, worktree, status, now + span)),
            () => []));

        await SweepAsync(cancellationToken);
    }

    public async Task SweepAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        foreach (var due in retained.Where(kept => kept.Due <= now).ToList())
        {
            var removed = await workspaces.FindAtAsync(due.Worktree, cancellationToken).AsTask()
                .BindAsync(found => workspaces.RemoveAsync(found.Id, cancellationToken).AsTask());

            if (removed.IsSuccess || removed.Match(_ => false, failure => failure == WorkspaceFailure.UnknownWorkspace))
            {
                retained.Remove(due);
            }

            if (removed.IsSuccess)
            {
                var reclaim = new ReclaimedWorktree(due.Job, due.Worktree, due.Status, now);
                ImmutableInterlocked.Update(ref reclaimed, known => known.Add(reclaim));
                await bus.PublishAsync(new WorktreeReclaimed(reclaim), cancellationToken);
            }
        }
    }

    private sealed record Retained(JobId Job, string Worktree, JobStatus Status, DateTimeOffset Due);
}
