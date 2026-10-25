using Avala.Sdk;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Workspaces.Testing;

public sealed class InMemoryWorkspacesPlugin(InMemoryGit git) : IPlugin
{
    public PluginInfo Info { get; } = new("avala.workspaces", "Workspaces");

    public InMemoryGit Git => git;

    public void Register(IPluginRegistrar registrar) =>
        registrar.Services
            .AddSingleton(provider => new InMemoryWorkspaces(git, provider.GetRequiredService<AvalaPaths>().Folder("worktrees")))
            .AddForwarded<IWorkspaces, InMemoryWorkspaces>()
            .AddForwarded<IWorkspaceCheckpoints, InMemoryWorkspaces>()
            .AddForwarded<IWorkspaceShelf, InMemoryWorkspaces>()
            .AddForwarded<IBaseFiles, InMemoryWorkspaces>()
            .AddSingleton<IWorkspaceChanges>(provider => new InMemoryChanges(git, provider.GetRequiredService<InMemoryWorkspaces>()))
            .AddSingleton(provider => new InMemoryRepositories(git, provider.GetRequiredService<AvalaPaths>().Folder("scratch"), provider.GetRequiredService<TimeProvider>()))
            .AddForwarded<IRepositories, InMemoryRepositories>()
            .AddForwarded<IScratchRepositories, InMemoryRepositories>()
            .AddForwarded<IRemoteRepositories, InMemoryRepositories>()
            .AddForwarded<IRepositoryOrigins, InMemoryRepositories>()
            .AddForwarded<IRepositoryIndex, InMemoryRepositories>()
            .AddForwarded<IWorkingFiles, InMemoryRepositories>()
            .AddForwarded<IWorkingCommits, InMemoryRepositories>()
            .AddSingleton<IProjectsFolder>(new InMemoryProjects(git));
}

internal sealed class InMemoryProjects(InMemoryGit git) : IProjectsFolder
{
    private static readonly string[] Usual = ["Projects", "projects", "code", "src", "dev", Path.Combine("source", "repos")];

    public ValueTask<ProjectsFolder> ReadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new ProjectsFolder(Suggested(), git.ProjectsFolder));

    public ValueTask<Result<ProjectsFolder, WorkspaceFailure>> ChooseAsync(string folder, CancellationToken cancellationToken)
    {
        var trimmed = folder.Trim();

        if (!Path.IsPathFullyQualified(trimmed))
        {
            return ValueTask.FromResult<Result<ProjectsFolder, WorkspaceFailure>>(WorkspaceFailure.RelativeFolder);
        }

        if (!Directory.Exists(trimmed))
        {
            return ValueTask.FromResult<Result<ProjectsFolder, WorkspaceFailure>>(WorkspaceFailure.FolderMissing);
        }

        git.ProjectsFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed));

        return ValueTask.FromResult<Result<ProjectsFolder, WorkspaceFailure>>(new ProjectsFolder(Suggested(), git.ProjectsFolder));
    }

    private static string Suggested()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return Usual.Select(name => Path.Combine(home, name)).FirstOrDefault(Directory.Exists) ?? home;
    }
}
