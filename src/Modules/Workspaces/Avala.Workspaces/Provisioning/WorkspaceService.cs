using Avala.Sdk;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Workspaces;

namespace Avala.Workspaces.Provisioning;

internal sealed class WorkspaceService(IGit git, IWorkspaceStore store, WorkspaceSettings settings, WorktreeReconciler reconciler) : IWorkspaces
{
    public async ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> FindAtAsync(string folder, CancellationToken cancellationToken) =>
        (await store.FindAtAsync(folder, cancellationToken)).ToResult(WorkspaceFailure.UnknownWorkspace).Map(Describe);

    public async ValueTask<WorktreeReconciliation> ReconcileAsync(CancellationToken cancellationToken) =>
        await reconciler.ReconcileAsync(cancellationToken);

    public async ValueTask<WorktreeReconciliation> CleanAsync(WorktreeReconciliation found, CancellationToken cancellationToken) =>
        await reconciler.CleanAsync(found, cancellationToken);

    public async ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> PrepareAsync(
        WorkspaceRequest request,
        CancellationToken cancellationToken) =>
        await git.FindRepositoryRootAsync(request.RepositoryPath, cancellationToken)
            .BindAsync(repository => git.ResolveCommitAsync(repository, request.BaseRef, cancellationToken)
                .BindAsync(commit => RulesAsync(repository, request.Rules, cancellationToken)
                    .BindAsync(async rules => Plan(repository, commit, await git.BranchOfAsync(repository, request.BaseRef, cancellationToken), rules))))
            .BindAsync(workspace => EnsureBranchIsFreeAsync(workspace, cancellationToken))
            .BindAsync(workspace => OpenAsync(workspace, cancellationToken))
            .MapAsync(Describe);

    public async ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> FindAsync(
        WorkspaceId workspace,
        CancellationToken cancellationToken) =>
        await LoadAsync(workspace, cancellationToken).MapAsync(Describe);

    public async ValueTask<Result<CheckpointInfo, WorkspaceFailure>> CheckpointAsync(
        WorkspaceId workspace,
        string label,
        CancellationToken cancellationToken) =>
        await LoadAsync(workspace, cancellationToken)
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
        await LoadAsync(workspace, cancellationToken)
            .BindAsync(found => git.RemoveWorktreeAsync(found.Location, found.Branch, cancellationToken)
                .BindAsync(_ => Valid(found.Remove())))
            .BindAsync(async removed =>
            {
                await store.RemoveAsync(removed.Workspace, cancellationToken);

                return Result<WorkspaceId, WorkspaceFailure>.Success(removed.Workspace);
            });

    private Result<Workspace, WorkspaceFailure> Plan(string repository, CommitSha commit, Option<BranchName> baseBranch, Option<CommitSha> rules)
    {
        var id = WorkspaceId.New();

        return Valid(WorkspaceLocation.Create(repository, Path.Combine(settings.Root, $"{id.Value:N}"))
            .Bind(location => BranchName.Create($"avala/{id.Value:N}")
                .Bind(branch => Workspace.Create(id, location, branch, commit, baseBranch, rules))));
    }

    private async Task<Result<Option<CommitSha>, WorkspaceFailure>> RulesAsync(string repository, Option<string> rules, CancellationToken cancellationToken) =>
        await rules.Match(
            reference => git.ResolveCommitAsync(repository, reference, cancellationToken).MapAsync(Option<CommitSha>.Some),
            () => Task.FromResult(Result<Option<CommitSha>, WorkspaceFailure>.Success(Option<CommitSha>.None)));

    private async Task<Result<Workspace, WorkspaceFailure>> EnsureBranchIsFreeAsync(
        Workspace workspace,
        CancellationToken cancellationToken) =>
        (await git.BranchExistsAsync(workspace.Location.Repository, workspace.Branch, cancellationToken))
            .Bind<Workspace>(exists => exists ? WorkspaceFailure.BranchAlreadyExists : workspace);

    private async Task<Result<Workspace, WorkspaceFailure>> OpenAsync(
        Workspace workspace,
        CancellationToken cancellationToken)
    {
        var opened = await git.AddWorktreeAsync(workspace.Location, workspace.Branch, workspace.Base, cancellationToken)
            .BindAsync(_ => Valid(workspace.MarkReady()));

        if (opened.IsFailure)
        {
            _ = workspace.Fail();

            return opened.Map(_ => workspace);
        }

        return await SaveAsync(workspace, workspace, cancellationToken);
    }

    private async Task<Result<Workspace, WorkspaceFailure>> LoadAsync(WorkspaceId id, CancellationToken cancellationToken) =>
        (await store.FindAsync(id, cancellationToken)).ToResult(WorkspaceFailure.UnknownWorkspace);

    private async Task<Result<T, WorkspaceFailure>> SaveAsync<T>(Workspace workspace, T value, CancellationToken cancellationToken)
    {
        await store.SaveAsync(workspace, cancellationToken);

        return Result<T, WorkspaceFailure>.Success(value!);
    }

    private static WorkspaceInfo Describe(Workspace workspace) => workspace.Describe();

    private static Result<T, WorkspaceFailure> Valid<T>(Result<T, WorkspaceError> result) =>
        result.MapError(_ => WorkspaceFailure.InvalidState);
}
