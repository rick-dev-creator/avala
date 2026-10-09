using Avala.Sdk;
using Avala.Workspaces.BaseFiles;
using Avala.Workspaces.Changes;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Git;
using Avala.Workspaces.Provisioning;
using Avala.Workspaces.Storage;
using Avala.Workspaces.WorkingFiles;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Workspaces;

public sealed class WorkspacesPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.workspaces", "Workspaces");

    public void Register(IPluginRegistrar registrar) =>
        registrar.Services
            .AddSingleton(provider => new WorkspaceSettings(provider.GetRequiredService<AvalaPaths>().Folder("worktrees")))
            .AddSingleton<IGit, GitCli>()
            .AddSingleton<SqliteWorkspaceStore>()
            .AddForwarded<IWorkspaceStore, SqliteWorkspaceStore>()
            .AddForwarded<IStartupTask, SqliteWorkspaceStore>()
            .AddSingleton<WorktreeReconciler>()
            .AddSingleton<IWorkspaces, WorkspaceService>()
            .AddSingleton<IBaseFiles, BaseFileReader>()
            .AddSingleton<IWorkingFiles, WorkingFileStore>()
            .AddSingleton<IGitChanges, GitChangesCli>()
            .AddSingleton<IWorkspaceChanges, WorkspaceChanges>();
}
