using System.Collections.Immutable;
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

    [Theory]
    [InlineData("shell")]
    [InlineData("relative")]
    [InlineData("git-dir option")]
    [InlineData("git-dir variable")]
    [InlineData("work-tree option")]
    public async Task GitRunOnAnInMemoryRepositoryFailsTheRunHoweverItNamesTheRepositoryAsync(string named)
    {
        var run = await SimulatedRun.PreparedAsync(plugins);
        var repository = run.Repository.Path;
        var parent = Path.GetDirectoryName(repository)!;
        var request = named switch
        {
            "shell" => new ProcessRequest("sh", ["-c", $"git -C '{repository}' log --format=%s"]),
            "relative" => new ProcessRequest("git", ["-C", Path.GetFileName(repository), "log", "--format=%s"], parent),
            "git-dir option" => new ProcessRequest("git", [$"--git-dir={Path.Combine(repository, ".git")}", "log", "--format=%s"]),
            "git-dir variable" => new ProcessRequest("git", ["log", "--format=%s"]) { Environment = ImmutableDictionary<string, string>.Empty.Add("GIT_DIR", Path.Combine(repository, ".git")) },
            _ => new ProcessRequest("git", [$"--work-tree={repository}", "status", "--porcelain"]),
        };
        _ = await run.Get<IProcessRunner>().RunAsync(request, Cancellation);

        var failure = await Record.ExceptionAsync(async () => await run.DisposeAsync());

        Assert.Contains("[UsesRealGit]", failure?.Message ?? string.Empty, StringComparison.Ordinal);
    }
}
