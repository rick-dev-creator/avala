using Avala.Forges.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class RepositoryPublishingTests(PublishedPlugins plugins)
{
    private static readonly (string File, string Content)[] LocalForge =
    [
        ("forges.json", """{ "pollSeconds": 60, "forges": [{ "name": "local", "forge": "simulated" }] }"""),
    ];

    private static readonly PublishTarget Local = new(new ForgeName("local"), "Simulated forge");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PublishingARepositoryWithoutARemoteCreatesItOnTheForgePushesItsBranchAndMakesItsOriginKnownAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, LocalForge, []);
        var publishing = run.Get<IRepositoryPublishing>();
        var before = await publishing.RemoteOfAsync(run.Repository.Path, Cancellation);

        var published = Outcomes.Succeeds(await publishing.PublishAsync(run.Repository.Path, Local.Forge, new RepositoryDraft("shop", Private: true), Cancellation));

        var bare = Path.Combine(run.DataFolder, "simulated-forge", "avala", "shop.git");
        Assert.True(before.IsNone);
        Assert.Equal([Local], await publishing.TargetsAsync(Cancellation));
        Assert.Equal((Local, "main", bare), (published.Forge, published.Branch, published.PushUrl));
        Assert.Equal(await run.Repository.GitAsync(Cancellation, "rev-parse", "HEAD"), await run.Repository.GitInAsync(bare, Cancellation, "rev-parse", "refs/heads/main"));
        Assert.Equal("origin/main", await run.Repository.GitAsync(Cancellation, "rev-parse", "--abbrev-ref", "main@{upstream}"));
        Assert.Equal(Option<PublishTarget>.Some(Local), Outcomes.Present(await publishing.RemoteOfAsync(run.Repository.Path, Cancellation)).Forge);
    }
}
