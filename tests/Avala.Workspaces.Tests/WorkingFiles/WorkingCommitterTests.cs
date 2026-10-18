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

public sealed class WorkingCommitterTests
{
    private const string Jobs = ".avala/jobs.json";

    private static readonly IProcessRunner Processes =
        new ServiceCollection().AddRuntime(new AvalaPaths(Path.GetTempPath())).BuildServiceProvider().GetRequiredService<IProcessRunner>();

    private static readonly string[] Identity = ["user.name=Ada", "user.email=ada@localhost", "commit.gpgsign=false"];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OnlyThePathIsCommittedOnTheCheckedOutBranchAndWhatThePersonStagedStaysStagedAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        await WriteAsync(repository, Jobs, """{ "docs": { "enabled": true } }""");
        await WriteAsync(repository, "staged.txt", "staged\n");
        await repository.GitAsync(Cancellation, "add", "staged.txt");

        var committed = Outcomes.Succeeds(await Committer(Identity).CommitAsync(repository.Path, Jobs, "Turn on feature documentation", Cancellation));

        Assert.Equal((await repository.GitAsync(Cancellation, "rev-parse", "HEAD")).Trim(), committed.Commit);
        Assert.Equal("main", committed.Branch);
        Assert.Equal(["Turn on feature documentation", Jobs], (await repository.GitAsync(Cancellation, "show", "--name-only", "--format=%s", "HEAD")).Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal("staged.txt", (await repository.GitAsync(Cancellation, "diff", "--cached", "--name-only")).Trim());
    }

    [Fact]
    public async Task OnADetachedHeadNothingIsCommittedAndTheMissingBranchIsReportedAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        await repository.GitAsync(Cancellation, "checkout", "--quiet", "--detach");
        var head = await repository.GitAsync(Cancellation, "rev-parse", "HEAD");
        await WriteAsync(repository, Jobs, "{}");

        Assert.Equal(WorkspaceFailure.NoBaseBranch, Outcomes.FailsWith(await Committer(Identity).CommitAsync(repository.Path, Jobs, "Turn on feature documentation", Cancellation)));
        Assert.Equal(head, await repository.GitAsync(Cancellation, "rev-parse", "HEAD"));
    }

    [Fact]
    public async Task APathThatDidNotChangeCommitsNothingAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var head = await repository.GitAsync(Cancellation, "rev-parse", "HEAD");

        Assert.Equal(WorkspaceFailure.FileUnchanged, Outcomes.FailsWith(await Committer(Identity).CommitAsync(repository.Path, "README.md", "Nothing", Cancellation)));
        Assert.Equal(head, await repository.GitAsync(Cancellation, "rev-parse", "HEAD"));
    }

    [Fact]
    public async Task APathThatLeavesTheRepositoryIsRefusedAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);

        Assert.Equal(WorkspaceFailure.OutsideRepository, Outcomes.FailsWith(await Committer(Identity).CommitAsync(repository.Path, "../outside.json", "Outside", Cancellation)));
    }

    [Fact]
    public async Task WithoutAGitIdentityNothingIsCommittedAndTheMissingIdentityIsReportedAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var head = await repository.GitAsync(Cancellation, "rev-parse", "HEAD");
        await WriteAsync(repository, Jobs, "{}");

        Assert.Equal(WorkspaceFailure.NoGitIdentity, Outcomes.FailsWith(await Committer("user.name=", "user.email=").CommitAsync(repository.Path, Jobs, "Turn on feature documentation", Cancellation)));
        Assert.Equal(head, await repository.GitAsync(Cancellation, "rev-parse", "HEAD"));
    }

    private static WorkingCommitter Committer(params string[] settings)
    {
        var configured = new ConfiguredGit(Processes, settings);

        return new WorkingCommitter(new GitCli(configured, NullLogger<GitCli>.Instance), configured);
    }

    private static async Task WriteAsync(TemporaryRepository repository, string path, string content)
    {
        var file = Path.Combine(repository.Path, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, content, Cancellation);
    }

    private sealed class ConfiguredGit(IProcessRunner inner, IReadOnlyList<string> settings) : IProcessRunner
    {
        public ValueTask<Result<ProcessOutcome, ProcessError>> RunAsync(ProcessRequest request, CancellationToken cancellationToken) =>
            inner.RunAsync(request with { Arguments = [.. settings.SelectMany(setting => new[] { "-c", setting }), .. request.Arguments] }, cancellationToken);
    }
}
