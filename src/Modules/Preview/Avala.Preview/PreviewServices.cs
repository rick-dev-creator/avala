using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Preview;

internal static class PreviewServices
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddPreview()
        {
            services.TryAddSingleton(TimeProvider.System);

            return services;
        }
    }
}
