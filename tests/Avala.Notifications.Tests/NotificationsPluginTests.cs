using Avala.Jobs.Contracts;
using Avala.Notifications.Contracts;
using Avala.Notifications.Dispatching;
using Avala.Notifications.Records;
using Avala.Sdk;
using Avala.Sdk.Diagnostics;
using Avala.Sdk.Events;
using Avala.Sdk.Presence;
using Avala.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Notifications.Tests;

public sealed class NotificationsPluginTests
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

        Assert.Same(composition.Get<NotificationBook>(), composition.Get<INotifications>());
        Assert.Contains(composition.Get<NotificationDesk>(), composition.All<IHandle<JobProgressed>>());
    }

    [Fact]
    public async Task NoNoticeIsSubscribedToAChildsQuestionToItsOrchestratorAsync()
    {
        await using var data = new TemporaryFolder();
        await using var composition = Compose(data);

        Assert.Empty(composition.All<IHandle<Delegation.Contracts.ParentAsked>>());
    }

    private static PluginComposition Compose(TemporaryFolder data)
    {
        var clock = new FakeTimeProvider(Samples.Monday);

        return PluginComposition.Of(new NotificationsPlugin(), new AvalaPaths(data.Path), services => services
            .AddSingleton<TimeProvider>(clock)
            .AddSingleton<IPresence>(new FakePresence(clock))
            .AddSingleton<IRedaction>(new FakeRedaction())
            .AddSingleton<IJobCatalog>(new FakeCatalog()));
    }
}
