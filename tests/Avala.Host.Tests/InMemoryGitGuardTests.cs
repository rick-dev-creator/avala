using Avala.Sdk.Processes;

namespace Avala.Host.Tests;

public sealed class InMemoryGitGuardTests(PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GitRunThroughTheProcessRunnerOnAnInMemoryRepositoryFailsSayingTheTestShouldBeMarkedAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins);

        var failure = await Record.ExceptionAsync(async () => await run.Get<IProcessRunner>().RunAsync(new ProcessRequest("git", ["-C", run.Repository.Path, "log", "--format=%s"]), Cancellation));

        Assert.Contains("[UsesRealGit]", failure?.Message ?? "git ran on the in-memory repository and the test was not told to be marked", StringComparison.Ordinal);
    }
}
