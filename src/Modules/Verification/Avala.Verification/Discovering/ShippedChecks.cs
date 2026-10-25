using Avala.Sdk;
using Avala.Verification.Checks;
using Avala.Workspaces.Contracts;

namespace Avala.Verification.Discovering;

internal sealed class ShippedChecks(IWorkingFiles files, IWorkspaces workspaces, IWorkspaceChanges changes)
{
    public const string Label = "Checks found by Avala";

    public async Task<bool> ShippedAsync(string worktree, string declaration, bool commits, CancellationToken cancellationToken)
    {
        var shipped = (await files.ReadAsync(worktree, CheckDeclaration.RelativePath, cancellationToken))
            .Match(content => content == Option<string>.Some(declaration), _ => false);

        if (shipped && commits)
        {
            await (await workspaces.FindAtAsync(worktree, cancellationToken)).Match(
                workspace => CommitAsync(workspace.Id, cancellationToken),
                _ => Task.CompletedTask);
        }

        return shipped;
    }

    private async Task CommitAsync(WorkspaceId workspace, CancellationToken cancellationToken)
    {
        var committed = (await changes.DiffAsync(workspace, cancellationToken))
            .Match(diff => diff.Files.Any(file => file.Path == CheckDeclaration.RelativePath), _ => true);

        if (!committed)
        {
            _ = await workspaces.CheckpointAsync(workspace, Label, cancellationToken);
        }
    }
}
