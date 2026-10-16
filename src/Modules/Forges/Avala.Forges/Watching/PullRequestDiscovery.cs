using Avala.Forges.Connecting;
using Avala.Forges.Contracts;
using Avala.Forges.Delivering;
using Avala.Forges.Policy;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Workspaces.Contracts;

namespace Avala.Forges.Watching;

internal sealed class PullRequestFinder(IPullRequestRules rules, IGitRemote git, ForgeConnections connections)
{
    public async Task<Option<Delivered>> FindAsync(JobId job, WorkspaceInfo worktree, CancellationToken cancellationToken)
    {
        if (!(await RulesAsync(worktree.Path, cancellationToken)).TryGetValue(out var declared, out _)
            || !(await git.UrlAsync(worktree.Path, declared.Remote, cancellationToken)).TryGetValue(out var remote, out _)
            || !(await connections.ResolveAsync(declared.Forge, remote, cancellationToken)).TryGetValue(out var forge, out _)
            || !(await forge.Forge.FindAsync(forge.Context, worktree.Branch, cancellationToken)).TryGetValue(out var found, out _))
        {
            return Option<Delivered>.None;
        }

        return found.Map(pullRequest => new Delivered(job, declared, remote, pullRequest, pullRequest.Head));
    }

    private async Task<Result<PullRequestRules, ForgeError>> RulesAsync(string worktree, CancellationToken cancellationToken) =>
        await (await rules.OfWorktreeAsync(worktree, cancellationToken)).Match(
            declared => declared.Match(
                found => Task.FromResult(Result<PullRequestRules, ForgeError>.Success(found)),
                () => HostedAsync(worktree, cancellationToken)),
            error => Task.FromResult(Result<PullRequestRules, ForgeError>.Failure(error)));

    private async Task<Result<PullRequestRules, ForgeError>> HostedAsync(string worktree, CancellationToken cancellationToken)
    {
        if (!(await git.UrlAsync(worktree, PullRequestRules.DefaultRemote, cancellationToken)).TryGetValue(out var remote, out var error))
        {
            return error;
        }

        return (await connections.HostingAsync(remote, cancellationToken))
            .Map(forge => new PullRequestRules(forge.Name, PullRequestRules.DefaultRemote, OnPullRequest.WatchOnly, PullRequestRules.DefaultWakeUps))
            .ToResult(ForgeError.MissingForge);
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

        await found.Match(delivered => adoption.AdoptAsync(delivered, cancellationToken), () => Task.CompletedTask);
    }
}
