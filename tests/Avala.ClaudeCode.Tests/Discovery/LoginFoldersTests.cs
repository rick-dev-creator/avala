using Avala.Agents.Contracts.Connections;
using Avala.ClaudeCode.Conversations;
using Avala.ClaudeCode.Discovery;
using Avala.Testing;

namespace Avala.ClaudeCode.Tests.Discovery;

public sealed class LoginFoldersTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryConfigurationFolderHoldingALoginIsAConnectionNamedAfterItAsync()
    {
        using var home = new TemporaryFolder();
        using var elsewhere = new TemporaryFolder();
        await LoginAsync(home.Path, ".claude", ".credentials.json");
        await LoginAsync(home.Path, ".claude-work", ".claude.json");
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude-empty"));
        var team = await LoginAsync(elsewhere.Path, "team logins", ".credentials.json");

        var found = await new LoginFolders(new UserHome(home.Path, [team, Path.Combine(home.Path, ".claude-work")])).DiscoverAsync(Cancellation);

        Assert.Equal(
            [
                new DiscoveredConnection(new ConnectionName("claude"), "claude-code", new CredentialReference("login", Path.Combine(home.Path, ".claude"))),
                new DiscoveredConnection(new ConnectionName("claude-team-logins"), "claude-code", new CredentialReference("login", team)),
                new DiscoveredConnection(new ConnectionName("claude-work"), "claude-code", new CredentialReference("login", Path.Combine(home.Path, ".claude-work"))),
            ],
            found);
    }

    [Fact]
    public async Task AHomeWithoutLoginsDiscoversNothingAsync()
    {
        using var home = new TemporaryFolder();

        Assert.Empty(await new LoginFolders(new UserHome(home.Path, [])).DiscoverAsync(Cancellation));
    }

    private static async Task<string> LoginAsync(string parent, string name, string mark)
    {
        var folder = Directory.CreateDirectory(Path.Combine(parent, name)).FullName;
        await File.WriteAllTextAsync(Path.Combine(folder, mark), "{}", Cancellation);

        return folder;
    }
}
