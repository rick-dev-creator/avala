using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Verification.Contracts;
using Avala.Verification.Evidence;
using Avala.Verification.Storage;
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
    }

    private static PluginComposition Compose(TemporaryFolder data) =>
        PluginComposition.Of(new VerificationPlugin(), new AvalaPaths(data.Path), services => services
            .AddSingleton<IProcessRunner>(new ScriptedProcesses(new FakeTimeProvider()))
            .AddSingleton<IBaseFiles>(new CommittedFiles()));
}
