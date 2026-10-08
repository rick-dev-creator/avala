using Avala.Sdk;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Domain;

namespace Avala.Workspaces.Application;

internal sealed class WorkspaceService(IGit git, IWorkspaceStore store, WorkspaceSettings settings) : IWorkspaces
{
    public async ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> PrepareAsync(
        WorkspaceRequest request,
        CancellationToken cancellationToken) =>
        await git.FindRepositoryRootAsync(request.RepositoryPath, cancellationToken)
            .BindAsync(Plan)
            .BindAsync(workspace => EnsureBranchIsFreeAsync(workspace, cancellationToken))
            .BindAsync(workspace => OpenAsync(workspace, request.BaseRef, cancellationToken))
            .MapAsync(workspace => new WorkspaceInfo(workspace.Id, workspace.Location.Path, workspace.Branch.Value));

    public async ValueTask<Result<CheckpointInfo, WorkspaceFailure>> CheckpointAsync(
        WorkspaceId workspace,
        string label,
        CancellationToken cancellationToken) =>
        await FindAsync(workspace, cancellationToken)
            .BindAsync(found => git.CommitAllAsync(found.Location, label, cancellationToken)
                .BindAsync(commit => Valid(found.RecordCheckpoint(commit, label)))
                .BindAsync(recorded => SaveAsync(found, recorded, cancellationToken)))
            .MapAsync(recorded => new CheckpointInfo(
                workspace,
                recorded.Checkpoint.Number,
                recorded.Checkpoint.Commit.Value,
                recorded.Checkpoint.Label));

    public async ValueTask<Result<WorkspaceId, WorkspaceFailure>> RemoveAsync(
        WorkspaceId workspace,
        CancellationToken cancellationToken) =>
        await FindAsync(workspace, cancellationToken)
            .BindAsync(found => git.RemoveWorktreeAsync(found.Location, found.Branch, cancellationToken)
                .BindAsync(_ => Valid(found.Remove())))
            .BindAsync(async removed =>
            {
                await store.RemoveAsync(removed.Workspace, cancellationToken);

                return Result<WorkspaceId, WorkspaceFailure>.Success(removed.Workspace);
            });

    private Result<Workspace, WorkspaceFailure> Plan(string repository)
    {
        var id = WorkspaceId.New();

        return Valid(WorkspaceLocation.Create(repository, Path.Combine(settings.Root, $"{id.Value:N}"))
            .Bind(location => BranchName.Create($"avala/{id.Value:N}")
                .Bind(branch => Workspace.Create(id, location, branch))));
    }

    private async Task<Result<Workspace, WorkspaceFailure>> EnsureBranchIsFreeAsync(
        Workspace workspace,
        CancellationToken cancellationToken) =>
        (await git.BranchExistsAsync(workspace.Location.Repository, workspace.Branch, cancellationToken))
            .Bind<Workspace>(exists => exists ? WorkspaceFailure.BranchAlreadyExists : workspace);

    private async Task<Result<Workspace, WorkspaceFailure>> OpenAsync(
        Workspace workspace,
        string baseRef,
        CancellationToken cancellationToken)
    {
        var opened = await git.AddWorktreeAsync(workspace.Location, workspace.Branch, baseRef, cancellationToken)
            .BindAsync(_ => Valid(workspace.MarkReady()));

        if (opened.IsFailure)
        {
            _ = workspace.Fail();

            return opened.Map(_ => workspace);
        }

        return await SaveAsync(workspace, workspace, cancellationToken);
    }

    private async Task<Result<Workspace, WorkspaceFailure>> FindAsync(WorkspaceId id, CancellationToken cancellationToken) =>
        await store.FindAsync(id, cancellationToken) is { } workspace
            ? workspace
            : WorkspaceFailure.UnknownWorkspace;

    private async Task<Result<T, WorkspaceFailure>> SaveAsync<T>(Workspace workspace, T value, CancellationToken cancellationToken)
    {
        await store.SaveAsync(workspace, cancellationToken);

        return Result<T, WorkspaceFailure>.Success(value!);
    }

    private static Result<T, WorkspaceFailure> Valid<T>(Result<T, WorkspaceError> result) =>
        result.MapError(_ => WorkspaceFailure.InvalidState);
}
