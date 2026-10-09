using Avala.Runtime.Appearance;
using Avala.Runtime.Containment;
using Avala.Runtime.Diagnostics;
using Avala.Runtime.Events;
using Avala.Runtime.Processes;
using Avala.Sdk;
using Avala.Sdk.Appearance;
using Avala.Sdk.Events;
using Avala.Sdk.Processes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Avala.Runtime;

public static class RuntimeServices
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddRuntime(AvalaPaths paths)
        {
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton(provider => new LogFile(paths, provider.GetRequiredService<TimeProvider>()));

            return services
                .AddSingleton<ILoggerProvider>(provider => provider.GetRequiredService<LogFile>())
                .AddSingleton(paths)
                .AddSingleton<EventBus>()
                .AddSingleton<IEventBus>(provider => provider.GetRequiredService<EventBus>())
                .AddSingleton<IEventFeed>(provider => provider.GetRequiredService<EventBus>())
                .AddSingleton(Containment())
                .AddSingleton(ListeningPorts())
                .AddSingleton<ProcessTrees>()
                .AddSingleton<IProcessTrees>(provider => provider.GetRequiredService<ProcessTrees>())
                .AddSingleton<IProcessRunner, ProcessRunner>()
                .AddSingleton<AppearanceFile>()
                .AddSingleton<IAppearance>(provider => provider.GetRequiredService<AppearanceFile>())
                .AddSingleton<IStartupTask>(provider => provider.GetRequiredService<AppearanceFile>());
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
