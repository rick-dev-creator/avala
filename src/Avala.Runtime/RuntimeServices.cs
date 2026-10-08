using Avala.Runtime.Events;
using Avala.Runtime.Processes;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.Processes;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Runtime;

public static class RuntimeServices
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddRuntime(AvalaPaths paths) =>
            services
                .AddSingleton(paths)
                .AddSingleton<EventBus>()
                .AddSingleton<IEventBus>(provider => provider.GetRequiredService<EventBus>())
                .AddSingleton<IEventFeed>(provider => provider.GetRequiredService<EventBus>())
                .AddSingleton<IProcessRunner, ProcessRunner>();
    }
}
