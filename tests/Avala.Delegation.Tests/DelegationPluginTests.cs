using Avala.Agents.Contracts;
using Avala.Budgets.Contracts;
using Avala.Delegation.Contracts;
using Avala.Delegation.Delegating;
using Avala.Delegation.Escalating;
using Avala.Delegation.Records;
using Avala.Delegation.Tests.Delegating;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Testing;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Delegation.Tests;

public sealed class DelegationPluginTests
{
    [Fact]
    public async Task TheRegistrationResolvesEveryServiceItRegistersAsync()
    {
        await using var data = new TemporaryFolder();
        await using var composition = Compose(data);

        Assert.Equal(composition.Registered, composition.ResolveEveryRegistration());
    }

    [Fact]
    public async Task EachServiceOfferedUnderSeveralContractsIsOneInstanceAsync()
    {
        await using var data = new TemporaryFolder();
        await using var composition = Compose(data);

        Assert.Same(composition.Get<DelegationBook>(), composition.Get<IDelegations>());
        Assert.Contains(composition.Get<DelegationBook>(), composition.All<IStartupTask>());
        Assert.Same(composition.Get<Storage.SqliteDelegationStore>(), composition.Get<IDelegationStore>());
        Assert.Contains(composition.Get<Storage.SqliteDelegationStore>(), composition.All<IStartupTask>());
        Assert.Same(composition.Get<DelegationDesk>(), composition.Get<IHandle<SessionOpened>>());
        Assert.Same(composition.Get<DelegationDesk>(), composition.Get<IHandle<JobSessionStarted>>());
        Assert.Contains(composition.Get<DelegationDesk>(), composition.All<IHandle<AgentActivity>>());
        Assert.Contains(composition.Get<ChildAnswers>(), composition.All<IHandle<AgentActivity>>());
        Assert.Same(composition.Get<ChildTerms>(), composition.Get<IJobTerms>());
        Assert.Same(composition.Get<ParentNotes>(), composition.Get<IHandle<PermissionDecided>>());
        Assert.Same(composition.Get<ParentNotes>(), composition.Get<IHandle<FormDecided>>());
        Assert.Same(composition.Get<DelegationDesk>(), composition.Get<IHandle<JobProgressed>>());
        Assert.Same(composition.Get<DelegationDesk>(), composition.Get<IHandle<JobHeld>>());
    }

    private static PluginComposition Compose(TemporaryFolder data)
    {
        var jobs = new FakeJobs();

        return PluginComposition.Of(new DelegationPlugin(), new AvalaPaths(data.Path), services => services
            .AddSingleton<IJobs>(jobs)
            .AddSingleton<IJobCatalog>(jobs)
            .AddSingleton<IAgents>(new ReturningAgents())
            .AddSingleton<IPermissionAudit>(new FixedAudit())
            .AddSingleton<IParentAnswers>(new RecordingParentAnswers())
            .AddSingleton<IUsage>(new FixedUsage())
            .AddSingleton<IBudgets>(new FixedBudgets())
            .AddSingleton<IWorkspaceChanges>(new FixedChanges())
            .AddSingleton<IVerifications>(new FixedVerifications())
            .AddSingleton<IBaseFiles>(new CommittedFiles()));
    }
}
