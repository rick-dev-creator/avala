using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Preview.Running;

internal sealed class JobWorktrees(IJobCatalog catalog, IWorkspaces workspaces) : IJobWorktrees
{
    public async ValueTask<Option<string>> FindAsync(JobId job, CancellationToken cancellationToken) =>
        await (await catalog.HistoryAsync(job, cancellationToken))
            .Bind(history => history.Summary.Workspace)
            .Match(
                async workspace => (await workspaces.FindAsync(workspace, cancellationToken)).Match(
                    found => Directory.Exists(found.Path) ? Option<string>.Some(found.Path) : Option<string>.None,
                    _ => Option<string>.None),
                () => Task.FromResult(Option<string>.None));
}
