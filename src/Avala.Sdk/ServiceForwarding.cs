using Microsoft.Extensions.DependencyInjection;

namespace Avala.Sdk;

public static class ServiceForwarding
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddForwarded<TService, TImplementation>()
            where TService : class
            where TImplementation : class, TService =>
            services.AddSingleton<TService>(provider => provider.GetRequiredService<TImplementation>());
    }
}
