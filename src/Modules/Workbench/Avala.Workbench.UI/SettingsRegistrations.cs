using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.UI;
using Avala.Workbench.Following;
using Avala.Workbench.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Workbench.UI;

internal static class SettingsRegistrations
{
    public static IServiceCollection AddWorktreeSettings(this IServiceCollection services) =>
        services
            .AddForwarded<IHandle<WorktreeFreed>, Pulse>()
            .AddSingleton<IWorktreeSettingsViewModel, WorktreeSettingsViewModel>();

    public static void RegisterViews(IViewRegistrar views)
    {
        views.Register<INotificationsViewModel, NotificationsView>();
        views.Register<IWorktreeSettingsViewModel, WorktreeSettingsView>();
        views.Register<IWorktreeRuleViewModel, WorktreeRuleView>();
        views.Register<IWorktreeLimitViewModel, WorktreeLimitView>();
    }
}
