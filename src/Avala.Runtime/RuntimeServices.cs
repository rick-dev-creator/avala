using Avala.Runtime.Appearance;
using Avala.Runtime.Containment;
using Avala.Runtime.Diagnostics;
using Avala.Runtime.Events;
using Avala.Runtime.Presence;
using Avala.Runtime.Processes;
using Avala.Runtime.Telemetry;
using Avala.Runtime.Updates;
using Avala.Sdk;
using Avala.Sdk.Appearance;
using Avala.Sdk.Diagnostics;
using Avala.Sdk.Events;
using Avala.Sdk.Presence;
using Avala.Sdk.Processes;
using Avala.Sdk.Telemetry;
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
                .AddSingleton<LayoutFile>()
                .AddForwarded<IWindowLayout, LayoutFile>()
                .AddSingleton<WindowPresence>()
                .AddForwarded<IPresence, WindowPresence>()
                .AddSingleton<IRedaction>(_ => LogRedaction.FromEnvironment())
                .AddSingleton<UpdatesFile>()
                .AddSingleton(provider => new UpdateCheck(
                    provider.GetService<AvalaBuild>() ?? AvalaBuild.From(Option<string>.None),
                    provider.GetRequiredService<UpdatesFile>(),
                    provider.GetService<ReleaseFeed>().ToOption(),
                    provider.GetRequiredService<IEventBus>()))
                .AddForwarded<IUpdates, UpdateCheck>()
                .AddForwarded<IStartupTask, UpdateCheck>()
                .AddUsageTelemetry(paths);
        }

        public IServiceCollection AddTelemetrySender(HttpMessageHandler handler, string appKey, Option<string> selfHostedHost)
        {
            _ = TelemetryKey.Parse(appKey, selfHostedHost).Match(
                key => services.AddSingleton(provider => new AptabaseSender(
                    new HttpClient(handler) { Timeout = AptabaseSender.Patience },
                    key,
                    provider.GetRequiredService<ILogger<AptabaseSender>>())),
                () => services);

            return services;
        }

        private IServiceCollection AddUsageTelemetry(AvalaPaths paths)
        {
            services.TryAddSingleton(provider => TelemetrySource.Of(
                provider.GetService<AvalaBuild>() ?? AvalaBuild.From(Option<string>.None),
                name => Environment.GetEnvironmentVariable(name).ToOption()));

            return services
                .AddSingleton(new TelemetryFile(paths))
                .AddSingleton(provider => new UsageTelemetry(
                    provider.GetRequiredService<TelemetryFile>(),
                    provider.GetRequiredService<TelemetrySource>(),
                    provider.GetService<AptabaseSender>().ToOption(),
                    provider.GetRequiredService<TimeProvider>()))
                .AddForwarded<ITelemetry, UsageTelemetry>()
                .AddForwarded<IStartupTask, UsageTelemetry>();
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
