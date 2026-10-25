using Avala.Sdk;
using Avala.Sdk.UI;
using Avala.Workbench.FleetActivity;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Workbench.UI;

internal static class FleetActivityRegistration
{
    public static IServiceCollection AddFleetActivity(this IServiceCollection services) =>
        services
            .AddSingleton<FleetActivityViewModel>()
            .AddForwarded<IPage, FleetActivityViewModel>();

    public static void RegisterFleetViews(this IViewRegistrar views)
    {
        views.Register<IFleetActivityViewModel, FleetActivityView>();
        views.Register<IFleetRepositoryRowViewModel, FleetRepositoryRowView>();
        views.Register<IFleetJobRowViewModel, FleetJobRowView>();
    }
}
