using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Resources.Tracking;
using Avala.Resources.Usage;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Workspaces.Contracts;

namespace Avala.Resources.Housekeeping;

internal sealed class RetainedWorktrees(IWorkspaces workspaces, IWorkspaceShelf shelf, IEndedJobs jobs, IWorktreeFacts facts)
{
    public IWorkspaces Workspaces => workspaces;

    public IWorktreeFacts Facts => facts;

    public async Task<Option<Result<WorkspaceId, WorkspaceFailure>>> RemoveEndedAsync(JobId job, string worktree, CancellationToken cancellationToken)
    {
        var removed = Result<WorkspaceId, WorkspaceFailure>.Failure(WorkspaceFailure.UnknownWorkspace);
        var ended = await jobs.WhileEndedAsync(
            job,
            async token => removed = await workspaces.FindAtAsync(worktree, token).AsTask().BindAsync(found => workspaces.RemoveAsync(found.Id, token).AsTask()),
            cancellationToken);

        return ended ? removed : Option<Result<WorkspaceId, WorkspaceFailure>>.None;
    }

    public async Task<Option<Result<WorkspaceInfo, WorkspaceFailure>>> ShelveEndedAsync(JobId job, string worktree, CancellationToken cancellationToken)
    {
        var shelved = Result<WorkspaceInfo, WorkspaceFailure>.Failure(WorkspaceFailure.UnknownWorkspace);
        var ended = await jobs.WhileEndedAsync(
            job,
            async token => shelved = await workspaces.FindAtAsync(worktree, token).AsTask().BindAsync(found => shelf.ShelveAsync(found.Id, token).AsTask()),
            cancellationToken);

        return ended ? shelved : Option<Result<WorkspaceInfo, WorkspaceFailure>>.None;
    }

    public async Task<bool> ShelvedAsync(string worktree, CancellationToken cancellationToken) =>
        (await workspaces.FindAtAsync(worktree, cancellationToken)).Match(found => found.Shelved, _ => false);

    public async Task<bool> DeliveredAsync(string worktree, CancellationToken cancellationToken) =>
        (await workspaces.FindAtAsync(worktree, cancellationToken).AsTask().BindAsync(found => shelf.DeliveredAsync(found.Id, cancellationToken).AsTask()))
            .Match(delivered => delivered, _ => false);
}

internal sealed class WorktreeHousekeeper(RetainedWorktrees worktrees, IResourceSettings settings, IEventBus bus, TimeProvider clock)
    : IWorktreeHousekeeping, IStartupTask, IAsyncDisposable
{
    public static readonly TimeSpan DeliveryCheck = TimeSpan.FromMinutes(10);

    private readonly SerialExecutor owner = new();
    private readonly List<Retained> retained = [];
    private ImmutableList<ReclaimedWorktree> reclaimed = [];
    private ImmutableList<FreedWorktree> freed = [];
    private ImmutableDictionary<string, long> sizes = ImmutableDictionary<string, long>.Empty;

    public IReadOnlyList<ReclaimedWorktree> Reclaimed() => Volatile.Read(ref reclaimed);

    public IReadOnlyList<FreedWorktree> Freed() => Volatile.Read(ref freed);

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
                foreach (var worktree in worktrees.Where(worktree => !retained.Any(known => known.Worktree == worktree)))
                {
                    retained.Add(await RetainedAsync(job, status, worktree, ended, token));
                }

                await SweepOwnedAsync(orphans, token);
            },
            cancellationToken);

    public Task MarkAsync(JobId job, WorktreeMoment moment, IOrphans orphans, CancellationToken cancellationToken) =>
        owner.RunAsync(
            token =>
            {
                var now = clock.GetUtcNow();

                for (var index = 0; index < retained.Count; index++)
                {
                    if (retained[index].Job == job && !retained[index].Moments.ContainsKey(moment))
                    {
                        retained[index] = retained[index] with { Moments = retained[index].Moments.Add(moment, now) };
                    }
                }

                return SweepOwnedAsync(orphans, token);
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

    public Task SweepAsync(ResourceSample sample, IOrphans orphans, CancellationToken cancellationToken) =>
        owner.RunAsync(
            token =>
            {
                sizes = sizes.SetItems(sample.Worktrees.Select(folder => KeyValuePair.Create(folder.Path, folder.Bytes)));

                return SweepOwnedAsync(orphans, token);
            },
            cancellationToken);

    public ValueTask DisposeAsync() => owner.DisposeAsync();

    private async Task<Retained> RetainedAsync(JobId job, JobStatus status, string worktree, DateTimeOffset ended, CancellationToken cancellationToken)
    {
        var facts = worktrees.Facts;
        var delivered = status == JobStatus.Approved && (facts.Merged(job) || await worktrees.DeliveredAsync(worktree, cancellationToken));
        var moments = ImmutableDictionary<WorktreeMoment, DateTimeOffset>.Empty.Add(status.EndedAs(delivered, facts.AwaitsMerge(job)), ended);

        return new Retained(job, worktree, status, ended)
        {
            Moments = facts.Archived(job) ? moments.SetItem(WorktreeMoment.Archived, ended) : moments,
            Freed = await worktrees.ShelvedAsync(worktree, cancellationToken),
            Checked = clock.GetUtcNow(),
        };
    }

    private async Task SweepOwnedAsync(IOrphans orphans, CancellationToken cancellationToken)
    {
        var policy = (await settings.LoadAsync(cancellationToken)).Worktrees;
        var now = clock.GetUtcNow();

        foreach (var kept in retained.ToList())
        {
            var outcome = policy.DeletedAfter(kept.Status).Match(span => kept.Ended + span <= now, () => false)
                ? await DeleteAsync(kept, orphans, now, cancellationToken)
                : kept.Freed ? kept : await FreeAsync(await CheckedAsync(kept, now, cancellationToken), policy, orphans, now, cancellationToken);
            var index = retained.IndexOf(kept);
            var next = outcome.Match<Retained?>(found => found.Freed && policy.DeletedAfter(found.Status).IsNone ? null : found, () => null);

            if (next is null)
            {
                retained.RemoveAt(index);
            }
            else
            {
                retained[index] = next;
            }
        }
    }

    private async Task<Retained> CheckedAsync(Retained kept, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (kept.Status != JobStatus.Approved || kept.Moments.ContainsKey(WorktreeMoment.Delivered) || now - kept.Checked < DeliveryCheck)
        {
            return kept;
        }

        var delivered = await worktrees.DeliveredAsync(kept.Worktree, cancellationToken);

        return kept with { Checked = now, Moments = delivered ? kept.Moments.SetItem(WorktreeMoment.Delivered, now) : kept.Moments };
    }

    private async Task<Option<Retained>> FreeAsync(Retained kept, WorktreePolicy policy, IOrphans orphans, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var due = kept.Moments
            .SelectMany(moment => policy.FreeAfter(moment.Key).Match<(WorktreeMoment Moment, DateTimeOffset Due)[]>(span => [(moment.Key, moment.Value + span)], () => []))
            .OrderBy(found => found.Due)
            .FirstOrDefault();

        if (due == default || due.Due > now)
        {
            return kept;
        }

        _ = await orphans.ReapAsync(kept.Job, cancellationToken);
        var bytes = sizes.GetValueOrDefault(kept.Worktree);
        var attempt = await worktrees.ShelveEndedAsync(kept.Job, kept.Worktree, cancellationToken);

        if (attempt.IsNone)
        {
            return Option<Retained>.None;
        }

        var shelved = attempt.Match(found => found, () => WorkspaceFailure.UnknownWorkspace);

        if (shelved.IsSuccess)
        {
            var release = new FreedWorktree(kept.Job, kept.Worktree, kept.Status, due.Moment, bytes, now);
            ImmutableInterlocked.Update(ref freed, known => known.Add(release));
            await bus.PublishAsync(new WorktreeFreed(release), cancellationToken);

            return kept with { Freed = true };
        }

        return shelved.Match(_ => false, failure => failure == WorkspaceFailure.UnknownWorkspace) ? Option<Retained>.None : kept;
    }

    private async Task<Option<Retained>> DeleteAsync(Retained kept, IOrphans orphans, DateTimeOffset now, CancellationToken cancellationToken)
    {
        _ = await orphans.ReapAsync(kept.Job, cancellationToken);

        var attempt = await worktrees.RemoveEndedAsync(kept.Job, kept.Worktree, cancellationToken);

        if (attempt.IsNone)
        {
            return Option<Retained>.None;
        }

        var removed = attempt.Match(found => found, () => WorkspaceFailure.UnknownWorkspace);

        if (removed.IsSuccess)
        {
            var reclaim = new ReclaimedWorktree(kept.Job, kept.Worktree, kept.Status, now);
            ImmutableInterlocked.Update(ref reclaimed, known => known.Add(reclaim));
            await bus.PublishAsync(new WorktreeReclaimed(reclaim), cancellationToken);
        }

        return removed.IsSuccess || removed.Match(_ => false, failure => failure == WorkspaceFailure.UnknownWorkspace) ? Option<Retained>.None : kept;
    }

    private sealed record Retained(JobId Job, string Worktree, JobStatus Status, DateTimeOffset Ended)
    {
        public ImmutableDictionary<WorktreeMoment, DateTimeOffset> Moments { get; init; } = ImmutableDictionary<WorktreeMoment, DateTimeOffset>.Empty;

        public bool Freed { get; init; }

        public DateTimeOffset Checked { get; init; }
    }
}
