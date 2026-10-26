using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.ClaudeCode.Conversations;
using Avala.ClaudeCode.Folders;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.ClaudeCode.Tests.Conversations;

public sealed class WritableFolderTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASessionInALinkedWorktreeMayWriteTheMemoryOfItsMainRepositoryAndOfItsWorktreeAndTheWorktreesOfItsSubagentsAsync()
    {
        using var folder = new TemporaryFolder();
        var repository = Path.Combine(folder.Path, "shop");
        var worktree = Path.Combine(folder.Path, "worktree");
        Directory.CreateDirectory(Path.Combine(repository, ".git", "worktrees", "worktree"));
        Directory.CreateDirectory(worktree);
        await File.WriteAllTextAsync(Path.Combine(worktree, ".git"), $"gitdir: {Path.Combine(repository, ".git", "worktrees", "worktree")}\n", Cancellation);

        await using var session = await StartedAsync(worktree);

        Assert.Equal(
            [
                Path.Combine("/logins/work", "projects", Slug(repository), "memory"),
                Path.Combine("/logins/work", "projects", Slug(worktree), "memory"),
                Path.Combine(repository, ".claude", "worktrees"),
            ],
            session.WritableFolders);
    }

    [Fact]
    public async Task ASessionInAMainRepositoryMayWriteItsOwnMemoryAndTheWorktreesOfItsSubagentsAsync()
    {
        using var folder = new TemporaryFolder();
        Directory.CreateDirectory(Path.Combine(folder.Path, ".git"));

        await using var session = await StartedAsync(folder.Path);

        Assert.Equal([Path.Combine("/logins/work", "projects", Slug(folder.Path), "memory"), Path.Combine(folder.Path, ".claude", "worktrees")], session.WritableFolders);
    }

    private static async Task<IAgentSession> StartedAsync(string folder)
    {
        var home = new UserHome(Path.Combine(Path.GetTempPath(), "avala-no-home"), []);
        var provider = new ClaudeCodeProvider(new FakeCli(), new ConfigurationFolders(home), home with { SignsInImplicitly = true }, new FixedPhoneStart());
        var connection = new ConnectionEnvironment { ConfigurationDirectory = "/logins/work" };

        return (await provider.StartAsync(new SessionOptions(folder, PermissionMode.AskEveryTime) { Connection = connection }, Cancellation))
            .Match(session => session, error => throw new InvalidOperationException(error.ToString()));
    }

    private static string Slug(string folder) => new([.. Path.GetFullPath(folder).Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-')]);
}
