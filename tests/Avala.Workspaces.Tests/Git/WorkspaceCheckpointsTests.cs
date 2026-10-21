using Avala.Runtime;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Workspaces.Changes;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Git;
using Avala.Workspaces.Provisioning;
using Avala.Workspaces.Storage;
using Avala.Workspaces.Tests.Provisioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avala.Workspaces.Tests.Git;

public sealed class WorkspaceCheckpointsTests
{
    private static readonly IProcessRunner Processes =
        new ServiceCollection().AddRuntime(new AvalaPaths(Path.GetTempPath())).BuildServiceProvider().GetRequiredService<IProcessRunner>();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EachCheckpointListsTheFilesItChangedSinceTheOneBeforeAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var (service, checkpoints) = Open(repository);
        var workspace = Outcomes.Succeeds(await service.PrepareAsync(new WorkspaceRequest(repository.Path), Cancellation));
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "minor.go"), "one", Cancellation);
        _ = Outcomes.Succeeds(await service.CheckpointAsync(workspace.Id, "Attempt 1", Cancellation));
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "total.go"), "two", Cancellation);
        _ = Outcomes.Succeeds(await service.CheckpointAsync(workspace.Id, "Attempt 2", Cancellation));

        var listed = Outcomes.Succeeds(await checkpoints.ListAsync(workspace.Id, Cancellation));

        Assert.Equal(["Attempt 1: minor.go", "Attempt 2: total.go"], listed.Select(checkpoint => $"{checkpoint.Label}: {string.Join(", ", checkpoint.Files.Select(file => file.Path))}"));
    }

    [Fact]
    public async Task RestoringReturnsTheWorktreeToACheckpointKeepsWhatCameAfterAndLeavesIgnoredFilesAloneAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        await repository.CommitAsync(".gitignore", "build/\n", Cancellation);
        var (service, checkpoints) = Open(repository);
        var workspace = Outcomes.Succeeds(await service.PrepareAsync(new WorkspaceRequest(repository.Path), Cancellation));
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "minor.go"), "first", Cancellation);
        var first = Outcomes.Succeeds(await service.CheckpointAsync(workspace.Id, "Attempt 1", Cancellation));
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "minor.go"), "second", Cancellation);
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "later.go"), "later", Cancellation);
        Directory.CreateDirectory(Path.Combine(workspace.Path, "build"));
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "build", "out.bin"), "cache", Cancellation);

        var restored = Outcomes.Succeeds(await checkpoints.RestoreAsync(workspace.Id, first.Number, Cancellation));

        Assert.Equal("first", await File.ReadAllTextAsync(Path.Combine(workspace.Path, "minor.go"), Cancellation));
        Assert.False(File.Exists(Path.Combine(workspace.Path, "later.go")));
        Assert.True(File.Exists(Path.Combine(workspace.Path, "build", "out.bin")));
        Assert.Equal(["later.go", "minor.go"], restored.Files.Select(file => file.Path).Order(StringComparer.Ordinal));
        var listed = Outcomes.Succeeds(await checkpoints.ListAsync(workspace.Id, Cancellation));
        var before = listed.Single(checkpoint => checkpoint.Label == "Before restoring to Attempt 1");

        _ = Outcomes.Succeeds(await checkpoints.RestoreAsync(workspace.Id, before.Number, Cancellation));

        Assert.Equal("second", await File.ReadAllTextAsync(Path.Combine(workspace.Path, "minor.go"), Cancellation));
        Assert.True(File.Exists(Path.Combine(workspace.Path, "later.go")));
    }

    [Fact]
    public async Task RestoringACheckpointTheWorkspaceNeverTookIsRefusedAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var (service, checkpoints) = Open(repository);
        var workspace = Outcomes.Succeeds(await service.PrepareAsync(new WorkspaceRequest(repository.Path), Cancellation));

        Assert.Equal(WorkspaceFailure.UnknownCheckpoint, Outcomes.FailsWith(await checkpoints.RestoreAsync(workspace.Id, 7, Cancellation)));
    }

    [Fact]
    public async Task ListingCheckpointsWhileAnotherCheckpointIsRecordedOnTheSameWorkspaceSucceedsAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        using var data = new TemporaryFolder();
        await using var store = new SqliteWorkspaceStore(new AvalaPaths(data.Path));
        var git = new GitCli(Processes, NullLogger<GitCli>.Instance);
        var settings = new WorkspaceSettings(repository.WorktreeRoot);
        var service = new WorkspaceService(git, store, settings, new WorktreeReconciler(git, store, settings));
        var changes = new PausingChanges(new GitChangesCli(Processes));
        var checkpoints = new WorkspaceCheckpoints(store, service, changes, git);
        var workspace = Outcomes.Succeeds(await service.PrepareAsync(new WorkspaceRequest(repository.Path), Cancellation));
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "minor.go"), "one", Cancellation);
        _ = Outcomes.Succeeds(await service.CheckpointAsync(workspace.Id, "Attempt 1", Cancellation));
        var listing = checkpoints.ListAsync(workspace.Id, Cancellation).AsTask();
        await changes.Entered.WaitAsync(Cancellation);
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "docs.md"), "version 1", Cancellation);
        _ = Outcomes.Succeeds(await service.CheckpointAsync(workspace.Id, "Document Greeting, version 1", Cancellation));

        changes.Release();

        var thrown = await Record.ExceptionAsync(() => listing);
        Assert.Null(thrown);
        Assert.Equal("Attempt 1", Outcomes.Succeeds(await listing)[0].Label);
    }

    private static (WorkspaceService Service, WorkspaceCheckpoints Checkpoints) Open(TemporaryRepository repository)
    {
        var git = new GitCli(Processes, NullLogger<GitCli>.Instance);
        var store = new InMemoryWorkspaceStore();
        var settings = new WorkspaceSettings(repository.WorktreeRoot);
        var service = new WorkspaceService(git, store, settings, new WorktreeReconciler(git, store, settings));

        return (service, new WorkspaceCheckpoints(store, service, new GitChangesCli(Processes), git));
    }
}
