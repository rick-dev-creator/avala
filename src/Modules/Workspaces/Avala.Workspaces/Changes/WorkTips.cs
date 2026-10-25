using Avala.Sdk;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Workspaces;

namespace Avala.Workspaces.Changes;

internal static class WorkTips
{
    extension(IGitChanges git)
    {
        public async Task<Result<CommitSha, WorkspaceFailure>> WorkOfAsync(Workspace workspace, CancellationToken cancellationToken) =>
            await (workspace.IsShelved
                    ? git.ShelvedTipAsync(workspace.Location.Repository, workspace.Id, cancellationToken)
                    : git.TipOfAsync(workspace.Location.Repository, workspace.Branch, cancellationToken))
                .BindAsync(tip => tip.ToResult(WorkspaceFailure.GitFailed));
    }
}
