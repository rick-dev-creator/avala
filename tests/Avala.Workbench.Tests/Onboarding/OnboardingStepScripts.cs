using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Components.Privacy;
using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Telemetry;
using Avala.Workbench.Onboarding;
using Avala.Workbench.Tests.Accounts;

namespace Avala.Workbench.Tests.Onboarding;

public sealed class AgentRowViewModelScripts
{
    private static readonly ConnectionCatalog Catalog = new FakeConnections("claude-work").Catalog;

    [Fact]
    public void AnAgentIsSignedInAtItsLimitOrSignedOutWithItsProviderAndMonogram()
    {
        var preview = Option<Result<ConnectionPreview, JobRejection>>.Some(FakePreview.ByCapacity("claude-work", ChoiceReason.MostCapacity, ("claude-work", 0.2, true), ("claude-personal", 0.97, false)));
        DeclaredConnection[] connections =
        [
            new(new ConnectionName("claude-work"), "claude-code", "login"),
            new(new ConnectionName("claude-personal"), "claude-code", "login"),
            new(new ConnectionName("claude-old"), "claude-code", "login"),
        ];
        Option<ConnectionError>[] problems = [Option<ConnectionError>.None, Option<ConnectionError>.None, ConnectionError.MissingFolder];

        var rows = connections.Zip(problems, (connection, problem) => AgentRowViewModel.Of(connection, problem, Catalog, preview));

        Assert.Equal(
            [
                ("claude-work", "CW", "Claude Code", AgentState.Ready, "Signed in"),
                ("claude-personal", "CP", "Claude Code", AgentState.AtLimit, "At its limit"),
                ("claude-old", "CO", "Claude Code", AgentState.SignedOut, "Signed out"),
            ],
            rows.Select(row => (row.Name, row.Monogram, row.Provider, row.State, row.Status)));
    }
}

public sealed class AgentsStepViewModelScripts
{
    private readonly Touring touring = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheStepListsEveryAgentConnectionOfThisComputerAndOneThatCannotSignInIsSignedOutAsync()
    {
        touring.Connections.Catalog = touring.Connections.Catalog with { Connections = [.. touring.Connections.Catalog.Connections, new DeclaredConnection(new ConnectionName("codex"), "simulator", "login")] };
        touring.Connections.SignedOut.Add("codex");
        var agents = touring.Agents();

        await agents.LoadAsync(Cancellation);

        Assert.Equal([("claude-work", "Simulator", AgentState.Ready), ("codex", "Simulator", AgentState.SignedOut)], agents.Agents.Select(row => (row.Name, row.Provider, row.State)));
        Assert.True(agents.HasAgents);
    }

    [Fact]
    public async Task WithoutAnAgentCheckAgainFindsOneSignedInSinceAsync()
    {
        var signedIn = touring.Connections.Catalog;
        touring.Connections.Catalog = signedIn with { Connections = [] };
        touring.Connections.Discovered = signedIn;
        var agents = touring.Agents();
        await agents.LoadAsync(Cancellation);
        var before = agents.HasAgents;

        await agents.CheckAgainCommand.ExecuteAsync(null);

        Assert.Equal((false, true), (before, agents.HasAgents));
        Assert.Equal(["claude-work"], agents.Agents.Select(row => row.Name));
    }

    [Fact]
    public async Task EveryHarnessThatSaysHowToInstallItOffersItsCommandAsync()
    {
        touring.Connections.Catalog = touring.Connections.Catalog with
        {
            Connections = [],
            Providers = [new ProviderInfo("simulator", "Simulator"), new ProviderInfo("harness", "Harness") { Install = "brew install harness", SignIn = "harness" }],
        };
        var agents = touring.Agents();

        await agents.LoadAsync(Cancellation);

        Assert.Equal([("Harness", "brew install harness", "Then run harness once to sign in.")], agents.Installs.Select(install => (install.Provider, install.Command, install.SignIn)));
    }
}

public sealed class InstallRowViewModelScripts
{
    [Fact]
    public async Task CopyPutsTheInstallCommandOnTheClipboardAsync()
    {
        var clipboard = new FakeClipboard();
        var install = new InstallRowViewModel("Harness", "brew install harness", AgentPhrases.SignIn(Option<string>.None), clipboard);

        await install.CopyCommand.ExecuteAsync(null);

        Assert.Equal((true, "brew install harness", "Then sign in once."), (install.IsCopied, Assert.Single(clipboard.Copied), install.SignIn));
    }
}

public sealed class CodeStepViewModelScripts
{
    private readonly Touring touring = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheStepShowsTheProjectsFolderAsAPersonalPathAndTheAccountsFoundOnThisComputerAsync()
    {
        touring.Forges.Accounts.Add(Kinds.Detected("hub", Kinds.Hub, CredentialSource.Cli, "octocat", "api.hub.example"));
        var code = touring.Code();

        await code.LoadAsync(Cancellation);

        Assert.Equal(Personal.Path("/home/ada/Projects"), code.Folder);
        Assert.Equal([(true, "oc•••••")], code.Accounts.Accounts.Select(row => (row.IsDetected, row.Login.ToString())));
    }

    [Fact]
    public async Task ChangeChoosesAnotherProjectsFolderAsync()
    {
        touring.Picker.Answer = "/home/ada/Code";
        var code = touring.Code();
        await code.LoadAsync(Cancellation);

        await code.ChangeFolderCommand.ExecuteAsync(null);

        Assert.Equal((Personal.Path("/home/ada/Code"), "/home/ada/Code"), (code.Folder, Assert.Single(touring.Projects.Choices)));
    }
}

public sealed class PrivacyStepViewModelScripts
{
    private readonly Touring touring = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BothSwitchesStartOnAndTurnHidingAndStatisticsOffInTheirSettingsAsync()
    {
        var privacy = touring.Privacy();
        await privacy.LoadAsync(Cancellation);
        var before = (privacy.PersonalDetails.IsHidden, privacy.Statistics.IsOn);

        await privacy.PersonalDetails.ToggleCommand.ExecuteAsync(null);
        await privacy.Statistics.ToggleCommand.ExecuteAsync(null);

        Assert.Equal((true, true), before);
        Assert.Equal((false, TelemetryStatus.Off), (Assert.Single(touring.Appearance.Changes).HidesPersonalDetails, touring.Telemetry.State.Status));
    }
}
