using Avala.Sdk.UI;
using Avala.Workbench.Overview;
using Avala.Workbench.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Workbench.UI;

internal static class RelayRegistrations
{
    public static IServiceCollection AddRelays(this IServiceCollection services) =>
        services
            .AddSingleton<RelaysReader>()
            .AddSingleton<RelaysPaneViewModel>()
            .AddSingleton<RelaysToggleViewModel>()
            .AddSingleton<UsageLimitsViewModel>();

    public static void RegisterViews(IViewRegistrar views)
    {
        views.Register<IRelaysPaneViewModel, RelaysPaneView>();
        views.Register<IRelaysToggleViewModel, RelaysToggleView>();
        views.Register<IRelayLaneViewModel, RelayLaneView>();
        views.Register<IRelayLegViewModel, RelayLegView>();
        views.Register<IUsageLimitsViewModel, UsageLimitsView>();
        views.Register<ILimitConnectionViewModel, LimitConnectionView>();
    }
}
