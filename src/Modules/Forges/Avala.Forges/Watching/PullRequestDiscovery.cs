using Avala.Agents.Contracts;
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
    public const int MostReadLinks = 5;

    public async Task<Option<Delivered>> FindAsync(JobId job, WorkspaceInfo worktree, Reported reported, CancellationToken cancellationToken)
    {
        if (!(await rules.OfWorktreeAsync(worktree.Path, cancellationToken)).TryGetValue(out var declared, out _)
            || !(await git.UrlAsync(worktree.Path, declared.Remote, cancellationToken)).TryGetValue(out var remote, out _)
            || !(await connections.ResolveAsync(declared.Forge, remote, cancellationToken)).TryGetValue(out var forge, out _))
        {
            return Option<Delivered>.None;
        }

        var found = await CheckedOutAsync(forge, worktree, cancellationToken);

        if (found.IsNone)
        {
            found = await ReportedAsync(forge, worktree, declared.Remote, reported, cancellationToken);
        }

        return found.Map(pullRequest => new Delivered(job, declared, remote, pullRequest, pullRequest.Head));
    }

    private async Task<Option<PullRequestRef>> CheckedOutAsync(ResolvedForge forge, WorkspaceInfo worktree, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> branches = [worktree.Branch, .. (await git.CheckedOutAsync(worktree.Path, cancellationToken)).Where(branch => branch != worktree.Branch)];

        foreach (var branch in branches)
        {
            if ((await forge.Forge.FindAsync(forge.Context, branch, cancellationToken)).TryGetValue(out var found, out _) && found.IsSome)
            {
                return found;
            }
        }

        return Option<PullRequestRef>.None;
    }

    private async Task<Option<PullRequestRef>> ReportedAsync(ResolvedForge forge, WorkspaceInfo worktree, string remote, Reported reported, CancellationToken cancellationToken)
    {
        var target = forge.Context.Target.Repository;
        var segments = new HashSet<string>([forge.Forge.Info.PullRequestSegment], StringComparer.Ordinal);
        var candidates = reported.Links.Reverse()
            .SelectMany(link => PullRequestLinks.NumberOf(link, target, segments).Match<(Uri Link, int Number)[]>(number => [(link, number)], () => []))
            .DistinctBy(candidate => candidate.Number)
            .Take(MostReadLinks);

        foreach (var (link, number) in candidates)
        {
            if ((await forge.Forge.ReadAsync(forge.Context, new PullRequestRef(number, link, string.Empty, string.Empty), cancellationToken)).TryGetValue(out var state, out _)
                && state.Lifecycle == PullRequestLifecycle.Open
                && string.Equals(state.PullRequest.Link.Host, link.Host, StringComparison.OrdinalIgnoreCase)
                && state.HeadRepository.Match(head => RemoteAddresses.Same(head, target), () => false)
                && await PushedHereAsync(worktree, remote, target, reported, state, cancellationToken))
            {
                return state.PullRequest;
            }
        }

        return Option<PullRequestRef>.None;
    }

    private async Task<bool> PushedHereAsync(WorkspaceInfo worktree, string remote, RepositoryAddress target, Reported reported, PullRequestState state, CancellationToken cancellationToken) =>
        await git.PushedAsync(worktree.Path, remote, state.PullRequest.Head, state.HeadCommit, cancellationToken)
        || (reported.Pushed.Any(pushed => pushed.Branch == state.PullRequest.Head && RemoteAddresses.Same(pushed.Repository, target))
            && await git.HeldAsync(worktree.Path, state.HeadCommit, cancellationToken));
}

internal sealed class PullRequestDiscovery(JobPlaces places, PullRequestFinder finder, WatchAdoption adoption, ReportedBook reported)
    : IHandle<JobProgressed>, IHandle<JobSessionStarted>, IHandle<AgentActivity>
{
    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Status is JobStatus.Approved or JobStatus.Discarded or JobStatus.Failed)
        {
            await reported.ForgetAsync(integrationEvent.Job, cancellationToken);

            return;
        }

        if (integrationEvent.Status is not (JobStatus.AwaitingReview or JobStatus.NeedsHelp) || adoption.Follows(integrationEvent.Job))
        {
            return;
        }

        var said = await reported.OfAsync(integrationEvent.Job, cancellationToken);
        var found = await (await places.WorktreeAsync(integrationEvent.Job, cancellationToken)).Match(
            worktree => finder.FindAsync(integrationEvent.Job, worktree, said, cancellationToken),
            () => Task.FromResult(Option<Delivered>.None));

        await found.Match(
            delivered => adoption.AdoptFoundAsync(delivered, cancellationToken),
            () => Task.CompletedTask);
    }

    public async ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken) =>
        await reported.StartedAsync(integrationEvent.Job, integrationEvent.Session, cancellationToken);

    public async ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken) =>
        await reported.SawAsync(integrationEvent.Event, cancellationToken);
}
