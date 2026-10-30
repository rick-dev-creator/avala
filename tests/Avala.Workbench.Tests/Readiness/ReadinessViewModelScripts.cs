using Avala.Components.Privacy;
using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Verification.Contracts;
using Avala.Workbench.Readiness;
using Avala.Workbench.Readying;
using Avala.Workbench.Tests.Accounts;
using Avala.Workbench.Tests.RepositoryChoice;

namespace Avala.Workbench.Tests.Readiness;

public sealed class ReadinessCardViewModelScripts : IDisposable
{
    private readonly FakeCatalog catalog = new();
    private readonly FakeScratch scratch = new();
    private readonly FakeSourceControl forges = new();
    private readonly FakeForgeReadiness readiness = new();
    private readonly Avala.Workbench.Board.JobBoard board = new();
    private readonly TestUiDispatcher ui = new();
    private readonly FakeProposals proposals = new();

    public void Dispose() => ui.Dispose();

    [Fact]
    public async Task WhileAvalaFindsTheChecksTheCardSaysSoAndOffersNothingToDoAsync()
    {
        var job = catalog.Add("Fix the build").Summary.Job;
        board.Publish(board.Discoveries.SetItem(job, new ChecksDiscovery(job, "/repo", DiscoveryState.Running, [])));

        var card = await ShownAsync(job, declared: false);

        var item = Assert.Single(card.Items);
        Assert.Equal((ReadinessPhrases.ChecksFinding, string.Empty, false), (item.Text.Show(hidden: false), item.Action, item.ActCommand.CanExecute(null)));
    }

    [Fact]
    public async Task ChecksAvalaFoundFinishTheItemWithTheirNamesAsync()
    {
        var job = catalog.Add("Fix the build").Summary.Job;
        board.Publish(board.Discoveries.SetItem(job, new ChecksDiscovery(job, "/repo", DiscoveryState.Running, [])));
        var card = await ShownAsync(job, declared: false);
        await ui.InvokeAsync(card.Activate, TestContext.Current.CancellationToken);
        var revision = await ui.ReadAsync(() => card.Revision);

        board.Publish(board.Discoveries.SetItem(job, new ChecksDiscovery(job, "/repo", DiscoveryState.Found, [new CheckDeclared("tests", "npm test", TimeSpan.FromMinutes(5)), new CheckDeclared("lint", "npm run lint", TimeSpan.FromMinutes(2))])));

        await ui.PresentedAsync(card, () => card.Revision > revision, () => $"revision {card.Revision}");
        var item = Assert.Single(card.Items);
        Assert.Equal(
            (true, "Avala found how to check this repository: tests, lint"),
            await ui.ReadAsync(() => (item.IsDone, item.Text.Show(hidden: false))));
        await ui.InvokeAsync(card.Deactivate, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProposedChecksShowTheirCommandsAndThePersonUsesOrSkipsThemAsync(bool use)
    {
        var job = catalog.Add("Fix the build").Summary.Job;
        board.Publish(board.Discoveries.SetItem(job, new ChecksDiscovery(job, "/repo", DiscoveryState.Proposed, [new CheckDeclared("install", "npm ci", TimeSpan.FromMinutes(5)), new CheckDeclared("tests", "npm test", TimeSpan.FromMinutes(5))])));
        var card = await ShownAsync(job, declared: false);
        var item = Assert.Single(card.Items);

        await (use ? item.ActCommand : item.DismissCommand).ExecuteAsync(null);

        Assert.Equal(
            (ReadinessPhrases.ChecksProposed, "npm ci\nnpm test", ReadinessPhrases.UseChecks, ReadinessPhrases.SkipChecks),
            (item.Text.Show(hidden: false), item.Command, item.Action, item.Dismiss));
        Assert.Equal([(job, use)], proposals.Decided);
    }

    [Fact]
    public async Task ALaterJobWaitingForItsProposedChecksShowsOnlyThatChoiceAsync()
    {
        _ = catalog.Add("Fix the build", Jobs.Contracts.JobStatus.Discarded);
        var later = catalog.Add("Add a discount code").Summary.Job;
        board.Publish(board.Discoveries.SetItem(later, new ChecksDiscovery(later, "/repo", DiscoveryState.Proposed, [new CheckDeclared("tests", "npm test", TimeSpan.FromMinutes(5))])));
        readiness.Shown = new ForgeReadiness(ForgeReadinessKind.SignInNeeded, "hub.example") { SignIn = Kinds.HubCli };

        var card = await ShownAsync(later, declared: false);

        Assert.Equal((true, ReadinessPhrases.UseChecks), (card.IsShown, Assert.Single(card.Items).Action));
    }

    [Fact]
    public async Task WhileAvalaReadsASupervisedRepositoryTheCardSaysItReadsRatherThanRunsAsync()
    {
        var job = catalog.Add("Fix the build").Summary.Job;
        board.Publish(board.Discoveries.SetItem(job, new ChecksDiscovery(job, "/repo", DiscoveryState.Running, []) { Reads = true }));

        var card = await ShownAsync(job, declared: false);

        Assert.Equal(ReadinessPhrases.ChecksReading, Assert.Single(card.Items).Text.Show(hidden: false));
    }

    [Fact]
    public async Task WhenAvalaFindsNoChecksTheCardOffersToDeclareThemAsync()
    {
        var job = catalog.Add("Fix the build").Summary.Job;
        board.Publish(board.Discoveries.SetItem(job, new ChecksDiscovery(job, "/repo", DiscoveryState.NoneFound, [])));

        var card = await ShownAsync(job, declared: false);

        var item = Assert.Single(card.Items);
        Assert.Equal((ReadinessPhrases.ChecksNotFound, ReadinessPhrases.DeclareChecks), (item.Text.Show(hidden: false), item.Action));
    }

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

    private async Task<ReadinessCardViewModel> ShownAsync(JobId job, bool declared = true)
    {
        var files = new CommittedFiles().With("/repo", ".avala/checks.json", """{ "checks": [] }""");
        var card = new ReadinessCardViewModel(
            new RepositoryReadiness(new FirstJobs(catalog, Scratching.Folders(scratch)), readiness, files, declared ? new FakeRules() : new NoChecks()),
            new ReadinessActions(forges.Source, new FakeDocumentation(), new FakeReadinessLinks(), proposals),
            new Avala.Workbench.Presenting.BoardFeed(board, ui, Microsoft.Extensions.Logging.Abstractions.NullLogger<Avala.Workbench.Presenting.BoardFeed>.Instance));
        card.OnRegionContextChanged(job);
        await card.Loading;

        return card;
    }
}

internal sealed class FakeProposals : IChecksProposals
{
    public List<(JobId Job, bool Used)> Decided { get; } = [];

    public ValueTask AcceptAsync(JobId job, CancellationToken cancellationToken)
    {
        Decided.Add((job, true));

        return ValueTask.CompletedTask;
    }

    public ValueTask DeclineAsync(JobId job, CancellationToken cancellationToken)
    {
        Decided.Add((job, false));

        return ValueTask.CompletedTask;
    }
}

internal sealed class NoChecks : IRepositoryChecks
{
    public ValueTask<RepositoryChecks> OfRepositoryAsync(string repository, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new RepositoryChecks(ChecksFileStatus.Absent, [], Option<Workspaces.Contracts.FileOrigin>.None));
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

public sealed class AgentPullRequestNoteViewModelScripts
{
    [Fact]
    public void TheNoteNamesThePullRequestAndItsHostAndOpensSettings()
    {
        var links = new FakeReadinessLinks();
        var board = new Avala.Workbench.Board.JobBoard();
        using var ui = new TestUiDispatcher();
        using var note = new AgentPullRequestNoteViewModel(new AgentPullRequests(board, new FakeMentions()), new Avala.Workbench.Following.LiveFeed(new Avala.Workbench.Following.Pulse(board), ui, Microsoft.Extensions.Logging.Abstractions.NullLogger<Avala.Workbench.Following.LiveFeed>.Instance), links);

        note.OpenSettingsCommand.Execute(null);

        Assert.Equal(
            ("Pull request #123 was opened by the agent. Connect github.com in Settings to follow its checks.", 1),
            (AgentPullRequestNoteViewModel.Phrase(new MentionedPullRequest(123, new Uri("https://github.com/ada/shop/pull/123"))), links.SettingsOpened));
    }
}

public sealed class AgentPullRequestsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WhatTheAgentSaidIsAskedAboutOnlyWhenItHoldsALinkAndNoPullRequestIsWatchedAsync()
    {
        var mentions = new FakeMentions { Mentioned = new MentionedPullRequest(4, new Uri("https://forge.example/o/r/pulls/4")) };
        var board = new Avala.Workbench.Board.JobBoard();
        var said = Job("Opened https://forge.example/o/r/pulls/4.");
        var plain = Job("Done, nothing else.");
        var watched = Job("Opened https://forge.example/o/r/pulls/4.") with
        {
            PullRequest = new Forges.Contracts.PullRequestWatchState(JobId.New(), new Forges.Contracts.ForgeName("local"), new Forges.Contracts.PullRequestRef(4, new Uri("https://forge.example/o/r/pulls/4"), "avala/4", "main"), Forges.Contracts.OnPullRequest.WatchOnly, 3),
        };
        board.Publish(System.Collections.Immutable.ImmutableDictionary<JobId, Avala.Workbench.Board.BoardJob>.Empty.Add(said.Job, said).Add(plain.Job, plain).Add(watched.Job, watched));
        var pullRequests = new AgentPullRequests(board, mentions);

        var found = await pullRequests.OfAsync(said.Job, Cancellation);
        var none = (await pullRequests.OfAsync(plain.Job, Cancellation), await pullRequests.OfAsync(watched.Job, Cancellation), await pullRequests.OfAsync(Option<JobId>.None, Cancellation));

        Assert.Equal(4, Outcomes.Present(found).Number);
        Assert.Equal((false, false, false), (none.Item1.IsSome, none.Item2.IsSome, none.Item3.IsSome));
        Assert.Equal(["Opened https://forge.example/o/r/pulls/4."], mentions.Read);
    }

    private static Avala.Workbench.Board.BoardJob Job(string reply)
    {
        var session = Avala.Agents.Contracts.Sessions.SessionId.New();
        var turn = Avala.Agents.Contracts.Sessions.TurnId.New();
        var transcript = Avala.Workbench.Timeline.Transcript.Empty
            .Apply(new Avala.Agents.Contracts.Events.TurnStarted(session, turn), Now)
            .Apply(new Avala.Agents.Contracts.Events.ItemStarted(session, turn, new Avala.Agents.Contracts.Sessions.ItemId("reply"), Avala.Agents.Contracts.Events.ItemKind.Message, "Reply"), Now)
            .Apply(new Avala.Agents.Contracts.Events.ItemProgressed(session, turn, new Avala.Agents.Contracts.Sessions.ItemId("reply"), reply), Now);

        return new Avala.Workbench.Board.BoardJob(new FakeCatalog().Add("Open a pull request").Summary, transcript);
    }
}
