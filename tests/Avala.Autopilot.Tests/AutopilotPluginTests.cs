using Avala.Agents.Contracts;
using Avala.Autopilot.Contracts;
using Avala.Autopilot.FollowUps;
using Avala.Autopilot.Looping;
using Avala.Autopilot.Sourcing;
using Avala.Autopilot.Tests.FollowUps;
using Avala.Autopilot.Tests.Looping;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Testing;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Autopilot.Tests;

public sealed class AutopilotPluginTests
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

        Assert.Same(composition.Get<LoopRegistry>(), composition.Get<IAutopilot>());
        Assert.Same(composition.Get<LoopFeed>(), composition.Get<IHandle<JobProgressed>>());
        Assert.Same(composition.Get<LoopFeed>(), composition.Get<IHandle<JobHeld>>());
        Assert.Same(composition.Get<LoopFeed>(), composition.Get<IHandle<PermissionDecided>>());
        Assert.Same(composition.Get<LoopFeed>(), composition.Get<IHandle<FormDecided>>());
        Assert.Same(composition.Get<FollowUpDesk>(), composition.Get<IHandle<SessionOpened>>());
        Assert.Same(composition.Get<FollowUpDesk>(), composition.Get<IHandle<JobSessionStarted>>());
        Assert.Same(composition.Get<FollowUpDesk>(), composition.Get<IHandle<AgentActivity>>());
        Assert.Equal(3, composition.All<IJobSource>().Count);
    }

    private static PluginComposition Compose(TemporaryFolder data)
    {
        var jobs = new FakeJobs();
        var evidence = new FakeEvidence();
        var work = new FakeWork();
        var usage = new FakeUsage();

        return PluginComposition.Of(new AutopilotPlugin(), new AvalaPaths(data.Path), services => services
            .AddSingleton<IJobs>(jobs)
            .AddSingleton<IJobCatalog>(jobs)
            .AddSingleton<IVerifications>(evidence)
            .AddSingleton<IPermissionAudit>(evidence)
            .AddSingleton<IWorkspaces>(work)
            .AddSingleton<IWorkspaceChanges>(work)
            .AddSingleton<IUsage>(usage)
            .AddSingleton<IUsageHistory>(usage)
            .AddSingleton<Avala.Agents.Contracts.Connections.IConnections>(new FakeConnections())
            .AddSingleton<IAgents>(new FollowUpTests.ReturningAgents())
            .AddSingleton<IBaseFiles>(new CommittedFiles()));
    }
}
