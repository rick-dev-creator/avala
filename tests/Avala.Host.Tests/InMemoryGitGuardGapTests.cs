using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Host.Tests;

public sealed class InMemoryGitGuardGapTests(PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("env")]
    [InlineData("xargs")]
    [InlineData("shell that cds into the repository")]
    [InlineData("shell naming git by its path")]
    [InlineData("shell naming git through command substitution")]
    [InlineData("git-dir option with a separate relative path")]
    public async Task GitRunOnAnInMemoryRepositoryFailsTheRunHoweverItIsLaunchedAsync(string launched)
    {
        var run = await SimulatedRun.PreparedAsync(plugins);
        var repository = run.Repository.Path;
        var parent = Path.GetDirectoryName(repository)!;
        var name = Path.GetFileName(repository);
        var request = launched switch
        {
            "env" => new ProcessRequest("env", ["git", "-C", repository, "log", "--format=%s"]),
            "xargs" => new ProcessRequest("xargs", ["--arg-file=/dev/null", "git", "-C", repository, "log", "--format=%s"]),
            "shell that cds into the repository" => new ProcessRequest("sh", ["-c", $"cd {name} && git log --format=%s"], parent),
            "shell naming git by its path" => new ProcessRequest("sh", ["-c", $"/usr/bin/git -C '{repository}' log --format=%s"]),
            "shell naming git through command substitution" => new ProcessRequest("sh", ["-c", $"\"$(command -v git)\" -C '{repository}' log --format=%s"]),
            _ => new ProcessRequest("git", ["--git-dir", Path.Combine(name, ".git"), "log", "--format=%s"], parent),
        };
        _ = await run.Get<IProcessRunner>().RunAsync(request, Cancellation);

        var failure = await Record.ExceptionAsync(async () => await run.DisposeAsync());

        Assert.Contains("[UsesRealGit]", failure?.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GitRunInAShellOnAnInMemoryRepositoryWhosePathHoldsASpaceFailsTheRunAsync()
    {
        var run = await SimulatedRun.PreparedAsync(plugins, [], [], repositoryName: "my shop");
        _ = await run.Get<IProcessRunner>().RunAsync(new ProcessRequest("sh", ["-c", $"git -C '{run.Repository.Path}' log --format=%s"]), Cancellation);

        var failure = await Record.ExceptionAsync(async () => await run.DisposeAsync());

        Assert.Contains("[UsesRealGit]", failure?.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GitRunOnASimulatedForgesBareRemoteAfterItWasClonedDoesNotFailTheRunAsync()
    {
        var run = await SimulatedRun.PreparedAsync(plugins);
        await using var forge = new TemporaryFolder();
        var remote = Path.Combine(forge.Path, "shop.git");
        var processes = run.Get<IProcessRunner>();
        _ = Outcomes.Succeeds(await processes.RunAsync(new ProcessRequest("git", ["init", "--quiet", "--bare", "--initial-branch=main", remote]), Cancellation));
        _ = await run.Get<IRemoteRepositories>().CloneAsync(remote, forge.Path, "shop", Cancellation);
        _ = await processes.RunAsync(new ProcessRequest("git", ["-C", remote, "rev-parse", "--verify", "--quiet", "refs/heads/main"]), Cancellation);

        var failure = await Record.ExceptionAsync(async () => await run.DisposeAsync());

        Assert.Null(failure);
    }

    [Fact]
    public async Task GitRunOnARepositoryMadeWithRealGitAfterAvalaListedItsFilesDoesNotFailTheRunAsync()
    {
        var run = await SimulatedRun.PreparedAsync(plugins);
        var other = await TemporaryRepository.CreateAsync(run.Get<IProcessRunner>(), Cancellation);
        _ = await run.Get<IRepositoryIndex>().FilesAsync(other.Path, 10, Cancellation);
        await other.CommitAsync("ledger.txt", "ledger\n", Cancellation);

        var failure = await Record.ExceptionAsync(async () => await run.DisposeAsync());
        await other.DisposeAsync();

        Assert.Null(failure);
    }
}
