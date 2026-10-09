using Avala.Runtime.Appearance;
using Avala.Runtime.Containment;
using Avala.Runtime.Diagnostics;
using Avala.Runtime.Events;
using Avala.Runtime.Processes;
using Avala.Runtime.Updates;
using Avala.Sdk;
using Avala.Sdk.Appearance;
using Avala.Sdk.Events;
using Avala.Sdk.Processes;
using Avala.Sdk.Updates;
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
                .AddForwarded<ILoggerProvider, LogFile>()
                .AddSingleton(paths)
                .AddSingleton<EventBus>()
                .AddForwarded<IEventBus, EventBus>()
                .AddForwarded<IEventFeed, EventBus>()
                .AddSingleton(Containment())
                .AddSingleton(ListeningPorts())
                .AddSingleton<ProcessTrees>()
                .AddForwarded<IProcessTrees, ProcessTrees>()
                .AddSingleton<IProcessRunner, ProcessRunner>()
                .AddSingleton<AppearanceFile>()
                .AddForwarded<IAppearance, AppearanceFile>()
                .AddForwarded<IStartupTask, AppearanceFile>()
                .AddSingleton<UpdatesFile>()
                .AddSingleton(provider => new UpdateCheck(
                    provider.GetService<AvalaBuild>() ?? AvalaBuild.From(Option<string>.None),
                    provider.GetRequiredService<UpdatesFile>(),
                    provider.GetService<ReleaseFeed>().ToOption(),
                    provider.GetRequiredService<IEventBus>()))
                .AddForwarded<IUpdates, UpdateCheck>()
                .AddForwarded<IStartupTask, UpdateCheck>();
        }

        public IServiceCollection AddReleaseFeed(HttpMessageHandler handler) =>
            services.AddSingleton(_ => new ReleaseFeed(new HttpClient(handler) { Timeout = ReleaseFeed.Patience }));
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
