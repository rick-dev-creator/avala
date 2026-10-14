using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;
using Avala.Projects.Placing;
using Avala.Projects.Storage;
using Avala.Sdk;
using Avala.Sdk.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Projects;

public sealed class ProjectsPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.projects", "Projects");

    public void Register(IPluginRegistrar registrar) =>
        registrar.Services
            .AddSingleton<SqliteProjectStore>()
            .AddForwarded<IProjectStore, SqliteProjectStore>()
            .AddForwarded<IStartupTask, SqliteProjectStore>()
            .AddSingleton<ProjectBook>()
            .AddSingleton<RepositoryProjects>()
            .AddForwarded<IStartupTask, RepositoryProjects>()
            .AddForwarded<IHandle<JobSubmitted>, RepositoryProjects>()
            .AddSingleton<ArchiveReturns>()
            .AddForwarded<IHandle<JobProgressed>, ArchiveReturns>()
            .AddSingleton<ProjectsEntry>()
            .AddForwarded<IProjects, ProjectsEntry>();
}
