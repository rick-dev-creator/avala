using Avala.Sdk.Regions;
using Avala.Shell.Regions;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Shell;

public static class ShellServices
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddShell()
        {
            services.TryAddSingleton<IMessenger>(new WeakReferenceMessenger());

            return services
                .AddSingleton<RegionContexts>()
                .AddSingleton<IRegions>(provider => provider.GetRequiredService<RegionContexts>())
                .AddSingleton<ShellViewModel>();
        }
    }
}
