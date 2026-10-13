using Avala.Runtime;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Git;
using Avala.Workspaces.WorkingFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

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
        var store = new WorkingFileStore(new GitCli(Processes, NullLogger<GitCli>.Instance));
        var absent = Outcomes.Succeeds(await store.ReadAsync(repository.Path, Budget, Cancellation));

        var written = Outcomes.Succeeds(await store.WriteAsync(repository.Path, Budget, "{ \"holdAtLimit\": 0.9 }", Cancellation));

        Assert.False(absent.IsSome);
        Assert.Equal(Path.Combine(repository.Path, Budget).Canonical(), Path.GetFullPath(written));
        Assert.Equal("{ \"holdAtLimit\": 0.9 }", Outcomes.Present(Outcomes.Succeeds(await store.ReadAsync(repository.Path, Budget, Cancellation))));
        Assert.Contains(Budget, await repository.GitAsync(Cancellation, "status", "--porcelain", "--untracked-files=all"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../outside.json")]
    [InlineData(".avala/../../outside.json")]
    public async Task APathThatLeavesTheRepositoryIsRefusedAsync(string path)
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var store = new WorkingFileStore(new GitCli(Processes, NullLogger<GitCli>.Instance));

        Assert.Equal(WorkspaceFailure.OutsideRepository, Outcomes.FailsWith(await store.WriteAsync(repository.Path, path, "{}", Cancellation)));
        Assert.Equal(WorkspaceFailure.OutsideRepository, Outcomes.FailsWith(await store.ReadAsync(repository.Path, path, Cancellation)));
    }

    [Fact]
    public async Task ContentOverTheSizeLimitIsNeverWrittenAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var store = new WorkingFileStore(new GitCli(Processes, NullLogger<GitCli>.Instance));

        Assert.Equal(WorkspaceFailure.FileTooLarge, Outcomes.FailsWith(await store.WriteAsync(repository.Path, Budget, new string('x', WorkingFileStore.MaximumBytes + 1), Cancellation)));
        Assert.False(File.Exists(Path.Combine(repository.Path, Budget)));
    }

    [Fact]
    public async Task AFileIsLocatedAndReadAsBytesOnlyInsideTheRepositoryAndUnderTheLimitAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var store = new WorkingFileStore(new GitCli(Processes, NullLogger<GitCli>.Instance));
        await File.WriteAllBytesAsync(Path.Combine(repository.Path, "orbit.png"), [137, 80, 78, 71], Cancellation);

        var located = Outcomes.Present(Outcomes.Succeeds(await store.LocateAsync(repository.Path, "orbit.png", Cancellation)));
        var bytes = Outcomes.Present(Outcomes.Succeeds(await store.ReadBytesAsync(repository.Path, "orbit.png", 4, Cancellation)));

        Assert.Equal(Path.Combine(repository.Path, "orbit.png").Canonical(), Path.GetFullPath(located));
        Assert.Equal([137, 80, 78, 71], bytes);
        Assert.False(Outcomes.Succeeds(await store.LocateAsync(repository.Path, "missing.png", Cancellation)).IsSome);
        Assert.Equal(WorkspaceFailure.FileTooLarge, Outcomes.FailsWith(await store.ReadBytesAsync(repository.Path, "orbit.png", 3, Cancellation)));
        Assert.Equal(WorkspaceFailure.OutsideRepository, Outcomes.FailsWith(await store.ReadBytesAsync(repository.Path, "../orbit.png", 4, Cancellation)));
    }

    [Fact]
    public async Task AFolderThatIsNotARepositoryIsRefusedAsync()
    {
        using var folder = new TemporaryFolder();

        Assert.Equal(WorkspaceFailure.NotAGitRepository, Outcomes.FailsWith(await new WorkingFileStore(new GitCli(Processes, NullLogger<GitCli>.Instance)).ReadAsync(folder.Path, Budget, Cancellation)));
    }
}
