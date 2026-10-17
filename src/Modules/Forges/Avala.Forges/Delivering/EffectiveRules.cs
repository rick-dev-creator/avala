using Avala.Forges.Connecting;
using Avala.Forges.Contracts;
using Avala.Forges.Policy;
using Avala.Sdk;

namespace Avala.Forges.Delivering;

internal sealed class EffectiveRules(IPullRequestRules rules, IGitRemote git, ForgeConnections connections)
{
    public async Task<Result<PullRequestRules, ForgeError>> OfWorktreeAsync(string worktree, CancellationToken cancellationToken) =>
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
