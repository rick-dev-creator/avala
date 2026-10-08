using Avala.Sdk;
using Avala.Workspaces.Application;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Workspaces;

public sealed class WorkspacesPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.workspaces", "Workspaces");

    public void Register(IPluginRegistrar registrar) =>
        registrar.Services
            .AddSingleton(provider => new WorkspaceSettings(provider.GetRequiredService<AvalaPaths>().Folder("worktrees")))
            .AddSingleton<IGit, GitCli>()
            .AddSingleton<IWorkspaceStore, SqliteWorkspaceStore>()
            .AddSingleton<IWorkspaces, WorkspaceService>();
}
