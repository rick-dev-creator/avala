using Avala.Runtime.Containment;
using Avala.Runtime.Events;
using Avala.Runtime.Processes;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.Processes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Runtime;

public static class RuntimeServices
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddRuntime(AvalaPaths paths)
        {
            services.TryAddSingleton(TimeProvider.System);

            return services
                .AddSingleton(paths)
                .AddSingleton<EventBus>()
                .AddSingleton<IEventBus>(provider => provider.GetRequiredService<EventBus>())
                .AddSingleton<IEventFeed>(provider => provider.GetRequiredService<EventBus>())
                .AddSingleton(Containment())
                .AddSingleton(ListeningPorts())
                .AddSingleton<ProcessTrees>()
                .AddSingleton<IProcessTrees>(provider => provider.GetRequiredService<ProcessTrees>())
                .AddSingleton<IProcessRunner, ProcessRunner>();
        }
    }

    private static IContainment Containment() =>
        OperatingSystem.IsWindows() ? new WindowsContainment()
        : OperatingSystem.IsMacOS() ? new MacContainment()
        : new LinuxContainment();

    private static IListeningPorts ListeningPorts() =>
        OperatingSystem.IsWindows() ? new WindowsListeningPorts()
        : OperatingSystem.IsMacOS() ? new MacListeningPorts()
        : new LinuxListeningPorts();
}
