using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Documentation.Writing;

internal sealed record JobPlace(JobId Job, string Repository, WorkspaceId Workspace, string Worktree, string Instruction, JobStatus Status, int Attempt)
{
    public Option<JobId> Parent { get; init; }

    public string Branch { get; init; } = string.Empty;
}

internal sealed class JobPlaces(IJobCatalog catalog, IWorkspaces workspaces)
{
    public async Task<IReadOnlyList<AttemptRecord>> AttemptsAsync(JobId job, CancellationToken cancellationToken) =>
        (await catalog.HistoryAsync(job, cancellationToken)).Match(history => history.Attempts, () => []);

    public async Task<Option<JobStatus>> StatusAsync(JobId job, CancellationToken cancellationToken) =>
        (await catalog.HistoryAsync(job, cancellationToken)).Map(history => history.Summary.Status);

    public async Task CommitAsync(JobId job, string label, CancellationToken cancellationToken)
    {
        foreach (var place in (await OfAsync(job, cancellationToken)).Match<JobPlace[]>(found => [found], () => []).Where(place => place.Status is not (JobStatus.Running or JobStatus.Checking)))
        {
            _ = await workspaces.CheckpointAsync(place.Workspace, label, cancellationToken);
        }
    }

    public async Task<Option<JobPlace>> OfAsync(JobId job, CancellationToken cancellationToken) =>
        await (await catalog.HistoryAsync(job, cancellationToken)).Match(
            history => history.Summary.Workspace.Match(
                async workspace => (await workspaces.FindAsync(workspace, cancellationToken)).Match(
                    found => Option<JobPlace>.Some(new JobPlace(job, history.Summary.Repository, workspace, found.Path, history.Summary.Instruction, history.Summary.Status, Math.Max(1, history.Attempts.Count))
                    {
                        Parent = history.Summary.Parent,
                        Branch = found.Branch,
                    }),
                    _ => Option<JobPlace>.None),
                () => Task.FromResult(Option<JobPlace>.None)),
            () => Task.FromResult(Option<JobPlace>.None));

    public async Task<IReadOnlyList<JobPlace>> OfRepositoryAsync(string repository, CancellationToken cancellationToken)
    {
        var places = new List<JobPlace>();

        foreach (var job in (await catalog.ListAsync(cancellationToken)).Where(found => found.Repository == repository && found.Parent.IsNone && !found.Companion).OrderByDescending(found => found.Submitted))
        {
            places.AddRange((await OfAsync(job.Job, cancellationToken)).Match<JobPlace[]>(found => [found], () => []));
        }

        return places;
    }
}
