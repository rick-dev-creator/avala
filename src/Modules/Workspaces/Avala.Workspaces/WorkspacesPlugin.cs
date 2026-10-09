using Avala.Sdk;
using Avala.Workspaces.BaseFiles;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Git;
using Avala.Workspaces.Provisioning;
using Avala.Workspaces.Storage;
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
            .AddSingleton<WorktreeReconciler>()
            .AddSingleton<IWorkspaces, WorkspaceService>()
            .AddSingleton<IBaseFiles, BaseFileReader>();
}
