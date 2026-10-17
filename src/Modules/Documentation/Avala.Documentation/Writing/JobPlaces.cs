using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Documentation.Writing;

internal sealed record JobPlace(JobId Job, string Repository, WorkspaceId Workspace, string Worktree, string Instruction, JobStatus Status, int Attempt)
{
    public Option<JobId> Parent { get; init; }
}

internal sealed class JobPlaces(IJobCatalog catalog, IWorkspaces workspaces)
{
    public async Task<IReadOnlyList<AttemptRecord>> AttemptsAsync(JobId job, CancellationToken cancellationToken) =>
        (await catalog.HistoryAsync(job, cancellationToken)).Match(history => history.Attempts, () => []);

    public async Task<Option<JobStatus>> StatusAsync(JobId job, CancellationToken cancellationToken) =>
        (await catalog.HistoryAsync(job, cancellationToken)).Map(history => history.Summary.Status);

    public async Task<Option<JobPlace>> OfAsync(JobId job, CancellationToken cancellationToken) =>
        await (await catalog.HistoryAsync(job, cancellationToken)).Match(
            history => history.Summary.Workspace.Match(
                async workspace => (await workspaces.FindAsync(workspace, cancellationToken)).Match(
                    found => Option<JobPlace>.Some(new JobPlace(job, history.Summary.Repository, workspace, found.Path, history.Summary.Instruction, history.Summary.Status, Math.Max(1, history.Attempts.Count))
                    {
                        Parent = history.Summary.Parent,
                    }),
                    _ => Option<JobPlace>.None),
                () => Task.FromResult(Option<JobPlace>.None)),
            () => Task.FromResult(Option<JobPlace>.None));
}
