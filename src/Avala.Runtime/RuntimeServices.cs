using Avala.Runtime.Events;
using Avala.Sdk.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Runtime;

public static class RuntimeServices
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddRuntime() =>
            services
                .AddSingleton<EventBus>()
                .AddSingleton<IEventBus>(provider => provider.GetRequiredService<EventBus>())
                .AddSingleton<IEventFeed>(provider => provider.GetRequiredService<EventBus>());
    }
}
