using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Verification.Contracts;
using Avala.Verification.Discovering;
using Avala.Verification.Evidence;
using Avala.Verification.Storage;
using Avala.Verification.Tests.Discovering;
using Avala.Verification.Tests.Verifying;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Verification.Tests;

public sealed class VerificationPluginTests
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

        Assert.Same(composition.Get<SqliteEvidenceStore>(), composition.Get<IEvidenceStore>());
        Assert.Contains(composition.Get<SqliteEvidenceStore>(), composition.All<IStartupTask>());
        Assert.Same(composition.Get<EvidenceBook>(), composition.Get<IVerifications>());
        Assert.Contains(composition.Get<EvidenceBook>(), composition.All<IStartupTask>());
        Assert.Same(composition.Get<SqliteEvidenceStore>(), composition.Get<IDiscoveryStore>());
        Assert.Same(composition.Get<DiscoveryBook>(), composition.Get<IChecksDiscoveries>());
        Assert.Contains(composition.Get<DiscoveryBook>(), composition.All<IStartupTask>());
        Assert.Same(composition.Get<DiscoveryDesk>(), composition.Get<IChecksProposals>());
    }

    private static PluginComposition Compose(TemporaryFolder data) =>
        PluginComposition.Of(new VerificationPlugin(), new AvalaPaths(data.Path), services => services
            .AddSingleton<IProcessRunner>(new ScriptedProcesses(new FakeTimeProvider()))
            .AddSingleton<IBaseFiles>(new CommittedFiles())
            .AddSingleton<IWorkingFiles>(new Scouted.WorkingFolders())
            .AddSingleton<Scouted.FakeJobs>()
            .AddSingleton<IJobs>(provider => provider.GetRequiredService<Scouted.FakeJobs>())
            .AddSingleton<IJobCatalog>(provider => provider.GetRequiredService<Scouted.FakeJobs>())
            .AddSingleton<IWorkspaces>(provider => provider.GetRequiredService<Scouted.FakeJobs>())
            .AddSingleton<IScratchRepositories>(new Scouted.Scratch())
            .AddSingleton<IRoleSettings>(new Scouted.FixedRoles())
            .AddSingleton<IWorkspaceChanges>(provider => provider.GetRequiredService<Scouted.FakeJobs>())
            .AddSingleton<Permissions.Contracts.IRepositoryPolicies>(new Scouted.FakePolicies())
            .AddSingleton<Permissions.Contracts.IPermissionAnswers>(new Scouted.FakeAnswers())
            .AddSingleton<Agents.Contracts.IAgents>(new Scouted.FakeAgents())
            .AddSingleton<IEventBus>(new RecordingBus()));
}
