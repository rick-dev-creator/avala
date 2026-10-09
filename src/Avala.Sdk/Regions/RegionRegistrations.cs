using Microsoft.Extensions.DependencyInjection;

namespace Avala.Sdk.Regions;

public static class RegionRegistrations
{
    extension(IPluginRegistrar registrar)
    {
        public IPluginRegistrar AddToRegion<TViewModel>(RegionName region, int order)
            where TViewModel : class
        {
            registrar.Services.AddSingleton(services =>
                new RegionContribution(region, order, services.GetRequiredService<TViewModel>()));

            return registrar;
        }
    }
}
