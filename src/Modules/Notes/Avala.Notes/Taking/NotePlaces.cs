using Avala.Jobs.Contracts;
using Avala.Notes.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Notes.Taking;

internal sealed class NotePlaces(IJobCatalog catalog, IWorkspaces workspaces)
{
    public async ValueTask<string> RootOfAsync(NotePlace place, CancellationToken cancellationToken) =>
        await place.Job.Match(
            job => WorktreeOfAsync(job, place.Repository, cancellationToken),
            () => Task.FromResult(place.Repository));

    private async Task<string> WorktreeOfAsync(JobId job, string repository, CancellationToken cancellationToken) =>
        await (await catalog.HistoryAsync(job, cancellationToken)).Match(
            history => history.Summary.Workspace.Match(
                async workspace => (await workspaces.FindAsync(workspace, cancellationToken)).Match(found => found.Path, _ => repository),
                () => Task.FromResult(repository)),
            () => Task.FromResult(repository));
}
