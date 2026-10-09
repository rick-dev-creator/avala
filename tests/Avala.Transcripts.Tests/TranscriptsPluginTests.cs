using Avala.Agents.Contracts;
using Avala.Canvas.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Testing;
using Avala.Transcripts.Keeping;
using Avala.Transcripts.Storage;
using Avala.Transcripts.Tests.Keeping;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Transcripts.Tests;

public sealed class TranscriptsPluginTests
{
    [Fact]
    public async Task TheRegistrationResolvesEveryServiceItRegistersAsync()
    {
        await using var data = new TemporaryFolder();
        await using var composition = Compose(data);

        Assert.Equal(composition.Registered, composition.ResolveEveryRegistration());
    }

    [Fact]
    public async Task OneLogAndOneKeeperServeEveryContractTheyAreOfferedUnderAsync()
    {
        await using var data = new TemporaryFolder();
        await using var composition = Compose(data);

        Assert.Same(composition.Get<SqliteTranscriptLog>(), composition.Get<ITranscriptLog>());
        Assert.Contains(composition.Get<SqliteTranscriptLog>(), composition.All<IStartupTask>());
        Assert.All(
            new object[]
            {
                composition.Get<IHandle<JobSessionStarted>>(),
                composition.Get<IHandle<JobProgressed>>(),
                composition.Get<IHandle<AgentActivity>>(),
                composition.Get<IHandle<CanvasUpdated>>(),
                composition.Get<IHandle<PermissionDecided>>(),
                composition.Get<IHandle<FormDecided>>(),
                composition.Get<IHandle<WorktreeReclaimed>>(),
            },
            handler => Assert.Same(composition.Get<TranscriptKeeper>(), handler));
    }

    private static PluginComposition Compose(TemporaryFolder data) =>
        PluginComposition.Of(new TranscriptsPlugin(), new AvalaPaths(data.Path), services => services.AddSingleton<IJobCatalog, FakeCatalog>());
}
