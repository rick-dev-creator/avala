using Avala.Permissions.Answering;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Permissions.Tests;

internal sealed class FixedReadableFolders(IReadOnlyList<string> folders) : IReadableFolders
{
    public ValueTask<IReadOnlyList<string>> FoldersAsync(CancellationToken cancellationToken) => ValueTask.FromResult(folders);
}

internal sealed class FixedProjectsFolder(string folder) : IProjectsFolder
{
    public ValueTask<ProjectsFolder> ReadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new ProjectsFolder(folder, Option<string>.None));

    public ValueTask<Result<ProjectsFolder, WorkspaceFailure>> ChooseAsync(string folder, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<ProjectsFolder, WorkspaceFailure>.Success(new ProjectsFolder(folder, folder)));
}
