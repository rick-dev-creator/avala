using Avala.Jobs.Contracts;
using Avala.Preview.Detection;
using Avala.Preview.Running;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
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

            return services
                .AddSingleton<PreviewBook>()
                .AddSingleton<RunBook>()
                .AddSingleton<PreviewDetector>()
                .AddForwarded<IHandle<ResourcesSampled>, PreviewDetector>()
                .AddForwarded<IHandle<JobProgressed>, PreviewDetector>();
        }
    }
}
