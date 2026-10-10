using Avala.Agents.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Observability.Tracking;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Testing;

namespace Avala.Observability.Tests;

public sealed class ObservabilityPluginTests
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

        Assert.Same(composition.Get<UsageBook>(), composition.Get<IUsage>());
        Assert.Same(composition.Get<UsageBook>(), composition.Get<IUsageSessions>());
        Assert.Same(composition.Get<UsageBook>(), composition.Get<IStartupTask>());
        Assert.Contains(composition.Get<Avala.Observability.Storage.SqliteUsageStore>(), composition.All<IStartupTask>());
        Assert.Same(composition.Get<UsageTracker>(), composition.Get<IHandle<SessionOpened>>());
        Assert.Same(composition.Get<UsageTracker>(), composition.Get<IHandle<JobSessionStarted>>());
        Assert.Same(composition.Get<UsageTracker>(), composition.Get<IHandle<AgentActivity>>());
    }

    private static PluginComposition Compose(TemporaryFolder data) =>
        PluginComposition.Of(new ObservabilityPlugin(), new AvalaPaths(data.Path), _ => { });
}
