using Avala.Autopilot.Contracts;
using Avala.Autopilot.Evidence;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Autopilot.Approving;

internal interface IAutopilotRules
{
    Task<Result<AutopilotRules, AutopilotError>> OfWorktreeAsync(string worktree, CancellationToken cancellationToken);
}

internal sealed class JobWork(IWorkspaces workspaces, IWorkspaceChanges changes, IAutopilotRules rules)
{
    public async Task<Option<WorkspaceInfo>> WorkspaceAsync(Option<WorkspaceId> workspace, CancellationToken cancellationToken) =>
        await workspace.Match(
            async id => (await workspaces.FindAsync(id, cancellationToken)).Match(Option<WorkspaceInfo>.Some, _ => Option<WorkspaceInfo>.None),
            () => Task.FromResult(Option<WorkspaceInfo>.None));

    public async Task<Option<IReadOnlyList<string>>> ChangedFilesAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        (await changes.DiffAsync(workspace, cancellationToken)).Match(
            diff => Option<IReadOnlyList<string>>.Some([.. diff.Files.Select(file => file.Path)]),
            _ => Option<IReadOnlyList<string>>.None);

    public async Task<Option<AutopilotRules>> RulesAsync(string worktree, CancellationToken cancellationToken) =>
        (await rules.OfWorktreeAsync(worktree, cancellationToken)).Match(Option<AutopilotRules>.Some, _ => Option<AutopilotRules>.None);
}
