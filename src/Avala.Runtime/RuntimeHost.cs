using Avala.Runtime.Events;
using Avala.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Runtime;

public static class RuntimeHost
{
    extension(IServiceProvider services)
    {
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            var loop = services.GetRequiredService<EventBus>().RunAsync(cancellationToken);

            foreach (var task in services.GetServices<IStartupTask>())
            {
                await task.RunAsync(cancellationToken);
            }

            await loop;
        }
    }
}
