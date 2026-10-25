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
        views.Register<IFleetRangeViewModel, FleetRangeView>();
        views.Register<IFleetRangeOptionViewModel, FleetRangeOptionView>();
        views.Register<IFleetTileViewModel, FleetTileView>();
        views.Register<IFleetTrendViewModel, FleetTrendView>();
        views.Register<IFleetColumnViewModel, FleetColumnView>();
        views.Register<IFleetShareListViewModel, FleetShareListView>();
        views.Register<IFleetShareRowViewModel, FleetShareRowView>();
        views.Register<IFleetRepositoryJobsViewModel, FleetRepositoryJobsView>();
        views.Register<IFleetJobRowViewModel, FleetJobRowView>();
    }
}
