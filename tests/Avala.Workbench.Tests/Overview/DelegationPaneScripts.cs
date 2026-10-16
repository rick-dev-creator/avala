using System.Collections.Immutable;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Contracts.Presentation;
using Avala.Workbench.Fleet;
using Avala.Workbench.Following;
using Avala.Workbench.Navigation;
using Avala.Workbench.Overview;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Tests.Overview;

public sealed class DelegationPaneViewModelScripts : IDisposable
{
    private readonly TreeBench bench = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OpenedFromAChildThePaneShowsItsWholeTreeWithTheChildHighlightedAndEachJobsSubagentsAsync()
    {
        using var pane = bench.Pane(bench.Endpoint.Job);

        await bench.Ui.InvokeAsync(() => bench.Messenger.Send(new DelegationPaneRequested(bench.Endpoint.Job)), Cancellation);
        await bench.Ui.PresentedAsync(pane, () => pane.Children.Count == 2, () => $"{pane.Children.Count} children");

        var shown = await bench.Ui.ReadAsync(() => (
            pane.IsOpen,
            pane.Root?.Title,
            pane.Root?.IsViewing,
            pane.Children.Select(child => (child.Title, child.IsViewing)).ToList(),
            pane.Root?.Subagents.Select(agent => (agent.Name, agent.State)).ToList(),
            pane.Children.Single(child => child.Title == "Refund API endpoint").Subagents.Select(agent => (agent.Name, agent.Activity, agent.State)).ToList(),
            pane.Caption,
            pane.Footer));
        Assert.True(shown.Item1);
        Assert.Equal(("Ship the refunds flow", false), (shown.Item2, shown.Item3));
        Assert.Equal([("Refunds screen", false), ("Refund API endpoint", true)], shown.Item4);
        Assert.Equal([("Plan the work", SubagentState.Done)], shown.Item5);
        Assert.Equal([("Find the refund call sites", "Reading tests/Payments", SubagentState.Running)], shown.Item6);
        Assert.Equal(("3 jobs · 2 agents", "claude-work · 2 jobs   personal · 1 job"), (shown.Item7, shown.Item8));
        Assert.Equal([new DelegationPaneChanged(bench.Endpoint.Job, true)], bench.Changes);
    }

    [Fact]
    public async Task OnlyTheShownJobsRequestTogglesThePaneAndClickingAJobOpensItAsync()
    {
        using var pane = bench.Pane(bench.Endpoint.Job);

        await bench.Ui.InvokeAsync(() => bench.Messenger.Send(new DelegationPaneRequested(bench.Screen.Job)), Cancellation);
        var ignored = await bench.Ui.ReadAsync(() => pane.IsOpen);
        await bench.Ui.InvokeAsync(() => bench.Messenger.Send(new DelegationPaneRequested(bench.Endpoint.Job)), Cancellation);
        await bench.Ui.PresentedAsync(pane, () => pane.Children.Count == 2, () => $"{pane.Children.Count} children");
        await bench.Ui.InvokeAsync(() => pane.Children[1].OpenCommand.Execute(bench.Screen.Job), Cancellation);
        await bench.Ui.InvokeAsync(() => bench.Messenger.Send(new DelegationPaneRequested(bench.Endpoint.Job)), Cancellation);

        Assert.False(ignored);
        Assert.Equal([bench.Screen.Job], bench.Selected);
        Assert.False(await bench.Ui.ReadAsync(() => pane.IsOpen));
        Assert.Equal([new DelegationPaneChanged(bench.Endpoint.Job, true), new DelegationPaneChanged(bench.Endpoint.Job, false)], bench.Changes);
    }

    [Fact]
    public async Task ARefreshKeepsEveryCardSoTheOneUnderThePointerStaysPutAsync()
    {
        using var pane = bench.Pane(bench.Endpoint.Job);
        await bench.Ui.InvokeAsync(() => bench.Messenger.Send(new DelegationPaneRequested(bench.Endpoint.Job)), Cancellation);
        await bench.Ui.PresentedAsync(pane, () => pane.Children.Count == 2, () => $"{pane.Children.Count} children");
        var before = await bench.Ui.ReadAsync(() => (pane.Revision, pane.Root, pane.Children.ToList()));

        await bench.Ui.InvokeAsync(() => pane.OnRegionContextChanged(bench.Endpoint.Job), Cancellation);
        await bench.Ui.PresentedAsync(pane, () => pane.Revision > before.Revision, () => $"revision {pane.Revision}");

        var after = await bench.Ui.ReadAsync(() => (pane.Root, pane.Children.ToList()));
        Assert.Same(before.Root, after.Root);
        Assert.Equal(before.Item3, after.Item2, ReferenceEqualityComparer.Instance);
    }

    [Fact]
    public async Task ThePaneOpensByItselfWhenTheShownJobStartsASubagentAndAfterBeingClosedOnlyForTheNextOneAsync()
    {
        var alone = bench.Alone();
        using var pane = bench.Pane(alone.Job);
        var before = await bench.Ui.ReadAsync(() => pane.IsOpen);

        bench.Starts(alone, "survey", "Survey the greeting");
        await bench.Ui.PresentedAsync(pane, () => pane.Root?.Subagents.Count == 1, () => $"open {pane.IsOpen}");
        await bench.Ui.InvokeAsync(pane.Close, Cancellation);
        var revision = await bench.Ui.ReadAsync(() => pane.Revision);
        bench.Starts(alone, "survey", "Survey the greeting");
        await bench.Ui.PresentedAsync(pane, () => pane.Revision > revision, () => $"revision {pane.Revision}");
        var reopened = await bench.Ui.ReadAsync(() => pane.IsOpen);
        bench.Starts(alone, "check", "Check the build");
        await bench.Ui.PresentedAsync(pane, () => pane.Root?.Subagents.Count == 2, () => $"open {pane.IsOpen}");

        Assert.False(before);
        Assert.False(reopened);
        Assert.Equal(
            [new DelegationPaneChanged(alone.Job, true), new DelegationPaneChanged(alone.Job, false), new DelegationPaneChanged(alone.Job, true)],
            bench.Changes);
    }

    [Fact]
    public async Task MovingToAJobOutsideAnyTreeClosesThePaneAsync()
    {
        var alone = bench.Alone();
        using var pane = bench.Pane(bench.Endpoint.Job);
        await bench.Ui.InvokeAsync(() => bench.Messenger.Send(new DelegationPaneRequested(bench.Endpoint.Job)), Cancellation);

        await bench.Ui.InvokeAsync(() => pane.OnRegionContextChanged(alone.Job), Cancellation);

        Assert.False(await bench.Ui.ReadAsync(() => pane.IsOpen));
    }

    [Fact]
    public async Task PeekingAtAnOpenChildFollowsItsLatestRowsAndSendsItAMessageAsync()
    {
        using var pane = bench.Pane(bench.Screen.Job);
        await bench.Ui.InvokeAsync(() => bench.Messenger.Send(new DelegationPaneRequested(bench.Screen.Job)), Cancellation);
        await bench.Ui.PresentedAsync(pane, () => pane.Children.Count == 2, () => $"{pane.Children.Count} children");
        var row = await bench.Ui.ReadAsync(() => pane.Children.OfType<DelegationNodeViewModel>().Single(child => child.Title == "Refunds screen"));

        await bench.Ui.InvokeAsync(
            () =>
            {
                row.ToggleCommand.Execute(null);
                row.PeekNowCommand.Execute(null);
            },
            Cancellation);
        var before = await bench.Ui.ReadAsync(() => (row.IsPeeking, row.Peek?.Latest.Count));
        bench.Runs(bench.Screen, "test", "pnpm test refunds");
        await bench.Ui.PresentedAsync(pane, () => row.Peek?.Latest.Count > before.Item2, () => $"{row.Peek?.Latest.Count} rows");
        await bench.Ui.InvokeAsync(() => row.Peek!.Composer.Draft = "Keep the old endpoint too", Cancellation);
        var sending = await bench.Ui.ReadAsync(() => ((Avala.Workbench.Conversation.ComposerViewModel)row.Peek!.Composer).SendCommand.ExecuteAsync(null));
        await sending;

        Assert.Equal((true, 0), before);
        Assert.Equal("Run pnpm test refunds", await bench.Ui.ReadAsync(() => row.Doing));
        Assert.Equal(bench.Screen.Job, ((Avala.Workbench.Conversation.ComposerViewModel)row.Peek!.Composer).Job);
        Assert.Equal("Keep the old endpoint too", bench.Jobs.Messages[^1].Text);
    }

    [Fact]
    public async Task FoldingAPeekedChildStopsFollowingItAsync()
    {
        using var pane = bench.Pane(bench.Screen.Job);
        await bench.Ui.InvokeAsync(() => bench.Messenger.Send(new DelegationPaneRequested(bench.Screen.Job)), Cancellation);
        await bench.Ui.PresentedAsync(pane, () => pane.Children.Count == 2, () => $"{pane.Children.Count} children");
        var row = await bench.Ui.ReadAsync(() => pane.Children.OfType<DelegationNodeViewModel>().Single(child => child.Title == "Refunds screen"));
        await bench.Ui.InvokeAsync(
            () =>
            {
                row.ToggleCommand.Execute(null);
                row.PeekNowCommand.Execute(null);
            },
            Cancellation);

        await bench.Ui.InvokeAsync(() => row.ToggleCommand.Execute(null), Cancellation);
        bench.Runs(bench.Screen, "test", "pnpm test refunds");
        await bench.Ui.PresentedAsync(pane, () => row.Doing == "Run pnpm test refunds", () => row.Doing);

        Assert.Equal((false, false, true), await bench.Ui.ReadAsync(() => (row.IsOpen, row.IsPeeking, row.Peek is null)));
    }

    public void Dispose() => bench.Dispose();
}

public sealed class DelegationToggleViewModelScripts : IDisposable
{
    private readonly TreeBench bench = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("root", true)]
    [InlineData("child", true)]
    [InlineData("alone", false)]
    public async Task TheHeaderOffersTheTreeOnlyToAJobThatIsPartOfOneAsync(string which, bool expected)
    {
        var alone = bench.Alone();
        var shown = which switch { "root" => bench.Root.Job, "child" => bench.Screen.Job, _ => alone.Job };
        using var toggle = bench.Toggle();
        toggle.Activate();
        toggle.OnRegionContextChanged(shown);

        await bench.Ui.PresentedAsync(toggle, () => toggle.Revision > 0, () => toggle.IsShown.ToString());

        Assert.Equal(expected, await bench.Ui.ReadAsync(() => toggle.IsShown));
    }

    [Fact]
    public async Task TheHeaderButtonAsksForThePaneAndShowsItOpenAsync()
    {
        using var toggle = bench.Toggle();
        using var pane = bench.Pane(bench.Endpoint.Job);
        toggle.OnRegionContextChanged(bench.Endpoint.Job);

        await bench.Ui.InvokeAsync(() => toggle.ToggleCommand.Execute(null), Cancellation);

        Assert.Equal((true, true), await bench.Ui.ReadAsync(() => (pane.IsOpen, toggle.IsOpen)));
    }

    public void Dispose() => bench.Dispose();
}

public sealed class SubagentViewModelScripts
{
    private static readonly ToolEntry Running = new("item:explore", ItemKind.Subagent, "Subagent: Find the refund call sites", "Reading tests/\n\nReading tests/Payments", Option<ItemOutcome>.None);

    [Fact]
    public void ASubagentSaysWhatItIsAndTheLastThingItReported() =>
        ViewModelScript.Given(new SubagentViewModel(Running, waiting: false))
            .Then(agent => Assert.Equal(("Find the refund call sites", "Reading tests/Payments", SubagentState.Running, "running"), (agent.Name, agent.Activity, agent.State, agent.StateText)));

    [Fact]
    public void AWorkingSubagentShowsTheCommandItRunsNowAndHowManyToolsAndTokensItHasUsedUntilItEnds()
    {
        var working = Running with { Steps = 3, Doing = "Run dotnet build", Tokens = 5_400 };

        ViewModelScript.Given(new SubagentViewModel(working, waiting: false))
            .Then(agent => Assert.Equal(("Run dotnet build", "3 tool uses · 5,400 tokens"), (agent.Activity, agent.Meter)));
        ViewModelScript.Given(new SubagentViewModel(working with { Outcome = ItemOutcome.Succeeded }, waiting: false))
            .Then(agent => Assert.Equal(("Reading tests/Payments", "3 tool uses · 5,400 tokens"), (agent.Activity, agent.Meter)));
    }

    [Fact]
    public void ASubagentOpensOntoTheToolsItRanNewestFirstAndStaysOpenWhenItsRowRefreshes()
    {
        var agent = new SubagentViewModel(Running with { Trail = ["Search RefundParams", "Read refund.go"] }, waiting: false);
        agent.ToggleCommand.Execute(null);

        var refreshed = SubagentViewModel.Kept([agent], [new SubagentViewModel(Running with { Trail = ["Search RefundParams", "Read refund.go", "Run go test"], Doing = "Run go test" }, waiting: false)]);

        Assert.True(agent.IsOpen);
        Assert.Equal(["Read refund.go", "Search RefundParams"], agent.Trail);
        Assert.True(Assert.Single(refreshed).IsOpen);
    }

    [Fact]
    public void ASubagentThatRanNoToolHasNothingToOpen() =>
        Assert.False(new SubagentViewModel(Running, waiting: false).ToggleCommand.CanExecute(null));

    [Theory]
    [InlineData(null, true, "waiting")]
    [InlineData(ItemOutcome.Succeeded, false, "done")]
    [InlineData(ItemOutcome.Failed, false, "failed")]
    public void ASubagentIsWaitingWhileItsJobAsksYouAndDoneOrFailedOnceItEnds(ItemOutcome? outcome, bool waiting, string state) =>
        ViewModelScript.Given(new SubagentViewModel(Running with { Outcome = outcome is { } ended ? ended : Option<ItemOutcome>.None }, waiting))
            .Then(agent => Assert.Equal(state, agent.StateText));
}

internal sealed class TreeBench : IDisposable
{
    private static readonly SessionId Session = SessionId.New();

    private static readonly TurnId Turn = TurnId.New();

    private readonly TreeCatalog catalog = new();

    public TreeBench()
    {
        Root = Pages.Summary("Ship the refunds flow", JobStatus.Running, "claude-work");
        Endpoint = Pages.Summary("Refund API endpoint", JobStatus.Running, "claude-work", Root.Job);
        Screen = Pages.Summary("Refunds screen", JobStatus.NeedsHelp, "personal", Root.Job);
        catalog.Jobs.AddRange([Root, Endpoint, Screen]);
        Board.Publish(ImmutableDictionary<JobId, BoardJob>.Empty
            .Add(Root.Job, new BoardJob(Root, Ran(Agent("plan", "Plan the work", "Split it into three jobs", ItemOutcome.Succeeded))))
            .Add(Endpoint.Job, new BoardJob(Endpoint, Ran(Agent("explore", "Find the refund call sites", "Reading tests/Payments", null))))
            .Add(Screen.Job, new BoardJob(Screen, Transcript.Empty)));
        Messenger.Register<JobSelected>(this, (_, message) => Selected.Add(message.Job));
        Messenger.Register<DelegationPaneChanged>(this, (_, message) => Changes.Add(message));
    }

    public TestUiDispatcher Ui { get; } = new();

    public JobBoard Board { get; } = new();

    public StrongReferenceMessenger Messenger { get; } = new();

    public List<JobId> Selected { get; } = [];

    public List<DelegationPaneChanged> Changes { get; } = [];

    public JobSummary Root { get; }

    public JobSummary Endpoint { get; }

    public JobSummary Screen { get; }

    public JobSummary Alone()
    {
        var alone = Pages.Summary("Fix the README", JobStatus.Running);
        catalog.Jobs.Add(alone);
        Board.Publish(Board.Jobs.Add(alone.Job, new BoardJob(alone, Transcript.Empty)));

        return alone;
    }

    public void Runs(JobSummary job, string id, string command)
    {
        var shown = Board.Jobs[job.Job];
        var ran = shown.Transcript.Apply(new TurnStarted(Session, Turn), DateTimeOffset.UnixEpoch).Apply(new ItemStarted(Session, Turn, new ItemId(id), ItemKind.Command, command), DateTimeOffset.UnixEpoch);
        Board.Publish(Board.Jobs.SetItem(job.Job, shown with { Transcript = ran }));
    }

    public void Starts(JobSummary job, string id, string name)
    {
        var shown = Board.Jobs[job.Job];
        var started = Agent(id, name, $"Working on {name}", null).Aggregate(shown.Transcript.Apply(new TurnStarted(Session, Turn), DateTimeOffset.UnixEpoch), (current, next) => current.Apply(next, DateTimeOffset.UnixEpoch));
        Board.Publish(Board.Jobs.SetItem(job.Job, shown with { Transcript = started }));
    }

    public FakeJobs Jobs { get; } = new() { Status = JobStatus.NeedsHelp };

    public DelegationPaneViewModel Pane(JobId shown)
    {
        var pane = new DelegationPaneViewModel(Reader(), new LiveFeed(new Pulse(Board), Ui), Focusing.Actions(new JobFocus(new TestRegions(), Messenger), Jobs), Messenger);
        pane.OnRegionContextChanged(shown);
        pane.Activate();

        return pane;
    }

    public DelegationToggleViewModel Toggle() => new(Reader(), new LiveFeed(new Pulse(Board), Ui), Messenger);

    public DelegationViewModel Page()
    {
        var page = new DelegationViewModel(Reader(), new LiveFeed(new Pulse(Board), Ui), Focusing.Actions(new JobFocus(new TestRegions(), Messenger), Jobs));
        page.Activate();

        return page;
    }

    public JobSummary Finished()
    {
        var root = Pages.Summary("Write the release notes", JobStatus.Approved, "claude") with { Repository = "/repositories/website", Submitted = DateTimeOffset.UnixEpoch.AddDays(-1) };
        var child = Pages.Summary("Write the to-do list", JobStatus.Approved, "claude", root.Job) with { Repository = "/repositories/website" };
        catalog.Jobs.AddRange([root, child]);
        Board.Publish(Board.Jobs.Add(root.Job, new BoardJob(root, Transcript.Empty)).Add(child.Job, new BoardJob(child, Transcript.Empty)));

        return root;
    }

    public void Dispose() => Ui.Dispose();

    private DelegationReader Reader()
    {
        var usage = new FakeUsage();

        return new DelegationReader(catalog, new FakeDelegations(), Focusing.Spending(usage, new SessionBook(usage)), Board);
    }

    private static IAgentEvent[] Agent(string id, string name, string activity, ItemOutcome? outcome) =>
    [
        new ItemStarted(Session, Turn, new ItemId(id), ItemKind.Subagent, $"Subagent: {name}"),
        new ItemProgressed(Session, Turn, new ItemId(id), activity),
        .. outcome is { } ended ? new IAgentEvent[] { new ItemCompleted(Session, Turn, new ItemId(id), ended) } : [],
    ];

    private static Transcript Ran(IAgentEvent[] events) =>
        events.Prepend(new TurnStarted(Session, Turn)).Aggregate(Transcript.Empty, (current, next) => current.Apply(next, DateTimeOffset.UnixEpoch));
}
