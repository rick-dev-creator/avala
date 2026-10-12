using Avala.Runtime;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Git;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Workspaces.Tests.Git;

public sealed class GitRepositoriesTests
{
    private static readonly IProcessRunner Processes =
        new ServiceCollection().AddRuntime(new AvalaPaths(Path.GetTempPath())).BuildServiceProvider().GetRequiredService<IProcessRunner>();

    private static readonly string[] Identity = ["user.name=Ada", "user.email=ada@localhost", "commit.gpgsign=false", "init.defaultBranch=trunk"];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnEmptyFolderBecomesARepositoryWithOneEmptyCommitOnTheConfiguredDefaultBranchAsync()
    {
        await using var scratch = new TemporaryFolder();

        var made = Outcomes.Succeeds(await Repositories(Identity).InitializeAsync(scratch.Path, Cancellation));

        Assert.Equal(Path.GetFullPath(scratch.Path), made);
        Assert.Equal(("1", "trunk", string.Empty), (await GitAsync(made, "rev-list", "--count", "HEAD"), await GitAsync(made, "branch", "--show-current"), await GitAsync(made, "ls-tree", "-r", "--name-only", "HEAD")));
    }

    [Fact]
    public async Task AFolderWithFilesBecomesARepositoryWhoseFirstCommitHoldsThemAsync()
    {
        await using var scratch = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(scratch.Path, "notes.md"), "# Notes\n", Cancellation);

        var made = Outcomes.Succeeds(await Repositories(Identity).InitializeAsync(scratch.Path, Cancellation));

        Assert.Equal(("notes.md", string.Empty), (await GitAsync(made, "ls-tree", "-r", "--name-only", "HEAD"), await GitAsync(made, "status", "--porcelain")));
    }

    [Fact]
    public async Task ARepositoryWithoutACommitGetsItsFirstCommitAsync()
    {
        await using var scratch = new TemporaryFolder();
        await GitAsync(scratch.Path, "init", "--quiet");

        Outcomes.Succeeds(await Repositories(Identity).InitializeAsync(scratch.Path, Cancellation));

        Assert.Equal("1", await GitAsync(scratch.Path, "rev-list", "--count", "HEAD"));
    }

    [Fact]
    public async Task ARepositoryWithHistoryIsLeftAloneAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var head = await repository.GitAsync(Cancellation, "rev-parse", "HEAD");

        var failure = Outcomes.FailsWith(await Repositories(Identity).InitializeAsync(repository.Path, Cancellation));

        Assert.Equal((WorkspaceFailure.AlreadyARepository, head), (failure, await repository.GitAsync(Cancellation, "rev-parse", "HEAD")));
    }

    [Fact]
    public async Task WithoutAGitIdentityTheFolderIsNotTouchedAndTheMissingIdentityIsReportedAsync()
    {
        await using var scratch = new TemporaryFolder();

        var failure = Outcomes.FailsWith(await Repositories("user.name=", "user.email=").InitializeAsync(scratch.Path, Cancellation));

        Assert.Equal((WorkspaceFailure.NoGitIdentity, false), (failure, Directory.Exists(Path.Combine(scratch.Path, ".git"))));
    }

    [Fact]
    public async Task ANewRepositoryIsAFolderNamedUnderItsParentWithOneEmptyCommitAsync()
    {
        await using var scratch = new TemporaryFolder();

        var made = Outcomes.Succeeds(await Repositories(Identity).CreateAsync(scratch.Path, " shop ", Cancellation));

        Assert.Equal(Path.Combine(Path.GetFullPath(scratch.Path), "shop"), made);
        Assert.Equal(("1", "trunk"), (await GitAsync(made, "rev-list", "--count", "HEAD"), await GitAsync(made, "branch", "--show-current")));
    }

    [Theory]
    [InlineData("", WorkspaceFailure.InvalidRepositoryName)]
    [InlineData("..", WorkspaceFailure.InvalidRepositoryName)]
    [InlineData("code/shop", WorkspaceFailure.InvalidRepositoryName)]
    [InlineData("taken", WorkspaceFailure.FolderExists)]
    public async Task ANewRepositoryNeedsAPlainNameThatIsNotTakenAsync(string name, WorkspaceFailure expected)
    {
        await using var scratch = new TemporaryFolder();
        Directory.CreateDirectory(Path.Combine(scratch.Path, "taken"));

        Assert.Equal(expected, Outcomes.FailsWith(await Repositories(Identity).CreateAsync(scratch.Path, name, Cancellation)));
    }

    [Fact]
    public async Task AFolderIsReadAsMissingNotARepositoryWithoutACommitOrReadyAsync()
    {
        await using var scratch = new TemporaryFolder();
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var empty = Directory.CreateDirectory(Path.Combine(scratch.Path, "empty")).FullName;
        var plain = Directory.CreateDirectory(Path.Combine(scratch.Path, "plain")).FullName;
        await GitAsync(empty, "init", "--quiet");
        var repositories = Repositories(Identity);

        Assert.Equal(
            [RepositoryState.Missing, RepositoryState.NotARepository, RepositoryState.NoCommit, RepositoryState.Ready],
            [
                await repositories.InspectAsync(Path.Combine(scratch.Path, "absent"), Cancellation),
                await repositories.InspectAsync(plain, Cancellation),
                await repositories.InspectAsync(empty, Cancellation),
                await repositories.InspectAsync(repository.Path, Cancellation),
            ]);
    }

    private static GitRepositories Repositories(params string[] settings) => new(new ConfiguredGit(Processes, settings));

    private static async Task<string> GitAsync(string folder, params string[] arguments) =>
        (await Processes.RunAsync(new ProcessRequest("git", ["-C", folder, .. arguments]), Cancellation)).Match(outcome => outcome.Output.Trim(), error => error.ToString());

    private sealed class ConfiguredGit(IProcessRunner inner, IReadOnlyList<string> settings) : IProcessRunner
    {
        public ValueTask<Result<ProcessOutcome, ProcessError>> RunAsync(ProcessRequest request, CancellationToken cancellationToken) =>
            inner.RunAsync(request with { Arguments = [.. settings.SelectMany(setting => new[] { "-c", setting }), .. request.Arguments] }, cancellationToken);
    }
}
