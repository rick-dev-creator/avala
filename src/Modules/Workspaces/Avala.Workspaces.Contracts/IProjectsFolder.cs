using Avala.Sdk;

namespace Avala.Workspaces.Contracts;

public sealed record ProjectsFolder(string Suggested, Option<string> Chosen)
{
    public string Folder => Chosen.Match(chosen => chosen, () => Suggested);
}

public interface IProjectsFolder
{
    ValueTask<ProjectsFolder> ReadAsync(CancellationToken cancellationToken);

    ValueTask<Result<ProjectsFolder, WorkspaceFailure>> ChooseAsync(string folder, CancellationToken cancellationToken);
}
