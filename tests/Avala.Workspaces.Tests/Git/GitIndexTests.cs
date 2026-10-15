using Avala.Runtime;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Workspaces.Git;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Workspaces.Tests.Git;

public sealed class GitIndexTests
{
    private static readonly IProcessRunner Processes =
        new ServiceCollection().AddRuntime(new AvalaPaths(Path.GetTempPath())).BuildServiceProvider().GetRequiredService<IProcessRunner>();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheIndexListsTrackedAndNewFilesButNotIgnoredOnesAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        await repository.CommitAsync(".gitignore", "bin/\n", Cancellation);
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "Draft.cs"), "class Draft;", Cancellation);
        Directory.CreateDirectory(Path.Combine(repository.Path, "bin"));
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "bin", "Shop.dll"), "built", Cancellation);

        var files = await new GitIndex(Processes).FilesAsync(repository.Path, 10, Cancellation);

        Assert.Equal([".gitignore", "Draft.cs", "README.md"], files.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheIndexListsTheLatestCommitsNewestFirstWithTheirSubjectsAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        await repository.CommitAsync("src/Rounding.cs", "class Rounding;", Cancellation);
        await repository.CommitAsync("src/Prices.cs", "class Prices;", Cancellation);

        var commits = await new GitIndex(Processes).CommitsAsync(repository.Path, 2, Cancellation);

        Assert.Equal(["Add src/Prices.cs", "Add src/Rounding.cs"], commits.Select(commit => commit.Subject));
        Assert.Equal(await repository.GitAsync(Cancellation, "rev-parse", "--short", "HEAD"), commits[0].Sha);
    }

    [Fact]
    public async Task AFolderThatIsNotARepositoryHasNothingToListAsync()
    {
        await using var scratch = new TemporaryFolder();

        var index = new GitIndex(Processes);

        Assert.Empty(await index.FilesAsync(scratch.Path, 10, Cancellation));
        Assert.Empty(await index.CommitsAsync(scratch.Path, 10, Cancellation));
    }
}
