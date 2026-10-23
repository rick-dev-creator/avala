using Avala.Runtime;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Workspaces.Tests.Conformance;

public sealed class BareRemoteFidelityTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CloningAnEmptyBareRepositoryLeavesAnEmptyCheckoutAsync(bool realGit)
    {
        await using var bench = await WorkspaceBench.CreateAsync(realGit, Cancellation);
        await using var forge = new TemporaryFolder();
        var remote = await BareAsync(forge, Cancellation);

        var cloned = await bench.Bench.Remotes.CloneAsync(remote, forge.Path, "shop", Cancellation);

        Assert.True(cloned.IsSuccess, cloned.ToString());
        Assert.Equal([".git"], Directory.EnumerateFileSystemEntries(Path.Combine(forge.Path, "shop")).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ACloneOfTheRemoteARepositoryWasPublishedToHoldsItsFilesAsync(bool realGit)
    {
        await using var bench = await WorkspaceBench.CreateAsync(realGit, Cancellation);
        await using var forge = new TemporaryFolder();
        var remote = await BareAsync(forge, Cancellation);
        _ = Outcomes.Succeeds(await bench.Bench.Remotes.PublishAsync(bench.Bench.Repository, "origin", remote, Cancellation));

        _ = Outcomes.Succeeds(await bench.Bench.Remotes.CloneAsync(remote, forge.Path, "shop", Cancellation));

        Assert.Equal("# Shop\n", await File.ReadAllTextAsync(Path.Combine(forge.Path, "shop", "README.md"), Cancellation));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AFolderWhereGitWasJustInitializedHasNoCommitAsync(bool realGit)
    {
        await using var bench = await WorkspaceBench.CreateAsync(realGit, Cancellation);
        await using var folder = new TemporaryFolder();
        var outcome = Outcomes.Succeeds(await Processes(folder).RunAsync(new ProcessRequest("git", ["init", "--quiet", folder.Path]), Cancellation));
        Assert.True(outcome.Succeeded, outcome.Error);

        Assert.Equal(RepositoryState.NoCommit, await bench.Bench.Repositories.InspectAsync(folder.Path, Cancellation));
    }

    private static async Task<string> BareAsync(TemporaryFolder forge, CancellationToken cancellationToken)
    {
        var remote = Path.Combine(forge.Path, "shop.git");
        var outcome = Outcomes.Succeeds(await Processes(forge).RunAsync(new ProcessRequest("git", ["init", "--quiet", "--bare", "--initial-branch=main", remote]), cancellationToken));
        Assert.True(outcome.Succeeded, outcome.Error);

        return remote;
    }

    private static IProcessRunner Processes(TemporaryFolder data) =>
        new ServiceCollection().AddRuntime(new AvalaPaths(data.Path)).BuildServiceProvider().GetRequiredService<IProcessRunner>();
}
