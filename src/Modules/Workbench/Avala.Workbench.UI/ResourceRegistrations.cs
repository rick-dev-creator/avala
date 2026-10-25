using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.UI;
using Avala.Workbench.Resources;
using Avala.Workbench.Upkeep;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Workbench.UI;

internal static class ResourceRegistrations
{
    public static IServiceCollection AddResourcePages(this IServiceCollection services) =>
        services
            .AddSingleton<Leftovers>()
            .AddForwarded<IHandle<WorktreesReconciled>, Leftovers>()
            .AddSingleton<ResourceReader>()
            .AddSingleton<Housekeeping>()
            .AddSingleton<WorktreeReader>()
            .AddSingleton<WorktreesViewModel>()
            .AddSingleton<WorktreesEntry>()
            .AddSingleton<ResourceIndicatorViewModel>()
            .AddSingleton<ResourcesViewModel>();

    public static void RegisterViews(IViewRegistrar views)
    {
        views.Register<IResourcesViewModel, ResourcesView>();
        views.Register<IAgentTreeViewModel, AgentTreeView>();
        views.Register<IOrphanViewModel, OrphanView>();
        views.Register<IStaleWorktreeViewModel, StaleWorktreeView>();
        views.Register<IResourceIndicatorViewModel, ResourceIndicatorView>();
        views.Register<IWorktreesViewModel, WorktreesView>();
        views.Register<IWorktreeRowViewModel, WorktreeRowView>();
        views.Register<IWorktreeDayViewModel, WorktreeDayView>();
    }
}
