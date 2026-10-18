using Avala.Jobs.Contracts;
using Avala.Notes.Contracts;
using Avala.Notes.Notebook;
using Avala.Workspaces.Contracts;

namespace Avala.Notes.Taking;

internal sealed class NoteKeeping(INoteShelf shelf, IJobCatalog catalog, IWorkingCommits commits) : IWorkspaceRemovals
{
    public async ValueTask RemovingAsync(WorkspaceInfo workspace, CancellationToken cancellationToken)
    {
        foreach (var job in shelf.JobsAt(workspace.Path))
        {
            await (await catalog.HistoryAsync(job, cancellationToken)).Match(
                history => history.Summary.Status is JobStatus.Discarded or JobStatus.Failed
                    ? KeepAsync(history.Summary, workspace.Path, cancellationToken)
                    : Task.CompletedTask,
                () => Task.CompletedTask);
        }
    }

    private async Task KeepAsync(JobSummary ended, string worktree, CancellationToken cancellationToken)
    {
        var folder = NoteFolder.Of(NotePlace.OfJob(ended.Job, ended.Repository));

        if (await shelf.KeepAsync(worktree, ended.Repository, folder, cancellationToken))
        {
            _ = await commits.CommitAsync(ended.Repository, folder, $"Keep the notes of {ended.Status.ToString().ToLowerInvariant()} job {ended.Job.Value:N}", cancellationToken);
        }
    }
}
