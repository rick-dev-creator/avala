using Avala.Agents.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Supervision.Contracts;
using Avala.Supervision.Supervising;
using Avala.Supervision.Tests.Supervising;
using Avala.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Supervision.Tests;

public sealed class SupervisionPluginTests
{
    [Fact]
    public async Task TheRegistrationResolvesEveryServiceItRegistersAsync()
    {
        await using var data = new TemporaryFolder();
        await using var composition = Compose(data);

        Assert.Equal(composition.Registered, composition.ResolveEveryRegistration());
    }

    [Fact]
    public async Task EveryHandlerIsSubscribedToEachEventItHandlesAsync()
    {
        await using var data = new TemporaryFolder();
        await using var composition = Compose(data);

        Assert.Empty(composition.EventsHandledButNotSubscribed());
    }

    [Fact]
    public async Task EachServiceOfferedUnderSeveralContractsIsOneInstanceAsync()
    {
        await using var data = new TemporaryFolder();
        await using var composition = Compose(data);

        Assert.Same(composition.Get<SupervisionBook>(), composition.Get<ISupervision>());
        Assert.Same(composition.Get<SupervisionBook>(), composition.Get<IStartupTask>());
        Assert.Same(composition.Get<Watchdog>(), composition.Get<IHandle<JobProgressed>>());
        Assert.Same(composition.Get<Watchdog>(), composition.Get<IHandle<JobSessionStarted>>());
        Assert.Same(composition.Get<Watchdog>(), composition.Get<IHandle<AgentActivity>>());
        Assert.Same(composition.Get<Watchdog>(), composition.Get<IHandle<SilenceNoticed>>());
    }

    private static PluginComposition Compose(TemporaryFolder data) =>
        PluginComposition.Of(new SupervisionPlugin(), new AvalaPaths(data.Path), services => services
            .AddSingleton<IJobs>(new Supervised.HoldingJobs(default)));
}
