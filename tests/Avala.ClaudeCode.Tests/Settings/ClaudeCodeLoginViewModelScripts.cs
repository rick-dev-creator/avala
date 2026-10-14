using System.Collections.Immutable;
using Avala.Agents.Contracts.Connections;
using Avala.ClaudeCode.Conversations;
using Avala.ClaudeCode.Settings;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.ClaudeCode.Tests.Settings;

public sealed class ClaudeCodeLoginViewModelScripts
{
    private readonly RecordedConnections connections = new();

    [Fact]
    public void ALoginShowsItsWindowAndWhatEachMessageReadsWithBothCompactionsOnByDefault() =>
        ViewModelScript.Given(Login(Discovered()))
            .Then(login =>
            {
                Assert.Equal(("claude-work", "/home/ana/.claude-work", true, true, true), (login.Name, login.Where, login.IsStandard, login.AutoCompact, login.IdleCompaction));
                Assert.Equal("Summarizes the conversation at about 217k tokens, so each message reads at most that much", login.Summary);
            });

    [Fact]
    public void AWindowSetInTheFileThatNoSegmentMatchesIsShownAsCustom() =>
        ViewModelScript.Given(Login(Discovered((CommandLine.CompactAtSetting, "400000"))))
            .Then(login => Assert.Equal((false, false, true, "400k in connections.json"), (login.IsStandard, login.IsLarge, login.HasCustom, login.Custom)));

    [Fact]
    public async Task ChoosingAWindowSavesItAndTheDefaultRemovesItFromTheConnectionAsync()
    {
        var login = Login(Discovered());

        await login.ChooseCommand.ExecuteAsync(CompactChoice.Large);
        var large = (login.IsLarge, login.Summary);
        await login.ChooseCommand.ExecuteAsync(CompactChoice.Standard);

        Assert.Equal((true, "Summarizes the conversation at about 467k tokens, so each message reads at most that much"), large);
        Assert.Equal(
            [Option<string>.Some("500000"), Option<string>.None],
            connections.Declared.Select(declared => declared.Edit.Settings[CommandLine.CompactAtSetting]));
    }

    [Fact]
    public async Task TurningACompactionOffSavesFalseAndTurningItBackOnRemovesItAsync()
    {
        var login = Login(Discovered());

        await login.ToggleAutoCompactCommand.ExecuteAsync(null);
        var off = (login.AutoCompact, login.Summary);
        await login.ToggleAutoCompactCommand.ExecuteAsync(null);
        await login.ToggleIdleCompactionCommand.ExecuteAsync(null);

        Assert.Equal((false, "Never summarizes: every message reads the whole conversation, however long"), off);
        Assert.Equal(
            [(CommandLine.AutoCompactSetting, Option<string>.Some("false")), (CommandLine.AutoCompactSetting, Option<string>.None), (CommandLine.IdleCompactionSetting, Option<string>.Some("false"))],
            connections.Declared.Select(declared => (declared.Edit.Settings.Keys.Single(), declared.Edit.Settings.Values.Single())));
    }

    [Fact]
    public async Task IdleCompactionCanOnlyBeToggledWhileAutomaticCompactionIsOnAsync()
    {
        var login = Login(Discovered());

        await login.ToggleAutoCompactCommand.ExecuteAsync(null);

        Assert.False(login.ToggleIdleCompactionCommand.CanExecute(null));
    }

    [Fact]
    public async Task ADiscoveredLoginIsDeclaredWithItsCredentialAndADeclaredOneIsReplacedAsync()
    {
        await Login(Discovered()).ChooseCommand.ExecuteAsync(CompactChoice.Small);
        await Login(Discovered() with { Origin = ConnectionOrigin.Declared }).ChooseCommand.ExecuteAsync(CompactChoice.Small);

        Assert.Equal(Option<ConnectionName>.None, connections.Declared[0].Replacing);
        Assert.Equal(Option<CredentialReference>.Some(new CredentialReference("login", "/home/ana/.claude-work")), connections.Declared[0].Edit.Credential);
        Assert.Equal(Option<ConnectionName>.Some(new ConnectionName("claude-work")), connections.Declared[1].Replacing);
    }

    [Fact]
    public async Task ARefusedSaveSaysWhyAndTryAgainRepeatsItAsync()
    {
        var login = Login(Discovered());
        connections.Refusal = ConnectionError.Unwritable;

        await login.ChooseCommand.ExecuteAsync(CompactChoice.Small);
        var refused = (login.HasProblem, login.Problem);
        connections.Refusal = Option<ConnectionError>.None;
        await login.RetryCommand.ExecuteAsync(null);

        Assert.Equal((true, "Could not save this setting: Unwritable"), refused);
        Assert.Equal((false, 2), (login.HasProblem, connections.Declared.Count));
        Assert.Equal(connections.Declared[0].Edit.Settings, connections.Declared[1].Edit.Settings);
    }

    private ClaudeCodeLoginViewModel Login(DeclaredConnection connection) => new(connection, connections);

    private static DeclaredConnection Discovered(params (string Name, string Value)[] settings) =>
        new(new ConnectionName("claude-work"), ClaudeCodeProvider.Id, "login")
        {
            Origin = ConnectionOrigin.Discovered,
            Reference = "/home/ana/.claude-work",
            Settings = settings.ToImmutableDictionary(setting => setting.Name, setting => setting.Value),
        };
}
