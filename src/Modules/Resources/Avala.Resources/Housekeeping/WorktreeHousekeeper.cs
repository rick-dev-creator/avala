using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Resources.Reaping;
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

    public async Task<Option<Result<WorkspaceInfo, WorkspaceFailure>>> ShelveAsync(JobId job, JobStatus status, string worktree, CancellationToken cancellationToken)
    {
        var shelved = Result<WorkspaceInfo, WorkspaceFailure>.Failure(WorkspaceFailure.UnknownWorkspace);
        Func<CancellationToken, Task> work = async token => shelved = await workspaces.FindAtAsync(worktree, token).AsTask().BindAsync(found => shelf.ShelveAsync(found.Id, token).AsTask());
        var still = status.Ended()
            ? await jobs.WhileEndedAsync(job, work, cancellationToken)
            : await jobs.WhileRestingAsync(job, work, cancellationToken);

        return still ? shelved : Option<Result<WorkspaceInfo, WorkspaceFailure>>.None;
    }

    public async Task<bool> ShelvedAsync(string worktree, CancellationToken cancellationToken) =>
        (await workspaces.FindAtAsync(worktree, cancellationToken)).Match(found => found.Shelved && !Directory.Exists(found.Path), _ => false);

    public async Task<bool> DeliveredAsync(string worktree, CancellationToken cancellationToken) =>
        (await workspaces.FindAtAsync(worktree, cancellationToken).AsTask().BindAsync(found => shelf.DeliveredAsync(found.Id, cancellationToken).AsTask()))
            .Match(delivered => delivered, _ => false);
}

internal sealed class WorktreeDesk(WorktreeHousekeeper housekeeper, OrphanReaper reaper) : IWorktreeHousekeeping
{
    public IReadOnlyList<ReclaimedWorktree> Reclaimed() => housekeeper.Reclaimed();

    public IReadOnlyList<FreedWorktree> Freed() => housekeeper.Freed();

    public Option<WorktreeSpace> Shortage() => housekeeper.Shortage();

    public IReadOnlyList<WorktreeDue> Upcoming() => housekeeper.Upcoming();

    public ValueTask<IReadOnlyList<FreedWorktree>> FreeNowAsync(CancellationToken cancellationToken) => housekeeper.FreeNowAsync(reaper, cancellationToken);

    public ValueTask<WorktreeReconciliation> ReconcileAsync(CancellationToken cancellationToken) => housekeeper.ReconcileAsync(cancellationToken);

    public ValueTask<WorktreeReconciliation> CleanAsync(CancellationToken cancellationToken) => housekeeper.CleanAsync(cancellationToken);
}

internal sealed class WorktreeHousekeeper(RetainedWorktrees worktrees, IResourceSettings settings, IEventBus bus, TimeProvider clock)
    : IStartupTask, IAsyncDisposable
{
    public static readonly TimeSpan DeliveryCheck = TimeSpan.FromMinutes(10);

    private readonly SerialExecutor owner = new();
    private readonly List<Retained> retained = [];
    private readonly Dictionary<string, (JobId Job, DateTimeOffset At)> gone = new(StringComparer.Ordinal);
    private ImmutableList<ReclaimedWorktree> reclaimed = [];
    private ImmutableList<FreedWorktree> freed = [];
    private ImmutableDictionary<string, long> sizes = ImmutableDictionary<string, long>.Empty;
    private WorktreeSpace? shortage;
    private ImmutableList<WorktreeDue> upcoming = [];

    public IReadOnlyList<ReclaimedWorktree> Reclaimed() => Volatile.Read(ref reclaimed);

    public IReadOnlyList<FreedWorktree> Freed() => Volatile.Read(ref freed);

    public Option<WorktreeSpace> Shortage() => Volatile.Read(ref shortage).ToOption();

    public IReadOnlyList<WorktreeDue> Upcoming() => Volatile.Read(ref upcoming);

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
                retained.RemoveAll(kept => kept.Job == job && !kept.Status.Ended());

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
                    if (retained[index].Job == job && (moment == WorktreeMoment.Archived || retained[index].Status.Ended()) && !retained[index].Moments.ContainsKey(moment))
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

                foreach (var path in gone.Where(entry => entry.Value.Job == job).Select(entry => entry.Key).ToList())
                {
                    gone.Remove(path);
                }

                return Task.CompletedTask;
            },
            cancellationToken);

    public Task SweepAsync(IOrphans orphans, CancellationToken cancellationToken) =>
        owner.RunAsync(token => SweepOwnedAsync(orphans, token), cancellationToken);

    public Task SweepAsync(ResourceSample sample, IOrphans orphans, CancellationToken cancellationToken) =>
        owner.RunAsync(
            async token =>
            {
                sizes = sizes.SetItems(sample.Worktrees.Select(folder => KeyValuePair.Create(folder.Path, folder.Bytes)));
                await SweepOwnedAsync(orphans, token);
                await GuardSpaceAsync(sample, orphans, token);
            },
            cancellationToken);

    public async ValueTask<IReadOnlyList<FreedWorktree>> FreeNowAsync(IOrphans orphans, CancellationToken cancellationToken) =>
        await owner.RunAsync(
            async token =>
            {
                var before = Volatile.Read(ref freed).Count;

                foreach (var kept in retained.Where(kept => !kept.Freed).ToList())
                {
                    Replace(kept, await ShelveNowAsync(kept, WorktreeMoment.Requested, orphans, clock.GetUtcNow(), token), await PolicyAsync(token));
                }

                return (IReadOnlyList<FreedWorktree>)[.. Volatile.Read(ref freed).Skip(before)];
            },
            cancellationToken);

    public ValueTask DisposeAsync() => owner.DisposeAsync();

    private async Task<Retained> RetainedAsync(JobId job, JobStatus status, string worktree, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var facts = worktrees.Facts;
        var delivered = status == JobStatus.Approved && (facts.Merged(job) || await worktrees.DeliveredAsync(worktree, cancellationToken));
        var moment = status.Ended() ? status.EndedAs(delivered, facts.AwaitsMerge(job)) : WorktreeMoment.Idle;
        var moments = ImmutableDictionary<WorktreeMoment, DateTimeOffset>.Empty.Add(moment, since);

        return new Retained(job, worktree, status, since)
        {
            Moments = facts.Archived(job) ? moments.SetItem(WorktreeMoment.Archived, since) : moments,
            Freed = await worktrees.ShelvedAsync(worktree, cancellationToken),
            Checked = clock.GetUtcNow(),
        };
    }

    private async Task<WorktreePolicy> PolicyAsync(CancellationToken cancellationToken) => (await settings.LoadAsync(cancellationToken)).Worktrees;

    private async Task SweepOwnedAsync(IOrphans orphans, CancellationToken cancellationToken)
    {
        var policy = await PolicyAsync(cancellationToken);
        var now = clock.GetUtcNow();

        foreach (var kept in retained.ToList())
        {
            var outcome = policy.DeletedAfter(kept.Status).Match(span => kept.Ended + span <= now, () => false)
                ? await DeleteAsync(kept, orphans, now, cancellationToken)
                : kept.Freed ? await StillFreedAsync(kept, now, cancellationToken)
                : kept.NotBefore > now ? kept
                : await FreeAsync(await CheckedAsync(kept, now, cancellationToken), policy, orphans, now, cancellationToken);
            Replace(kept, outcome, policy);
        }

        Volatile.Write(ref upcoming, [.. retained.Where(kept => !kept.Freed).SelectMany(kept => DueOf(kept, policy).Match<WorktreeDue[]>(due => [new(kept.Job, kept.Worktree, due.Moment, due.Due > kept.NotBefore ? due.Due : kept.NotBefore)], () => [])).OrderBy(due => due.Due)]);
    }

    private static Option<(WorktreeMoment Moment, DateTimeOffset Due)> DueOf(Retained kept, WorktreePolicy policy)
    {
        var due = kept.Moments
            .SelectMany(moment => policy.FreeAfter(moment.Key).Match<(WorktreeMoment Moment, DateTimeOffset Due)[]>(span => [(moment.Key, moment.Value + span)], () => []))
            .OrderBy(found => found.Due)
            .FirstOrDefault();

        return due == default ? Option<(WorktreeMoment, DateTimeOffset)>.None : due;
    }

    private void Replace(Retained kept, Option<Retained> outcome, WorktreePolicy policy)
    {
        var index = retained.IndexOf(kept);
        var next = outcome.Match<Retained?>(found => found.Freed && found.Status.Ended() && policy.DeletedAfter(found.Status).IsNone ? null : found, () => null);

        if (next is null)
        {
            retained.RemoveAt(index);
        }
        else
        {
            retained[index] = next;
        }
    }

    private async Task<Option<Retained>> StillFreedAsync(Retained kept, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (kept.Status.Ended() || now - kept.Checked < DeliveryCheck)
        {
            return kept;
        }

        return await worktrees.ShelvedAsync(kept.Worktree, cancellationToken)
            ? kept with { Checked = now }
            : kept with { Freed = false, Checked = now, Moments = kept.Moments.SetItem(WorktreeMoment.Idle, now) };
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
        return await DueOf(kept, policy).Match(
            due => due.Due > now ? Task.FromResult(Option<Retained>.Some(kept)) : ShelveNowAsync(kept, due.Moment, orphans, now, cancellationToken),
            () => Task.FromResult(Option<Retained>.Some(kept)));
    }

    private async Task<Option<Retained>> ShelveNowAsync(Retained kept, WorktreeMoment moment, IOrphans orphans, DateTimeOffset now, CancellationToken cancellationToken)
    {
        _ = await orphans.ReapAsync(kept.Job, cancellationToken);
        var bytes = sizes.GetValueOrDefault(kept.Worktree);
        var attempt = await worktrees.ShelveAsync(kept.Job, kept.Status, kept.Worktree, cancellationToken);

        if (attempt.IsNone)
        {
            return kept.Status.Ended() ? Option<Retained>.None : kept with { NotBefore = now + DeliveryCheck };
        }

        var shelved = attempt.Match(found => found, () => WorkspaceFailure.UnknownWorkspace);

        if (shelved.IsSuccess)
        {
            gone[kept.Worktree] = (kept.Job, now);
            var release = new FreedWorktree(kept.Job, kept.Worktree, kept.Status, moment, bytes, now);
            ImmutableInterlocked.Update(ref freed, known => known.Add(release));
            await bus.PublishAsync(new WorktreeFreed(release), cancellationToken);

            return kept with { Freed = true };
        }

        return shelved.Match(_ => false, failure => failure == WorkspaceFailure.UnknownWorkspace) ? Option<Retained>.None : kept with { NotBefore = now + DeliveryCheck };
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
            gone[kept.Worktree] = (kept.Job, now);
            var reclaim = new ReclaimedWorktree(kept.Job, kept.Worktree, kept.Status, now);
            ImmutableInterlocked.Update(ref reclaimed, known => known.Add(reclaim));
            await bus.PublishAsync(new WorktreeReclaimed(reclaim), cancellationToken);
        }

        return removed.IsSuccess || removed.Match(_ => false, failure => failure == WorkspaceFailure.UnknownWorkspace) ? Option<Retained>.None : kept;
    }

    private async Task GuardSpaceAsync(ResourceSample sample, IOrphans orphans, CancellationToken cancellationToken)
    {
        var policy = await PolicyAsync(cancellationToken);
        var space = Space(sample, policy);

        var now = clock.GetUtcNow();

        foreach (var kept in retained.Where(kept => !kept.Freed && kept.NotBefore <= now).OrderBy(kept => kept.Moments.Values.Min()).ToList())
        {
            if (space.Within)
            {
                break;
            }

            Replace(kept, await ShelveNowAsync(kept, WorktreeMoment.Space, orphans, now, cancellationToken), policy);
            space = Space(sample, policy);
        }

        if (space.Within)
        {
            Volatile.Write(ref shortage, null);
        }
        else if (Volatile.Read(ref shortage) is null)
        {
            Volatile.Write(ref shortage, space);
            await bus.PublishAsync(new WorktreeSpaceShort(space), cancellationToken);
        }
    }

    private WorktreeSpace Space(ResourceSample sample, WorktreePolicy policy)
    {
        foreach (var path in gone.Where(entry => entry.Value.At < sample.Measured).Select(entry => entry.Key).ToList())
        {
            gone.Remove(path);
        }

        var used = sample.Worktrees.Where(folder => !gone.ContainsKey(folder.Path)).Sum(folder => folder.Bytes);
        var released = sample.Worktrees.Where(folder => gone.ContainsKey(folder.Path)).Sum(folder => folder.Bytes);

        return new WorktreeSpace(used, policy.SpaceBudget, sample.Disk.Map(disk => disk with { Available = disk.Available + released }), policy.DiskFloor, sample.At);
    }

    private sealed record Retained(JobId Job, string Worktree, JobStatus Status, DateTimeOffset Ended)
    {
        public ImmutableDictionary<WorktreeMoment, DateTimeOffset> Moments { get; init; } = ImmutableDictionary<WorktreeMoment, DateTimeOffset>.Empty;

        public bool Freed { get; init; }

        public DateTimeOffset Checked { get; init; }

        public DateTimeOffset NotBefore { get; init; }
    }
}
