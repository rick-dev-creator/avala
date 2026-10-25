using Avala.Agents.Contracts;
using Avala.Fleet.Connecting;
using Avala.Fleet.Contracts;
using Avala.Fleet.Delivering;
using Avala.Fleet.FleetFiles;
using Avala.Fleet.Keychain;
using Avala.Fleet.Reading;
using Avala.Fleet.Recording;
using Avala.Fleet.Server;
using Avala.Fleet.Settings;
using Avala.Fleet.Storage;
using Avala.Fleet.Workspace;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.Regions;
using Avala.Sdk.UI;
using Avala.Verification.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Fleet;

public sealed class FleetPlugin : IPlugin, IViewContributor
{
    public PluginInfo Info { get; } = new("avala.fleet", "Fleet");

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.TryAddSingleton(TimeProvider.System);
        registrar.Services
            .AddSingleton<SqliteFactStore>()
            .AddForwarded<IFactStore, SqliteFactStore>()
            .AddForwarded<IStartupTask, SqliteFactStore>()
            .AddSingleton<Outbox>()
            .AddSingleton<IFleetFile, FleetFileStore>()
            .AddSingleton(provider => provider.GetKeyedService<IKeychain>(FleetTransport.Handler) is { } keychain ? new FleetKeys(keychain) : new FleetKeys(PlatformKeychain.Create()))
            .AddSingleton(provider => new FleetSession(provider.GetRequiredService<IFleetFile>(), provider.GetRequiredService<FleetKeys>().Keychain))
            .AddForwarded<IStartupTask, FleetSession>()
            .AddSingleton(Http)
            .AddSingleton<IFleetServer, HttpFleetServer>()
            .AddSingleton<IJobFacts, CatalogJobFacts>()
            .AddSingleton<FleetRecorder>()
            .AddForwarded<IHandle<JobSubmitted>, FleetRecorder>()
            .AddForwarded<IHandle<JobProgressed>, FleetRecorder>()
            .AddForwarded<IHandle<JobHeld>, FleetRecorder>()
            .AddForwarded<IHandle<JobModelChosen>, FleetRecorder>()
            .AddForwarded<IHandle<JobSessionStarted>, FleetRecorder>()
            .AddForwarded<IHandle<AgentActivity>, FleetRecorder>()
            .AddForwarded<IHandle<AttemptVerified>, FleetRecorder>()
            .AddForwarded<IHandle<SessionEnded>, FleetRecorder>()
            .AddSingleton<FleetCourier>()
            .AddForwarded<IHandle<StartupCompleted>, FleetCourier>()
            .AddSingleton(provider => new FleetConnection(
                provider.GetRequiredService<FleetSession>(),
                provider.GetRequiredService<Outbox>(),
                provider.GetRequiredService<IFleetServer>(),
                provider.GetRequiredService<FleetKeys>().Keychain))
            .AddSingleton<IFleetActivity, FleetActivityReader>()
            .AddSingleton<FleetSettingsViewModel>();
        registrar.AddToRegion<FleetSettingsViewModel>(WorkbenchRegions.Fleet, 0);
    }

    public void RegisterViews(IViewRegistrar views)
    {
        views.Register<IFleetSettingsViewModel, UI.FleetSettingsView>();
        views.Register<IFleetRejectionRowViewModel, UI.FleetRejectionRowView>();
    }

    private static FleetHttp Http(IServiceProvider services) =>
        services.GetKeyedService<HttpMessageHandler>(FleetTransport.Handler) is { } handler
            ? new FleetHttp(handler, owned: false)
            : new FleetHttp(new SocketsHttpHandler(), owned: true);
}
