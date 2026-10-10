using Avala.Sdk.Processes;
using Xunit;

namespace Avala.Testing;

public sealed class TemporaryRepository : IAsyncDisposable
{
    private static readonly string[] Identity = ["-c", "user.name=Test", "-c", "user.email=test@localhost", "-c", "commit.gpgsign=false"];

    private readonly DirectoryInfo scratch;
    private readonly IProcessRunner processes;

    private TemporaryRepository(DirectoryInfo scratch, IProcessRunner processes)
    {
        this.scratch = scratch;
        this.processes = processes;
    }

    public string Path => System.IO.Path.Combine(scratch.FullName, "repository");

    public string WorktreeRoot => System.IO.Path.Combine(scratch.FullName, "worktrees");

    public string Remote => System.IO.Path.Combine(scratch.FullName, "octo", "shop.git");

    public static async Task<TemporaryRepository> CreateAsync(IProcessRunner processes, CancellationToken cancellationToken)
    {
        var repository = new TemporaryRepository(Directory.CreateTempSubdirectory("avala-"), processes);
        Directory.CreateDirectory(repository.Path);

        await repository.GitAsync(cancellationToken, "init", "--quiet", "--initial-branch=main");
        await File.WriteAllTextAsync(System.IO.Path.Combine(repository.Path, "README.md"), "# Shop\n", cancellationToken);
        await repository.GitAsync(cancellationToken, "add", "--all");
        await repository.GitAsync(cancellationToken, [.. Identity, "commit", "--quiet", "--message", "Initial commit"]);

        return repository;
    }

    public async Task CommitAsync(string relativePath, string content, CancellationToken cancellationToken)
    {
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
        var outcome = Outcomes.Succeeds(await processes.RunAsync(new ProcessRequest("git", ["-C", directory, .. arguments]), cancellationToken));

        Assert.True(outcome.Succeeded, outcome.Error);

        return outcome.Output.Trim();
    }

    public ValueTask DisposeAsync()
    {
        TemporaryFolder.Remove(scratch);

        return ValueTask.CompletedTask;
    }
}
