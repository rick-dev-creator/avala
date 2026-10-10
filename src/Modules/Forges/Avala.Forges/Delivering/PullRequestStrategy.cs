using Avala.Forges.Connecting;
using Avala.Forges.Contracts;
using Avala.Forges.Policy;
using Avala.Forges.Watching;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Forges.Delivering;

internal interface IPullRequestRules
{
    ValueTask<Result<Option<PullRequestRules>, ForgeError>> OfWorktreeAsync(string worktree, CancellationToken cancellationToken);
}

internal interface IGitRemote
{
    ValueTask<Result<string, ForgeError>> UrlAsync(string worktree, string remote, CancellationToken cancellationToken);

    ValueTask<Result<string, ForgeError>> PushAsync(string worktree, string remote, string branch, CancellationToken cancellationToken);

    ValueTask<Result<string, ForgeError>> FetchAsync(string worktree, string remote, string branch, CancellationToken cancellationToken);
}

internal sealed class PullRequestStrategy(IPullRequestRules rules, ForgeConnections connections, IGitRemote git, WatchAdoption watches) : IApprovalStrategy
{
    public const int LongestTitle = 72;

    public string Name => PullRequestDelivery.Strategy;

    public static PullRequestDraft Draft(ApprovalRequest request, string head, string target)
    {
        var first = request.Instruction.ReplaceLineEndings("\n").Split('\n', 2)[0].Trim();
        var title = first.Length > LongestTitle ? first[..LongestTitle] : first;

        return new PullRequestDraft(head, target, title.Length > 0 ? title : $"Job {request.Job.Value}", $"{request.Instruction}\n\nOpened by Avala for job {request.Job.Value}.");
    }

    public async ValueTask<Result<ApprovalDelivery, JobRejection>> DeliverAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        if (!(await rules.OfWorktreeAsync(request.Workspace.Path, cancellationToken)).Bind(found => found.ToResult(ForgeError.MissingForge)).TryGetValue(out var declared, out var invalid))
        {
            await watches.RefusedAsync(request.Job, invalid, cancellationToken);

            return JobRejection.InvalidJobFile;
        }

        if (!request.Workspace.BaseBranch.ToResult(ForgeError.NoBaseBranch).TryGetValue(out var target, out _))
        {
            await watches.RefusedAsync(request.Job, ForgeError.NoBaseBranch, cancellationToken);

            return JobRejection.NoBaseBranch;
        }

        var opened = await OpenAsync(request, declared, target, cancellationToken);

        if (!opened.TryGetValue(out var delivered, out var error))
        {
            await watches.RefusedAsync(request.Job, error, cancellationToken);

            return JobRejection.DeliveryFailed;
        }

        await watches.AdoptAsync(delivered, cancellationToken);

        return new ApprovalDelivery(Name, request.Workspace.Branch, delivered.Head);
    }

    private async Task<Result<Delivered, ForgeError>> OpenAsync(ApprovalRequest request, PullRequestRules declared, string target, CancellationToken cancellationToken)
    {
        var worktree = request.Workspace.Path;
        var branch = request.Workspace.Branch;

        if (!(await git.UrlAsync(worktree, declared.Remote, cancellationToken)).TryGetValue(out var remote, out var error)
            || !(await connections.ResolveAsync(declared.Forge, remote, cancellationToken)).TryGetValue(out var forge, out error)
            || !(await git.PushAsync(worktree, declared.Remote, branch, cancellationToken)).TryGetValue(out var head, out error)
            || !(await forge.Forge.FindAsync(forge.Context, branch, cancellationToken)).TryGetValue(out var existing, out error))
        {
            return error;
        }

        return (await existing.Match(
                found => Task.FromResult(Result<PullRequestRef, ForgeError>.Success(found)),
                async () => await forge.Forge.OpenAsync(forge.Context, Draft(request, branch, target), cancellationToken)))
            .Map(pullRequest => new Delivered(request.Job, declared, remote, pullRequest, head));
    }
}
