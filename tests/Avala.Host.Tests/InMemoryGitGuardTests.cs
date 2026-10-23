using Avala.Sdk.Processes;

namespace Avala.Host.Tests;

public sealed class InMemoryGitGuardTests(PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GitRunOnAnInMemoryRepositoryFailsTheRunWhenItEndsSayingTheTestShouldBeMarkedAsync()
    {
        var run = await SimulatedRun.PreparedAsync(plugins);
        _ = await run.Get<IProcessRunner>().RunAsync(new ProcessRequest("git", ["-C", run.Repository.Path, "log", "--format=%s"]), Cancellation);

        var failure = await Record.ExceptionAsync(async () => await run.DisposeAsync());

        Assert.Contains("git -C", failure?.Message ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("[UsesRealGit]", failure?.Message ?? string.Empty, StringComparison.Ordinal);
    }
}
