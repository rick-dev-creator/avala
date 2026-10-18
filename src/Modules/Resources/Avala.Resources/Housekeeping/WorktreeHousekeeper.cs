using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Resources.Tracking;
using Avala.Resources.Usage;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Workspaces.Contracts;

namespace Avala.Resources.Housekeeping;

internal sealed class RetainedWorktrees(IWorkspaces workspaces, IEndedJobs jobs)
{
    public IWorkspaces Workspaces => workspaces;

    public async Task<Option<Result<WorkspaceId, WorkspaceFailure>>> RemoveEndedAsync(JobId job, string worktree, CancellationToken cancellationToken)
    {
        var removed = Result<WorkspaceId, WorkspaceFailure>.Failure(WorkspaceFailure.UnknownWorkspace);
        var ended = await jobs.WhileEndedAsync(
            job,
            async token => removed = await workspaces.FindAtAsync(worktree, token).AsTask().BindAsync(found => workspaces.RemoveAsync(found.Id, token).AsTask()),
            cancellationToken);

        return ended ? removed : Option<Result<WorkspaceId, WorkspaceFailure>>.None;
    }
}

internal sealed class WorktreeHousekeeper(RetainedWorktrees worktrees, IResourceSettings settings, IEventBus bus, TimeProvider clock)
    : IWorktreeHousekeeping, IStartupTask, IAsyncDisposable
{
    private readonly SerialExecutor owner = new();
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
        var found = await worktrees.Workspaces.ReconcileAsync(cancellationToken);
        await bus.PublishAsync(new WorktreesReconciled(found, Cleaned: false), cancellationToken);

        return found;
    }

    public async ValueTask<WorktreeReconciliation> CleanAsync(CancellationToken cancellationToken)
    {
        var cleaned = await worktrees.Workspaces.CleanAsync(await worktrees.Workspaces.ReconcileAsync(cancellationToken), cancellationToken);
        await bus.PublishAsync(new WorktreesReconciled(cleaned, Cleaned: true), cancellationToken);

        return cleaned;
    }

    public Task RetainAsync(JobId job, JobStatus status, IReadOnlyList<string> worktrees, IOrphans orphans, CancellationToken cancellationToken) =>
        RetainAsync(job, status, worktrees, clock.GetUtcNow(), orphans, cancellationToken);

    public Task RetainAsync(JobId job, JobStatus status, IReadOnlyList<string> worktrees, DateTimeOffset ended, IOrphans orphans, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async token =>
            {
                var kept = (await settings.LoadAsync(token)).Retention.KeptFor(status);

                retained.AddRange(kept.Match<IEnumerable<Retained>>(
                    span => worktrees.Where(worktree => !retained.Any(known => known.Worktree == worktree)).Select(worktree => new Retained(job, worktree, status, ended + span)),
                    () => []));

                await SweepOwnedAsync(orphans, token);
            },
            cancellationToken);

    public Task ReleaseAsync(JobId job, CancellationToken cancellationToken) =>
        owner.RunAsync(
            _ =>
            {
                retained.RemoveAll(kept => kept.Job == job);

                return Task.CompletedTask;
            },
            cancellationToken);

    public Task SweepAsync(IOrphans orphans, CancellationToken cancellationToken) =>
        owner.RunAsync(token => SweepOwnedAsync(orphans, token), cancellationToken);

    public ValueTask DisposeAsync() => owner.DisposeAsync();

    private async Task SweepOwnedAsync(IOrphans orphans, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        foreach (var due in retained.Where(kept => kept.Due <= now).ToList())
        {
            _ = await orphans.ReapAsync(due.Job, cancellationToken);

            var attempt = await worktrees.RemoveEndedAsync(due.Job, due.Worktree, cancellationToken);

            if (attempt.IsNone)
            {
                retained.Remove(due);
                continue;
            }

            var removed = attempt.Match(found => found, () => WorkspaceFailure.UnknownWorkspace);

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
