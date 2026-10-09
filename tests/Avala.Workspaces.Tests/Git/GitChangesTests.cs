using Avala.Runtime;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Workspaces.Changes;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Git;
using Avala.Workspaces.Provisioning;
using Avala.Workspaces.Tests.Provisioning;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Workspaces.Tests.Git;

public sealed class GitChangesTests
{
    private const string Message = "Add GitHub login\n\nAvala-Job: 1";

    private static readonly IProcessRunner Processes =
        new ServiceCollection().AddRuntime(new AvalaPaths(Path.GetTempPath())).BuildServiceProvider().GetRequiredService<IProcessRunner>();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AWorkspaceRemembersTheBranchItsBaseWasCheckedOutOnAndADetachedBaseHasNoneAsync()
    {
        await using var job = await Job.PreparedAsync();
        await job.Repository.GitAsync(Cancellation, "checkout", "--quiet", "--detach");

        var detached = Outcomes.Succeeds(await job.Service.PrepareAsync(new WorkspaceRequest(job.Repository.Path), Cancellation));

        Assert.Equal(Option<string>.Some("main"), job.Workspace.BaseBranch);
        Assert.Equal(Option<string>.None, detached.BaseBranch);
        Assert.Equal(WorkspaceFailure.NoBaseBranch, Outcomes.FailsWith(await job.Changes.MergeAsync(detached.Id, Message, Cancellation)));
    }

    [Fact]
    public async Task TheDiffListsEveryFileTheCheckpointsChangedAgainstTheBaseCommitWithItsCountsAsync()
    {
        await using var job = await Job.PreparedAsync(("src/cart.cs", "one\ntwo\nthree\n"), ("old.txt", "gone\n"));
        await job.WriteAsync("src/cart.cs", "one\n2\nthree\nfour\n");
        await job.WriteAsync("login.cs", "class Login;\n");
        await File.WriteAllBytesAsync(Path.Combine(job.Workspace.Path, "logo.png"), [0, 1, 2, 0, 255], Cancellation);
        File.Delete(Path.Combine(job.Workspace.Path, "old.txt"));
        await job.CheckpointAsync();
        await job.Repository.CommitAsync("later.txt", "after the job started\n", Cancellation);

        var diff = Outcomes.Succeeds(await job.Changes.DiffAsync(job.Workspace.Id, Cancellation));

        Assert.Equal((job.Workspace.BaseCommit, await job.HeadAsync()), (diff.BaseCommit, diff.Head));
        Assert.Equal(
            [
                new FileChange("login.cs", ChangeKind.Added, 1, 0),
                new FileChange("logo.png", ChangeKind.Added, Option<int>.None, Option<int>.None),
                new FileChange("old.txt", ChangeKind.Deleted, 0, 1),
                new FileChange("src/cart.cs", ChangeKind.Modified, 2, 1),
            ],
            diff.Files);
    }

    [Fact]
    public async Task TheHunksOfAChangedFileComeOnDemandAndAnUnchangedFileHasNoneAsync()
    {
        await using var job = await Job.PreparedAsync(("src/cart.cs", "one\ntwo\nthree\n"));
        await job.WriteAsync("src/cart.cs", "one\n2\nthree\n");
        await File.WriteAllBytesAsync(Path.Combine(job.Workspace.Path, "logo.png"), [0, 1, 2, 0, 255], Cancellation);
        await job.CheckpointAsync();

        var file = Outcomes.Succeeds(await job.Changes.FileDiffAsync(job.Workspace.Id, "src/cart.cs", Cancellation));

        var hunk = Assert.Single(file.Hunks);
        Assert.Equal((1, 3, 1, 3), (hunk.OldStart, hunk.OldLines, hunk.NewStart, hunk.NewLines));
        Assert.Equal(
            [
                new DiffLine(DiffLineKind.Context, "one"),
                new DiffLine(DiffLineKind.Removed, "two"),
                new DiffLine(DiffLineKind.Added, "2"),
                new DiffLine(DiffLineKind.Context, "three"),
            ],
            hunk.Lines);
        Assert.False(file.Binary);
        Assert.True(Outcomes.Succeeds(await job.Changes.FileDiffAsync(job.Workspace.Id, "logo.png", Cancellation)).Binary);
        Assert.Equal(WorkspaceFailure.FileUnchanged, Outcomes.FailsWith(await job.Changes.FileDiffAsync(job.Workspace.Id, "README.md", Cancellation)));
        Assert.Equal(WorkspaceFailure.UnknownWorkspace, Outcomes.FailsWith(await job.Changes.DiffAsync(WorkspaceId.New(), Cancellation)));
    }

    [Fact]
    public async Task MergingSquashesTheCheckpointsIntoOneCommitOnTheBaseBranchAndUpdatesItsCleanCheckoutAsync()
    {
        await using var job = await Job.PreparedAsync();
        await job.WriteAsync("login.cs", "class Login;\n");
        await job.CheckpointAsync();
        await job.WriteAsync("login.cs", "class Login { }\n");
        await job.CheckpointAsync();
        var tip = await job.Repository.GitAsync(Cancellation, "rev-parse", "main");
        var head = await job.HeadAsync();

        var merged = Outcomes.Succeeds(await job.Changes.MergeAsync(job.Workspace.Id, Message, Cancellation));

        var commit = Outcomes.Present(merged.Commit);
        Assert.Equal(("main", Option<string>.Some(job.Repository.Path)), (merged.BaseBranch, merged.Checkout.Map(Path.GetFullPath)));
        Assert.Equal(commit, await job.Repository.GitAsync(Cancellation, "rev-parse", "main"));
        Assert.Equal(tip, await job.Repository.GitAsync(Cancellation, "rev-parse", $"{commit}^"));
        Assert.Equal(Message, await job.Repository.GitAsync(Cancellation, "log", "-1", "--format=%B", commit));
        Assert.Equal(await job.Repository.GitAsync(Cancellation, "rev-parse", $"{head}^{{tree}}"), await job.Repository.GitAsync(Cancellation, "rev-parse", $"{commit}^{{tree}}"));
        Assert.Equal("class Login { }\n", (await File.ReadAllTextAsync(Path.Combine(job.Repository.Path, "login.cs"), Cancellation)).ReplaceLineEndings("\n"));
        Assert.Empty(await job.Repository.GitAsync(Cancellation, "status", "--porcelain"));
        Assert.Equal(head, await job.HeadAsync());
    }

    [Fact]
    public async Task AWorkspaceStartedFromAnotherWorkspacesBranchMergesIntoThatBranchAndUpdatesItsWorktreeAsync()
    {
        await using var job = await Job.PreparedAsync();
        await job.WriteAsync("PLAN.md", "the plan\n");
        await job.CheckpointAsync();
        var parentTip = await job.HeadAsync();
        var child = Outcomes.Succeeds(await job.Service.PrepareAsync(
            new WorkspaceRequest(job.Repository.Path, job.Workspace.Branch) { Rules = job.Workspace.RulesCommit },
            Cancellation));
        await File.WriteAllTextAsync(Path.Combine(child.Path, "NOTES.md"), "the notes\n", Cancellation);
        Outcomes.Succeeds(await job.Service.CheckpointAsync(child.Id, "Attempt 1", Cancellation));
        var main = await job.Repository.GitAsync(Cancellation, "rev-parse", "main");

        var merged = Outcomes.Succeeds(await job.Changes.MergeAsync(child.Id, Message, Cancellation));

        var commit = Outcomes.Present(merged.Commit);
        Assert.Equal((job.Workspace.Branch, Option<string>.Some(Path.GetFullPath(job.Workspace.Path))), (merged.BaseBranch, merged.Checkout.Map(Path.GetFullPath)));
        Assert.Equal((commit, parentTip), (await job.HeadAsync(), await job.Repository.GitAsync(Cancellation, "rev-parse", $"{commit}^")));
        Assert.Equal("the notes\n", (await File.ReadAllTextAsync(Path.Combine(job.Workspace.Path, "NOTES.md"), Cancellation)).ReplaceLineEndings("\n"));
        Assert.Equal(main, await job.Repository.GitAsync(Cancellation, "rev-parse", "main"));
    }

    [Fact]
    public async Task MergingOntoABaseThatMovedCleanlyAppliesTheJobsChangesOnTopAsync()
    {
        await using var job = await Job.PreparedAsync(("src/cart.cs", "one\ntwo\nthree\n"));
        await job.WriteAsync("src/cart.cs", "one\ntwo\nthree\nfour\n");
        await job.CheckpointAsync();
        await job.Repository.CommitAsync("docs/notes.md", "moved on\n", Cancellation);
        var tip = await job.Repository.GitAsync(Cancellation, "rev-parse", "main");

        var commit = Outcomes.Present(Outcomes.Succeeds(await job.Changes.MergeAsync(job.Workspace.Id, Message, Cancellation)).Commit);

        Assert.Equal(tip, await job.Repository.GitAsync(Cancellation, "rev-parse", $"{commit}^"));
        Assert.Equal("one\ntwo\nthree\nfour\n", (await File.ReadAllTextAsync(Path.Combine(job.Repository.Path, "src/cart.cs"), Cancellation)).ReplaceLineEndings("\n"));
        Assert.True(File.Exists(Path.Combine(job.Repository.Path, "docs/notes.md")));
    }

    [Fact]
    public async Task ABaseThatMovedIntoAConflictIsReportedWithItsFilesAndKeepsItsBranchAsync()
    {
        await using var job = await Job.PreparedAsync(("src/cart.cs", "one\ntwo\nthree\n"));
        await job.WriteAsync("src/cart.cs", "one\nTWO\nthree\n");
        await job.CheckpointAsync();
        await job.Repository.CommitAsync("src/cart.cs", "one\ndeux\nthree\n", Cancellation);
        var tip = await job.Repository.GitAsync(Cancellation, "rev-parse", "main");

        Assert.Equal(WorkspaceFailure.MergeConflict, Outcomes.FailsWith(await job.Changes.MergeAsync(job.Workspace.Id, Message, Cancellation)));

        Assert.Equal(["src/cart.cs"], Outcomes.Succeeds(await job.Changes.ConflictsAsync(job.Workspace.Id, Cancellation)));
        Assert.Equal(tip, await job.Repository.GitAsync(Cancellation, "rev-parse", "main"));
        Assert.Empty(await job.Repository.GitAsync(Cancellation, "status", "--porcelain"));
    }

    [Fact]
    public async Task ABaseCheckoutWithUncommittedChangesIsNeverTouchedAsync()
    {
        await using var job = await Job.PreparedAsync(("src/cart.cs", "one\n"));
        await job.WriteAsync("login.cs", "class Login;\n");
        await job.CheckpointAsync();
        var tip = await job.Repository.GitAsync(Cancellation, "rev-parse", "main");
        await File.WriteAllTextAsync(Path.Combine(job.Repository.Path, "src/cart.cs"), "work in progress\n", Cancellation);

        Assert.Equal(WorkspaceFailure.BaseCheckoutDirty, Outcomes.FailsWith(await job.Changes.MergeAsync(job.Workspace.Id, Message, Cancellation)));

        Assert.Equal(tip, await job.Repository.GitAsync(Cancellation, "rev-parse", "main"));
        Assert.Equal("work in progress\n", await File.ReadAllTextAsync(Path.Combine(job.Repository.Path, "src/cart.cs"), Cancellation));
        Assert.False(File.Exists(Path.Combine(job.Repository.Path, "login.cs")));
    }

    [Fact]
    public async Task AnUntrackedFileTheMergeWouldOverwriteMovesTheBaseBranchBackAndStaysAsItWasAsync()
    {
        await using var job = await Job.PreparedAsync();
        await job.WriteAsync("login.cs", "class Login;\n");
        await job.CheckpointAsync();
        var tip = await job.Repository.GitAsync(Cancellation, "rev-parse", "main");
        await File.WriteAllTextAsync(Path.Combine(job.Repository.Path, "login.cs"), "mine\n", Cancellation);

        Assert.Equal(WorkspaceFailure.BaseCheckoutDirty, Outcomes.FailsWith(await job.Changes.MergeAsync(job.Workspace.Id, Message, Cancellation)));

        Assert.Equal(tip, await job.Repository.GitAsync(Cancellation, "rev-parse", "main"));
        Assert.Equal("mine\n", await File.ReadAllTextAsync(Path.Combine(job.Repository.Path, "login.cs"), Cancellation));
    }

    [Fact]
    public async Task ABaseBranchCheckedOutNowhereOnlyMovesItsRefAsync()
    {
        await using var job = await Job.PreparedAsync();
        await job.WriteAsync("login.cs", "class Login;\n");
        await job.CheckpointAsync();
        await job.Repository.GitAsync(Cancellation, "checkout", "--quiet", "-b", "elsewhere");

        var merged = Outcomes.Succeeds(await job.Changes.MergeAsync(job.Workspace.Id, Message, Cancellation));

        Assert.Equal(Option<string>.None, merged.Checkout);
        Assert.Equal(Outcomes.Present(merged.Commit), await job.Repository.GitAsync(Cancellation, "rev-parse", "main"));
        Assert.False(File.Exists(Path.Combine(job.Repository.Path, "login.cs")));
    }

    [Fact]
    public async Task WorkTheBaseAlreadyHoldsMergesWithoutANewCommitAsync()
    {
        await using var job = await Job.PreparedAsync();
        await job.CheckpointAsync();
        var tip = await job.Repository.GitAsync(Cancellation, "rev-parse", "main");

        var merged = Outcomes.Succeeds(await job.Changes.MergeAsync(job.Workspace.Id, Message, Cancellation));

        Assert.Equal(Option<string>.None, merged.Commit);
        Assert.Equal(tip, await job.Repository.GitAsync(Cancellation, "rev-parse", "main"));
    }

    private sealed class Job(TemporaryRepository repository, WorkspaceService service, WorkspaceChanges changes, WorkspaceInfo workspace) : IAsyncDisposable
    {
        public TemporaryRepository Repository { get; } = repository;

        public WorkspaceService Service { get; } = service;

        public WorkspaceChanges Changes { get; } = changes;

        public WorkspaceInfo Workspace { get; } = workspace;

        public static async Task<Job> PreparedAsync(params (string Path, string Content)[] committed)
        {
            var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);

            foreach (var (path, content) in committed)
            {
                await repository.CommitAsync(path, content, Cancellation);
            }

            var store = new InMemoryWorkspaceStore();
            var git = new GitCli(Processes);
            var settings = new WorkspaceSettings(repository.WorktreeRoot);
            var service = new WorkspaceService(git, store, settings, new WorktreeReconciler(git, store, settings));
            var workspace = Outcomes.Succeeds(await service.PrepareAsync(new WorkspaceRequest(repository.Path), Cancellation));

            return new Job(repository, service, new WorkspaceChanges(store, new GitChangesCli(Processes)), workspace);
        }

        public async Task WriteAsync(string path, string content)
        {
            var file = Path.Combine(Workspace.Path, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, content, Cancellation);
        }

        public async Task CheckpointAsync() => Outcomes.Succeeds(await Service.CheckpointAsync(Workspace.Id, "Attempt", Cancellation));

        public Task<string> HeadAsync() => Repository.GitAsync(Cancellation, "rev-parse", Workspace.Branch);

        public ValueTask DisposeAsync() => Repository.DisposeAsync();
    }
}
