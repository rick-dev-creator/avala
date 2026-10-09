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
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, content, cancellationToken);
        await GitAsync(cancellationToken, "add", "--all");
        await GitAsync(cancellationToken, [.. Identity, "commit", "--quiet", "--message", $"Add {relativePath}"]);
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
        foreach (var file in scratch.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes = FileAttributes.Normal;
        }

        scratch.Delete(recursive: true);

        return ValueTask.CompletedTask;
    }
}
