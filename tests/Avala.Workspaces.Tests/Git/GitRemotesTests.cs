using Avala.Runtime;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Git;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Workspaces.Tests.Git;

public sealed class GitRemotesTests
{
    private static readonly IProcessRunner Processes =
        new ServiceCollection().AddRuntime(new AvalaPaths(Path.GetTempPath())).BuildServiceProvider().GetRequiredService<IProcessRunner>();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PublishingAddsTheRemoteAndPushesTheCurrentBranchWithItsUpstreamAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        await using var scratch = new TemporaryFolder();
        var remote = await BareAsync(scratch.Path, "shop.git");

        var branch = Outcomes.Succeeds(await new GitRemotes(Processes).PublishAsync(repository.Path, "origin", remote, Cancellation));

        Assert.Equal("main", branch);
        Assert.Equal(await repository.GitAsync(Cancellation, "rev-parse", "HEAD"), await GitAsync(remote, "rev-parse", "refs/heads/main"));
        Assert.Equal(("origin/main", remote), (await repository.GitAsync(Cancellation, "rev-parse", "--abbrev-ref", "main@{upstream}"), await repository.GitAsync(Cancellation, "remote", "get-url", "origin")));
    }

    [Fact]
    public async Task PublishingAgainToTheSameRemoteOnlyPushesAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        await using var scratch = new TemporaryFolder();
        var remote = await BareAsync(scratch.Path, "shop.git");
        await repository.GitAsync(Cancellation, "remote", "add", "origin", remote);

        Outcomes.Succeeds(await new GitRemotes(Processes).PublishAsync(repository.Path, "origin", remote, Cancellation));

        Assert.Equal(await repository.GitAsync(Cancellation, "rev-parse", "HEAD"), await GitAsync(remote, "rev-parse", "refs/heads/main"));
    }

    [Fact]
    public async Task ARemoteThatPointsElsewhereIsLeftAloneAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        await using var scratch = new TemporaryFolder();
        var remote = await BareAsync(scratch.Path, "shop.git");
        await repository.GitAsync(Cancellation, "remote", "add", "origin", "git@example.com:octo/other.git");

        var failure = Outcomes.FailsWith(await new GitRemotes(Processes).PublishAsync(repository.Path, "origin", remote, Cancellation));

        Assert.Equal((WorkspaceFailure.RemoteExists, "git@example.com:octo/other.git"), (failure, await repository.GitAsync(Cancellation, "remote", "get-url", "origin")));
    }

    [Fact]
    public async Task ARepositoryWithoutACommitHasNoBranchToPublishAsync()
    {
        await using var scratch = new TemporaryFolder();
        await GitAsync(scratch.Path, "init", "--quiet");
        var remote = await BareAsync(scratch.Path, "shop.git");

        Assert.Equal(WorkspaceFailure.NoBaseBranch, Outcomes.FailsWith(await new GitRemotes(Processes).PublishAsync(scratch.Path, "origin", remote, Cancellation)));
    }

    [Fact]
    public async Task RemotesAreListedByNameWithTheirFetchUrlsAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        await repository.GitAsync(Cancellation, "remote", "add", "upstream", "https://forge.example/octo/shop.git");
        await repository.GitAsync(Cancellation, "remote", "add", "origin", "git@forge.example:rick/shop.git");
        await repository.GitAsync(Cancellation, "remote", "set-url", "--push", "origin", "git@forge.example:rick/push-only.git");

        var remotes = Outcomes.Succeeds(await new GitRemotes(Processes).RemotesAsync(repository.Path, Cancellation));

        Assert.Equal(
            [new OriginRemote("origin", "git@forge.example:rick/shop.git"), new OriginRemote("upstream", "https://forge.example/octo/shop.git")],
            remotes);
    }

    [Fact]
    public async Task TheRootCommitIsListedByItsHashAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var first = await repository.GitAsync(Cancellation, "rev-list", "--max-parents=0", "HEAD");
        await repository.CommitAsync("later.txt", "later\n", Cancellation);

        Assert.Equal([first], Outcomes.Succeeds(await new GitRemotes(Processes).RootCommitsAsync(repository.Path, Cancellation)));
    }

    [Fact]
    public async Task ARepositoryWithoutCommitsHasNoRootCommitAsync()
    {
        await using var scratch = new TemporaryFolder();
        await GitAsync(scratch.Path, "init", "--quiet");

        Assert.Empty(Outcomes.Succeeds(await new GitRemotes(Processes).RootCommitsAsync(scratch.Path, Cancellation)));
    }

    [Fact]
    public async Task AFolderOutsideAnyRepositoryIsAFailureNotARepositoryWithoutOriginsAsync()
    {
        await using var outside = new TemporaryFolder();
        var remotes = new GitRemotes(Processes);

        Assert.Equal(
            (WorkspaceFailure.GitFailed, WorkspaceFailure.GitFailed),
            (Outcomes.FailsWith(await remotes.RemotesAsync(outside.Path, Cancellation)), Outcomes.FailsWith(await remotes.RootCommitsAsync(outside.Path, Cancellation))));
    }

    [Theory]
    [InlineData("credential.helper=store", true)]
    [InlineData("credential.helper=", false)]
    public async Task AnHttpsRemoteIsUsableWhenGitHasACredentialHelperForItAsync(string setting, bool expected)
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);

        Assert.Equal(expected, await new GitRemotes(new ConfiguredGit(Processes, setting)).HasCredentialHelperAsync(repository.Path, "https://forge.example/octo/shop.git", Cancellation));
    }

    [Fact]
    public async Task CloningCopiesTheRepositoryIntoAFolderNamedUnderItsParentAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        await using var scratch = new TemporaryFolder();

        var cloned = Outcomes.Succeeds(await new GitRemotes(Processes).CloneAsync(repository.Path, scratch.Path, " shop ", Cancellation));

        Assert.Equal(Path.Combine(Path.GetFullPath(scratch.Path), "shop"), cloned);
        Assert.Equal(await repository.GitAsync(Cancellation, "rev-parse", "HEAD"), await GitAsync(cloned, "rev-parse", "HEAD"));
    }

    [Fact]
    public async Task CloningIntoSomethingThatExistsLeavesItUntouchedAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        await using var scratch = new TemporaryFolder();
        var taken = Directory.CreateDirectory(Path.Combine(scratch.Path, "shop")).FullName;
        await File.WriteAllTextAsync(Path.Combine(taken, "notes.md"), "mine", Cancellation);

        var failure = Outcomes.FailsWith(await new GitRemotes(Processes).CloneAsync(repository.Path, scratch.Path, "shop", Cancellation));

        Assert.Equal((WorkspaceFailure.FolderExists, "mine"), (failure, await File.ReadAllTextAsync(Path.Combine(taken, "notes.md"), Cancellation)));
    }

    [Fact]
    public async Task AFailedCloneRemovesTheFolderItMadeAndSaysWhyAsync()
    {
        await using var scratch = new TemporaryFolder();

        var failure = Outcomes.FailsWith(await new GitRemotes(Processes).CloneAsync(Path.Combine(scratch.Path, "absent.git"), scratch.Path, "shop", Cancellation));

        Assert.Equal((WorkspaceFailure.RemoteNotFound, false), (failure, Directory.Exists(Path.Combine(scratch.Path, "shop"))));
    }

    [Fact]
    public async Task ACancelledCloneRemovesOnlyTheFolderItMadeAndNeverFollowsALinkInItAsync()
    {
        await using var scratch = new TemporaryFolder();
        var outside = Directory.CreateDirectory(Path.Combine(scratch.Path, "outside")).FullName;
        await File.WriteAllTextAsync(Path.Combine(outside, "keep.md"), "keep", Cancellation);
        var interrupted = new InterruptedClone(outside);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await new GitRemotes(interrupted).CloneAsync("https://example.com/octo/shop.git", scratch.Path, "shop", Cancellation));

        Assert.True(interrupted.Linked, "the interrupted clone left no link to follow");
        Assert.Equal((false, "keep"), (Directory.Exists(Path.Combine(scratch.Path, "shop")), await File.ReadAllTextAsync(Path.Combine(outside, "keep.md"), Cancellation)));
    }

    [Theory]
    [InlineData("remote: Invalid username or password.\nfatal: Authentication failed for 'https://github.com/octo/shop.git/'", WorkspaceFailure.RemoteUnauthorized)]
    [InlineData("fatal: could not read Username for 'https://github.com': terminal prompts disabled", WorkspaceFailure.RemoteUnauthorized)]
    [InlineData("git@github.com: Permission denied (publickey).\nfatal: Could not read from remote repository.", WorkspaceFailure.RemoteUnauthorized)]
    [InlineData("remote: Repository not found.\nfatal: repository 'https://github.com/octo/absent.git/' not found", WorkspaceFailure.RemoteNotFound)]
    [InlineData("fatal: unable to access 'https://git.invalid/octo/shop.git/': Could not resolve host: git.invalid", WorkspaceFailure.RemoteUnreachable)]
    [InlineData(" ! [rejected]        main -> main (fetch first)\nerror: failed to push some refs to 'origin'", WorkspaceFailure.PushRejected)]
    [InlineData("fatal: something else went wrong", WorkspaceFailure.GitFailed)]
    public void GitsErrorOutputTellsWhyARemoteRefused(string error, WorkspaceFailure expected) =>
        Assert.Equal(expected, GitRemoteErrors.Classify(error));

    private static async Task<string> BareAsync(string parent, string name)
    {
        var path = Directory.CreateDirectory(Path.Combine(parent, name)).FullName;
        await GitAsync(path, "init", "--quiet", "--bare");

        return path;
    }

    private static async Task<string> GitAsync(string folder, params string[] arguments) =>
        (await Processes.RunAsync(new ProcessRequest("git", ["-C", folder, .. arguments]), Cancellation)).Match(outcome => outcome.Output.Trim(), error => error.ToString());

    private sealed class ConfiguredGit(IProcessRunner inner, params string[] settings) : IProcessRunner
    {
        public ValueTask<Result<ProcessOutcome, ProcessError>> RunAsync(ProcessRequest request, CancellationToken cancellationToken) =>
            inner.RunAsync(request with { Arguments = [.. settings.SelectMany(setting => new[] { "-c", setting }), .. request.Arguments] }, cancellationToken);
    }

    private sealed class InterruptedClone(string outside) : IProcessRunner
    {
        public bool Linked { get; private set; }

        public async ValueTask<Result<ProcessOutcome, ProcessError>> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            var destination = request.Arguments[^1];
            var objects = Directory.CreateDirectory(Path.Combine(destination, ".git", "objects")).FullName;
            await File.WriteAllTextAsync(Path.Combine(objects, "pack"), "partial", cancellationToken);
            File.SetAttributes(Path.Combine(objects, "pack"), FileAttributes.ReadOnly);

            try
            {
                Directory.CreateSymbolicLink(Path.Combine(destination, "linked"), outside);
                Linked = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Assert.Skip($"This machine cannot create a symbolic link: {exception.Message}");
            }

            throw new OperationCanceledException(cancellationToken);
        }
    }
}
