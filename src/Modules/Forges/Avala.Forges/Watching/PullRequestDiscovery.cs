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
    public async Task<Option<Delivered>> FindAsync(JobId job, WorkspaceInfo worktree, IReadOnlyList<Uri> reported, CancellationToken cancellationToken)
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
            found = await ReportedAsync(forge, worktree, reported, cancellationToken);
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

    private async Task<Option<PullRequestRef>> ReportedAsync(ResolvedForge forge, WorkspaceInfo worktree, IReadOnlyList<Uri> reported, CancellationToken cancellationToken)
    {
        var segments = new HashSet<string>([forge.Forge.Info.PullRequestSegment], StringComparer.Ordinal);

        foreach (var (link, number) in reported.Reverse().SelectMany(link => PullRequestLinks.NumberOf(link, forge.Context.Target.Repository, segments).Match<(Uri, int)[]>(number => [(link, number)], () => [])))
        {
            if ((await forge.Forge.ReadAsync(forge.Context, new PullRequestRef(number, link, string.Empty, string.Empty), cancellationToken)).TryGetValue(out var state, out _)
                && state.Lifecycle == PullRequestLifecycle.Open
                && await git.HasBranchAsync(worktree.Path, state.PullRequest.Head, cancellationToken))
            {
                return state.PullRequest;
            }
        }

        return Option<PullRequestRef>.None;
    }
}

internal sealed class PullRequestDiscovery(WatchBook book, JobPlaces places, PullRequestFinder finder, WatchAdoption adoption)
    : IHandle<JobProgressed>, IHandle<JobSessionStarted>, IHandle<AgentActivity>
{
    private readonly ReportedLinks reported = new();

    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Status is JobStatus.Approved or JobStatus.Discarded or JobStatus.Failed)
        {
            reported.Forget(integrationEvent.Job);

            return;
        }

        if (integrationEvent.Status is not (JobStatus.AwaitingReview or JobStatus.NeedsHelp) || book.Find(integrationEvent.Job).IsSome)
        {
            return;
        }

        var found = await (await places.WorktreeAsync(integrationEvent.Job, cancellationToken)).Match(
            worktree => finder.FindAsync(integrationEvent.Job, worktree, reported.Of(integrationEvent.Job), cancellationToken),
            () => Task.FromResult(Option<Delivered>.None));

        await found.Match(delivered => adoption.AdoptFoundAsync(delivered, cancellationToken), () => Task.CompletedTask);
    }

    public ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        reported.Started(integrationEvent.Job, integrationEvent.Session);

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken)
    {
        reported.Saw(integrationEvent.Event);

        return ValueTask.CompletedTask;
    }
}
