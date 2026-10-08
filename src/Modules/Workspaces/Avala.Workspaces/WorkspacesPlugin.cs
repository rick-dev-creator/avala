using Avala.Sdk;
using Avala.Workspaces.Application;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Workspaces;

public sealed class WorkspacesPlugin : IPlugin
{
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Avala",
        "worktrees");

    public PluginInfo Info { get; } = new("avala.workspaces", "Workspaces");

    public void Register(IPluginRegistrar registrar) =>
        registrar.Services
            .AddSingleton(new WorkspaceSettings(Root))
            .AddSingleton<IGit, GitCli>()
            .AddSingleton<IWorkspaceStore, InMemoryWorkspaceStore>()
            .AddSingleton<IWorkspaces, WorkspaceService>();
}
