using Avala.Forges.Contracts;
using Avala.Sdk;
using Avala.Workbench.Accounts;

namespace Avala.Workbench.Tests.Accounts;

public sealed class AccountRowViewModelScripts
{
    private readonly FakeSourceControl forges = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryAccountShowsItsLogoLoginHostAndHowItSignsInAndADetectedOneOffersAddAsync()
    {
        forges.Tools.Add(Kinds.HubCli);
        forges.Accounts.AddRange([
            Kinds.Declared("hub", Kinds.Hub, CredentialSource.Cli, login: "octocat"),
            Kinds.Detected("hub-2", Kinds.Hub, CredentialSource.Environment, login: null, "api.hub.example", reference: "HUB_TOKEN"),
        ]);
        var accounts = await LoadedAsync();

        Assert.Equal(
            [("oc•••••", "api.hub.example", "hub", Kinds.Hub.Logo, AccountState.SignedIn, false), ("hub-2", "api.hub.example", AccountPhrases.Token, Kinds.Hub.Logo, AccountState.Detected, true)],
            accounts.Accounts.Select(row => (row.Login.ToString(), row.Host, row.SignIn, row.Logo, row.State, row.IsDetected)));
        Assert.Equal((false, true), (accounts.Accounts[0].AddCommand.CanExecute(null), accounts.Accounts[0].RemoveCommand.CanExecute(null)));
    }

    [Fact]
    public async Task AnAccountThatCannotSignInAsksForAttentionWithAShortReasonAsync()
    {
        forges.Accounts.Add(Kinds.Declared("work", Kinds.Hub, CredentialSource.Environment, problem: ForgeError.MissingCredential, reference: "WORK_TOKEN"));

        var row = Assert.Single((await LoadedAsync()).Accounts);

        Assert.Equal((AccountState.Attention, "Token not set", "work"), (row.State, row.Problem, row.Login.ToString()));
    }

    private async Task<SourceControlViewModel> LoadedAsync()
    {
        var accounts = new SourceControlViewModel(forges.Source, new ConnectSheetViewModel(forges.Source));
        await accounts.LoadAsync(Cancellation);

        return accounts;
    }
}

public sealed class SourceControlViewModelScripts
{
    private readonly FakeSourceControl forges = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AddingADetectedAccountDeclaresItWithoutItsDefaultUrlAndShowsItAddedAsync()
    {
        forges.Accounts.Add(Kinds.Detected("hub", Kinds.Hub, CredentialSource.Cli, "octocat", "api.hub.example"));
        var accounts = await LoadedAsync();

        await accounts.Accounts[0].AddCommand.ExecuteAsync(null);

        Assert.Equal([new ForgeConnectionEdit(new ForgeName("hub"), "hub") { Source = CredentialSource.Cli }], forges.Connected);
        Assert.Equal(AccountState.SignedIn, Assert.Single(accounts.Accounts).State);
    }

    [Fact]
    public async Task ADetectedTokenWithoutAHostOpensTheConnectSheetToAskForItAsync()
    {
        forges.Accounts.Add(Kinds.Detected("cup", Kinds.Cup, CredentialSource.Environment, login: null, string.Empty, reference: "CUP_TOKEN"));
        var accounts = await LoadedAsync();

        await accounts.Accounts[0].AddCommand.ExecuteAsync(null);

        Assert.Empty(forges.Connected);
        Assert.Equal((true, "cup", true, true, "CUP_TOKEN"), (accounts.Connecting.IsOpen, accounts.Connecting.Kind, accounts.Connecting.NeedsHost, accounts.Connecting.UsesToken, accounts.Connecting.TokenVariable));
    }

    [Fact]
    public async Task RemovingADeclaredAccountTakesItOutOfTheListAsync()
    {
        forges.Accounts.Add(Kinds.Declared("hub", Kinds.Hub, CredentialSource.Cli, login: "octocat"));
        var accounts = await LoadedAsync();

        await accounts.Accounts[0].RemoveCommand.ExecuteAsync(null);

        Assert.Equal([new ForgeName("hub")], forges.Removed);
        Assert.Empty(accounts.Accounts);
    }

    private async Task<SourceControlViewModel> LoadedAsync()
    {
        var accounts = new SourceControlViewModel(forges.Source, new ConnectSheetViewModel(forges.Source));
        await accounts.LoadAsync(Cancellation);

        return accounts;
    }
}

public sealed class ForgeKindViewModelScripts
{
    private readonly FakeSourceControl forges = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheConnectSheetOffersEveryInstalledForgeAndAsksAHostOnlyWhereTheForgeHasNoDefaultPrefilledWithItsSuggestionAsync()
    {
        var sheet = await OpenedAsync();
        var hub = (sheet.Kind, sheet.NeedsHost);

        sheet.Kinds[2].SelectCommand.Execute(null);

        Assert.Equal(["hub", "cup", "saucer"], sheet.Kinds.Select(kind => kind.Id));
        Assert.Equal(("hub", false), hub);
        Assert.Equal((true, "https://saucer.example", true), (sheet.NeedsHost, sheet.Host, sheet.Kinds[2].IsSelected));
    }

    private async Task<ConnectSheetViewModel> OpenedAsync()
    {
        var sheet = new ConnectSheetViewModel(forges.Source);
        await sheet.OpenAsync(Cancellation);

        return sheet;
    }
}

public sealed class ConnectSheetViewModelScripts
{
    private readonly FakeSourceControl forges = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheForgesACommandLineToolSignsInToComeFirstAndTheFirstIsChosenAsync()
    {
        forges.Tools.Add(Kinds.CupCli);

        var sheet = await OpenedAsync();

        Assert.Equal(["cup", "saucer", "hub"], sheet.Kinds.Select(kind => kind.Id));
        Assert.Equal("cup", sheet.Kind);
    }

    [Fact]
    public async Task ASignedInCliIsChosenAndConnectsAtOnceAsync()
    {
        forges.Tools.Add(Kinds.HubCli);
        forges.Accounts.Add(Kinds.Detected("hub", Kinds.Hub, CredentialSource.Cli, "octocat", "api.hub.example"));
        var sheet = await OpenedAsync();

        await sheet.ConnectCommand.ExecuteAsync(null);

        Assert.Equal((CliState.SignedIn, "Signed in as oc•••••"), (sheet.CliState, sheet.CliStatus.ToString()));
        Assert.Equal([new ForgeConnectionEdit(new ForgeName("hub"), "hub") { Source = CredentialSource.Cli }], forges.Connected);
        Assert.False(sheet.IsOpen);
    }

    [Fact]
    public async Task ASignedOutCliSaysSoWithItsSignInCommandAndCheckAgainEnablesConnectOnceItIsSignedInAsync()
    {
        forges.Tools.Add(Kinds.CupCli);
        var sheet = await OpenedAsync();
        sheet.Kinds.Single(kind => kind.Id == "cup").SelectCommand.Execute(null);
        sheet.Host = "git.example.com";
        var signedOut = (sheet.CliState, sheet.CliStatus.ToString(), sheet.SignInCommand, sheet.ConnectCommand.CanExecute(null));
        forges.Accounts.Add(Kinds.Detected("work", Kinds.Cup, CredentialSource.Cli, "ada", "git.example.com", reference: "work"));

        await sheet.CheckAgainCommand.ExecuteAsync(null);
        await sheet.ConnectCommand.ExecuteAsync(null);

        Assert.Equal((CliState.SignedOut, "cup is not signed in", "cup login add", false), signedOut);
        Assert.Equal(
            [new ForgeConnectionEdit(new ForgeName("example"), "cup") { Url = new Uri("https://git.example.com"), Source = CredentialSource.Cli, Reference = "work" }],
            forges.Connected);
    }

    [Fact]
    public async Task ACliThatIsNotInstalledIsSaidSoAndTheTokenIsChosenAsync()
    {
        forges.Tools.Add(Kinds.HubCli with { Installed = false });

        var sheet = await OpenedAsync();
        var chosen = sheet.UsesToken;
        sheet.UsesCli = true;

        Assert.True(chosen);
        Assert.Equal((CliState.Missing, "hub is not installed", false), (sheet.CliState, sheet.CliStatus.ToString(), sheet.ConnectCommand.CanExecute(null)));
    }

    [Fact]
    public async Task ATokenIsConnectedByTheNameOfItsVariableUnderAFreeNameAsync()
    {
        forges.Accounts.Add(Kinds.Declared("hub", Kinds.Hub, CredentialSource.Cli, login: "octocat"));
        var sheet = await OpenedAsync();
        var suggested = sheet.TokenVariable;
        sheet.TokenVariable = "WORK_HUB_TOKEN";

        await sheet.ConnectCommand.ExecuteAsync(null);

        Assert.Equal(("HUB_TOKEN", false), (suggested, sheet.UsesCli));
        Assert.Equal([new ForgeConnectionEdit(new ForgeName("hub-2"), "hub") { Source = CredentialSource.Environment, Reference = "WORK_HUB_TOKEN" }], forges.Connected);
    }

    [Fact]
    public async Task ARefusedConnectionSaysWhyAndTheSheetStaysOpenAsync()
    {
        forges.Refusal = ForgeError.Unwritable;
        var sheet = await OpenedAsync();

        await sheet.ConnectCommand.ExecuteAsync(null);

        Assert.Equal((true, AccountPhrases.Refusal(ForgeError.Unwritable)), (sheet.IsOpen, sheet.Error));
    }

    private async Task<ConnectSheetViewModel> OpenedAsync()
    {
        var sheet = new ConnectSheetViewModel(forges.Source);
        await sheet.OpenAsync(Cancellation);

        return sheet;
    }
}
