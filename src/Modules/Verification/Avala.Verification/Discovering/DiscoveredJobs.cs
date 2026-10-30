using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Checks;
using Avala.Workspaces.Contracts;

namespace Avala.Verification.Discovering;

internal sealed record JobPlace(JobId Job, string Repository, string Worktree);

internal sealed class DiscoveredJobs(JobWorktrees worktrees, IBaseFiles baseFiles, IWorkingFiles files, IScratchRepositories scratch)
{
    public Task<JobStatus> StatusAsync(JobId job, CancellationToken cancellationToken) => worktrees.StatusAsync(job, cancellationToken);

    public Task<IReadOnlyList<JobId>> ChildrenAsync(JobId job, CancellationToken cancellationToken) => worktrees.ChildrenAsync(job, cancellationToken);

    public async Task<Option<JobPlace>> PlaceOfAsync(Discovery discovery, CancellationToken cancellationToken) =>
        (await worktrees.WorktreeAsync(discovery.Job, cancellationToken)).Map(worktree => new JobPlace(discovery.Job, discovery.Repository, worktree));

    public async Task<Option<JobPlace>> UncheckedAsync(JobId job, CancellationToken cancellationToken)
    {
        var summary = (await worktrees.SummaryAsync(job, cancellationToken))
            .Bind(found => found.Parent.IsSome || found.Companion || scratch.Holds(found.Repository) ? Option<JobSummary>.None : found);

        return await summary.Match(
            found => PlaceAsync(job, found.Repository, cancellationToken),
            () => Task.FromResult(Option<JobPlace>.None));
    }

    public async Task ShipAsync(JobId job, string declaration, CancellationToken cancellationToken)
    {
        foreach (var worktree in (await worktrees.WorktreeAsync(job, cancellationToken)).Match<string[]>(path => [path], () => []))
        {
            var present = (await files.ReadAsync(worktree, CheckDeclaration.RelativePath, cancellationToken)).Match(content => content.IsSome, _ => true);

            if (!present)
            {
                _ = await files.WriteAsync(worktree, CheckDeclaration.RelativePath, declaration, cancellationToken);
            }
        }
    }

    private async Task<Option<JobPlace>> PlaceAsync(JobId job, string repository, CancellationToken cancellationToken)
    {
        var worktree = await worktrees.WorktreeAsync(job, cancellationToken);
        var declared = await worktree.Match(
            async path => (await baseFiles.ReadAsync(path, CheckDeclaration.RelativePath, cancellationToken)).Match(file => file.Content.IsSome, _ => true),
            () => Task.FromResult(true));

        return declared ? Option<JobPlace>.None : worktree.Map(path => new JobPlace(job, repository, path));
    }
}
