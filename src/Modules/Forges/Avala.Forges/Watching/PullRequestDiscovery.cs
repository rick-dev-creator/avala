using Avala.Forges.Connecting;
using Avala.Forges.Contracts;
using Avala.Forges.Delivering;
using Avala.Forges.Policy;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Workspaces.Contracts;

namespace Avala.Forges.Watching;

internal sealed class PullRequestFinder(EffectiveRules rules, IGitRemote git, ForgeConnections connections)
{
    public async Task<Option<Delivered>> FindAsync(JobId job, WorkspaceInfo worktree, CancellationToken cancellationToken)
    {
        if (!(await rules.OfWorktreeAsync(worktree.Path, cancellationToken)).TryGetValue(out var declared, out _)
            || !(await git.UrlAsync(worktree.Path, declared.Remote, cancellationToken)).TryGetValue(out var remote, out _)
            || !(await connections.ResolveAsync(declared.Forge, remote, cancellationToken)).TryGetValue(out var forge, out _))
        {
            return Option<Delivered>.None;
        }

        IReadOnlyList<string> branches = [worktree.Branch, .. (await git.CheckedOutAsync(worktree.Path, cancellationToken)).Where(branch => branch != worktree.Branch)];

        foreach (var branch in branches)
        {
            if ((await forge.Forge.FindAsync(forge.Context, branch, cancellationToken)).TryGetValue(out var found, out _) && found.IsSome)
            {
                return found.Map(pullRequest => new Delivered(job, declared, remote, pullRequest, pullRequest.Head));
            }
        }

        return Option<Delivered>.None;
    }
}

internal sealed class PullRequestDiscovery(WatchBook book, JobPlaces places, PullRequestFinder finder, WatchAdoption adoption) : IHandle<JobProgressed>
{
    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Status is not (JobStatus.AwaitingReview or JobStatus.NeedsHelp) || book.Find(integrationEvent.Job).IsSome)
        {
            return;
        }

        var found = await (await places.WorktreeAsync(integrationEvent.Job, cancellationToken)).Match(
            worktree => finder.FindAsync(integrationEvent.Job, worktree, cancellationToken),
            () => Task.FromResult(Option<Delivered>.None));

        await found.Match(delivered => adoption.AdoptFoundAsync(delivered, cancellationToken), () => Task.CompletedTask);
    }
}
