using Avala.Runtime;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Git;
using Avala.Workspaces.WorkingFiles;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Workspaces.Tests.WorkingFiles;

public sealed class WorkingFileStoreTests
{
    private const string Budget = ".avala/budget.json";

    private static readonly IProcessRunner Processes =
        new ServiceCollection().AddRuntime(new AvalaPaths(Path.GetTempPath())).BuildServiceProvider().GetRequiredService<IProcessRunner>();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AFileIsWrittenInTheCheckoutLeftUncommittedAndReadBackAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var store = new WorkingFileStore(new GitCli(Processes));
        var absent = Outcomes.Succeeds(await store.ReadAsync(repository.Path, Budget, Cancellation));

        var written = Outcomes.Succeeds(await store.WriteAsync(repository.Path, Budget, "{ \"holdAtLimit\": 0.9 }", Cancellation));

        Assert.False(absent.IsSome);
        Assert.Equal(Path.GetFullPath(Path.Combine(repository.Path, Budget)), Path.GetFullPath(written));
        Assert.Equal("{ \"holdAtLimit\": 0.9 }", Outcomes.Present(Outcomes.Succeeds(await store.ReadAsync(repository.Path, Budget, Cancellation))));
        Assert.Contains(Budget, await repository.GitAsync(Cancellation, "status", "--porcelain", "--untracked-files=all"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../outside.json")]
    [InlineData(".avala/../../outside.json")]
    public async Task APathThatLeavesTheRepositoryIsRefusedAsync(string path)
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var store = new WorkingFileStore(new GitCli(Processes));

        Assert.Equal(WorkspaceFailure.OutsideRepository, Outcomes.FailsWith(await store.WriteAsync(repository.Path, path, "{}", Cancellation)));
        Assert.Equal(WorkspaceFailure.OutsideRepository, Outcomes.FailsWith(await store.ReadAsync(repository.Path, path, Cancellation)));
    }

    [Fact]
    public async Task ContentOverTheSizeLimitIsNeverWrittenAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var store = new WorkingFileStore(new GitCli(Processes));

        Assert.Equal(WorkspaceFailure.FileTooLarge, Outcomes.FailsWith(await store.WriteAsync(repository.Path, Budget, new string('x', WorkingFileStore.MaximumBytes + 1), Cancellation)));
        Assert.False(File.Exists(Path.Combine(repository.Path, Budget)));
    }

    [Fact]
    public async Task AFolderThatIsNotARepositoryIsRefusedAsync()
    {
        using var folder = new TemporaryFolder();

        Assert.Equal(WorkspaceFailure.NotAGitRepository, Outcomes.FailsWith(await new WorkingFileStore(new GitCli(Processes)).ReadAsync(folder.Path, Budget, Cancellation)));
    }
}
