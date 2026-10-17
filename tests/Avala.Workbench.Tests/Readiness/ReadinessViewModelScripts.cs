using Avala.Components.Privacy;
using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Readiness;
using Avala.Workbench.Readying;
using Avala.Workbench.Tests.Accounts;
using Avala.Workbench.Tests.RepositoryChoice;

namespace Avala.Workbench.Tests.Readiness;

public sealed class ReadinessCardViewModelScripts
{
    private readonly FakeCatalog catalog = new();
    private readonly FakeScratch scratch = new();
    private readonly FakeSourceControl forges = new();
    private readonly FakeForgeReadiness readiness = new();

    [Fact]
    public async Task TwoLoginsForTheHostAreListedWithTheirToolAndOnlyTheChosenOneIsConnectedAsync()
    {
        forges.Tools.Add(Kinds.HubCli);
        readiness.Shown = new ForgeReadiness(ForgeReadinessKind.Connectable, "hub.example")
        {
            Accounts = [Kinds.Detected("hub", Kinds.Hub, CredentialSource.Cli, "octocat", "hub.example"), Kinds.Detected("hub-2", Kinds.Hub, CredentialSource.Environment, null, "hub.example", reference: "HUB_TOKEN")],
            Forge = Kinds.Hub,
            SignIn = Kinds.HubCli,
        };
        var card = await ShownAsync(catalog.Add("Fix the build").Summary.Job);
        var item = Assert.Single(card.Items);

        Assert.Equal(("Choose the account that connects hub.example to open pull requests and follow their checks", string.Empty), (item.Text.Show(hidden: false), item.Action));
        Assert.Equal([("octocat", "hub"), ("hub-2", "$HUB_TOKEN")], item.Accounts.Select(account => (account.Login.Show(hidden: false), account.Tool)));

        await item.Accounts[1].ConnectCommand.ExecuteAsync(null);

        Assert.Equal([new ForgeName("hub-2")], forges.Connected.Select(connection => connection.Name));
    }

    [Fact]
    public async Task AScratchRepositorysFirstJobShowsNoCardAsync()
    {
        readiness.Shown = new ForgeReadiness(ForgeReadinessKind.SignInNeeded, "hub.example") { SignIn = Kinds.HubCli };
        var job = catalog.Add("Sketch an idea").Summary.Job;
        catalog.Change(job, history => history with { Summary = history.Summary with { Repository = $"{scratch.Folder}/sketch" } });

        var card = await ShownAsync(job);

        Assert.Equal((false, 0), (card.IsShown, card.Items.Count));
    }

    [Fact]
    public async Task ARefusedConnectionKeepsTheItemAndSaysWhyAsync()
    {
        forges.Refusal = ForgeError.Unwritable;
        readiness.Shown = new ForgeReadiness(ForgeReadinessKind.Connectable, "hub.example")
        {
            Accounts = [Kinds.Detected("hub", Kinds.Hub, CredentialSource.Cli, "octocat", "hub.example")],
            Forge = Kinds.Hub,
            SignIn = Kinds.HubCli,
        };
        var card = await ShownAsync(catalog.Add("Fix the build").Summary.Job);

        await card.Items[0].ActCommand.ExecuteAsync(null);

        var item = Assert.Single(card.Items);
        Assert.Equal((false, ReadinessPhrases.Connect), (item.IsDone, item.Action));
        Assert.NotEqual(string.Empty, item.Error);
    }

    private async Task<ReadinessCardViewModel> ShownAsync(JobId job)
    {
        var files = new CommittedFiles().With("/repo", ".avala/checks.json", """{ "checks": [] }""");
        var card = new ReadinessCardViewModel(
            new RepositoryReadiness(new FirstJobs(catalog, Scratching.Folders(scratch)), readiness, files, new FakeRules()),
            new ReadinessActions(forges.Source, new FakeDocumentation(), new FakeReadinessLinks()));
        card.OnRegionContextChanged(job);
        await card.Loading;

        return card;
    }
}

public sealed class ReadinessItemViewModelScripts
{
    [Fact]
    public void AnItemThatIsDoneShowsItsOutcomeAndOffersNothingToDo()
    {
        var acted = 0;
        var item = new ReadinessItemViewModel(new ReadinessStep(ReadinessNeed.Rules, new PersonalPhrase(ReadinessPhrases.RulesUncommitted), ReadinessPhrases.Commit), _ => Task.FromResult(++acted), []);
        Assert.True(item.ActCommand.CanExecute(null));

        item.Finish(new PersonalPhrase("Committed .avala/jobs.json, so new jobs follow it"));

        Assert.Equal((true, string.Empty, false, "Committed .avala/jobs.json, so new jobs follow it"), (item.IsDone, item.Action, item.ActCommand.CanExecute(null), item.Text.Show(hidden: false)));
        Assert.Equal(0, acted);
    }
}

public sealed class ReadinessAccountViewModelScripts
{
    [Fact]
    public async Task AnAccountShowsItsMaskedLoginAndToolAndConnectsThroughItsCommandAsync()
    {
        var connected = 0;
        var account = new ReadinessAccountViewModel(Personal.Login("octocat"), "gh", _ => Task.FromResult(++connected));

        await account.ConnectCommand.ExecuteAsync(null);

        Assert.Equal(("oc•••••", "gh", 1), (account.Login.ToString(), account.Tool, connected));
    }
}
