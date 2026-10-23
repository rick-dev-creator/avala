using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Workspaces.Testing;
using Xunit;

namespace Avala.Testing;

public sealed class TemporaryRepository : IAsyncDisposable
{
    private static readonly string[] Identity = ["-c", "user.name=Test", "-c", "user.email=test@localhost", "-c", "commit.gpgsign=false"];

    private readonly DirectoryInfo scratch;
    private readonly IProcessRunner processes;
    private readonly string name;
    private readonly Option<InMemoryGit> inMemory;

    private TemporaryRepository(DirectoryInfo scratch, IProcessRunner processes, string name, Option<InMemoryGit> inMemory)
    {
        this.scratch = scratch;
        this.processes = processes;
        this.name = name;
        this.inMemory = inMemory;
    }

    public string Path => System.IO.Path.Combine(scratch.FullName, name);

    public string WorktreeRoot => System.IO.Path.Combine(scratch.FullName, "worktrees");

    public string Remote => System.IO.Path.Combine(scratch.FullName, "octo", "shop.git");

    public static Task<TemporaryRepository> CreateAsync(IProcessRunner processes, CancellationToken cancellationToken) =>
        CreateAsync(processes, "repository", cancellationToken);

    public static async Task<TemporaryRepository> CreateAsync(IProcessRunner processes, string name, CancellationToken cancellationToken)
    {
        var repository = new TemporaryRepository(TemporaryFolder.Create(), processes, name, Option<InMemoryGit>.None);
        Directory.CreateDirectory(repository.Path);

        await repository.GitAsync(cancellationToken, "init", "--quiet", "--initial-branch=main");
        await File.WriteAllTextAsync(System.IO.Path.Combine(repository.Path, "README.md"), "# Shop\n", cancellationToken);
        await repository.GitAsync(cancellationToken, "add", "--all");
        await repository.GitAsync(cancellationToken, [.. Identity, "commit", "--quiet", "--message", "Initial commit"]);

        return repository;
    }

    public static async Task<TemporaryRepository> InMemoryAsync(InMemoryGit git, IProcessRunner processes, string name, CancellationToken cancellationToken)
    {
        var repository = new TemporaryRepository(TemporaryFolder.Create(), processes, name, git);
        await git.CreateAsync(repository.Path, [("README.md", "# Shop\n")], cancellationToken);

        return repository;
    }

    public async Task CommitAsync(string relativePath, string content, CancellationToken cancellationToken)
    {
        if (inMemory.Match(git => git, () => null!) is { } git)
        {
            await git.CommitAsync(Path, relativePath, content, cancellationToken);

            return;
        }

        var file = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file) ?? Path);
        await File.WriteAllTextAsync(file, content, cancellationToken);
        await GitAsync(cancellationToken, "add", "--all");
        await GitAsync(cancellationToken, [.. Identity, "commit", "--quiet", "--message", $"Add {relativePath}"]);
    }

    public async Task<string> PublishAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Remote);
        await GitInAsync(Remote, cancellationToken, "init", "--quiet", "--bare", "--initial-branch=main");
        await GitAsync(cancellationToken, "remote", "add", "origin", Remote);
        await GitAsync(cancellationToken, "push", "--quiet", "origin", "main");

        return Remote;
    }

    public async Task CommitToRemoteAsync(string branch, string relativePath, string content, CancellationToken cancellationToken)
    {
        var clone = System.IO.Path.Combine(scratch.FullName, $"clone-{Guid.NewGuid():N}");
        await GitInAsync(scratch.FullName, cancellationToken, "clone", "--quiet", Remote, clone);
        var existing = await processes.RunAsync(new ProcessRequest("git", ["-C", clone, "checkout", "--quiet", branch]), cancellationToken);

        if (!existing.Match(outcome => outcome.Succeeded, _ => false))
        {
            await GitInAsync(clone, cancellationToken, "checkout", "--quiet", "-b", branch);
        }

        await File.WriteAllTextAsync(System.IO.Path.Combine(clone, relativePath), content, cancellationToken);
        await GitInAsync(clone, cancellationToken, "add", "--all");
        await GitInAsync(clone, cancellationToken, [.. Identity, "commit", "--quiet", "--message", $"Change {relativePath}"]);
        await GitInAsync(clone, cancellationToken, "push", "--quiet", "origin", branch);
    }

    public Task<string> GitAsync(CancellationToken cancellationToken, params string[] arguments) =>
        GitInAsync(Path, cancellationToken, arguments);

    public async Task<string> GitInAsync(string directory, CancellationToken cancellationToken, params string[] arguments)
    {
        Assert.True(inMemory.IsNone, "This test runs git itself on a repository whose git is in memory; mark its class [UsesRealGit] if it proves git's own behavior, or prove the behavior without git.");
        var outcome = Outcomes.Succeeds(await processes.RunAsync(new ProcessRequest("git", ["-C", directory, .. arguments]), cancellationToken));

        Assert.True(outcome.Succeeded, outcome.Error);

        return outcome.Output.Trim();
    }

    public ValueTask DisposeAsync() => TemporaryFolder.RemoveAsync(scratch);
}
