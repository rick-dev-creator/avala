using Avala.Agents.Contracts.Connections;
using Avala.ClaudeCode.Conversations;
using Avala.ClaudeCode.Settings;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.ClaudeCode.Tests.Settings;

public sealed class ClaudeCodeSettingsViewModelScripts : IDisposable
{
    private readonly RecordedConnections connections = new();
    private readonly TestUiDispatcher ui = new();

    [Fact]
    public async Task TheSectionListsEveryUsableClaudeCodeLoginAndNoOtherConnectionAsync()
    {
        connections.Connections.AddRange(
        [
            new DeclaredConnection(new ConnectionName("claude-work"), ClaudeCodeProvider.Id, "login") { Origin = ConnectionOrigin.Discovered, Reference = "/home/ana/.claude-work" },
            new DeclaredConnection(new ConnectionName("claude-team"), ClaudeCodeProvider.Id, "apiKey") { Problem = ConnectionError.MissingVariable },
            new DeclaredConnection(new ConnectionName("simulator"), "simulator", Option<string>.None),
        ]);
        var section = new ClaudeCodeSettingsViewModel(connections, ui);

        section.Activate();
        await section.Loading;

        Assert.True(await ui.ReadAsync(() => section.HasLogins));
        Assert.Equal(["claude-work"], await ui.ReadAsync(() => section.Logins.Select(login => login.Name).ToArray()));
    }

    [Fact]
    public async Task WithoutAClaudeCodeLoginTheSectionHasNothingToShowAsync()
    {
        var section = new ClaudeCodeSettingsViewModel(connections, ui);

        section.Activate();
        await section.Loading;

        Assert.False(await ui.ReadAsync(() => section.HasLogins));
    }

    public void Dispose() => ui.Dispose();
}
