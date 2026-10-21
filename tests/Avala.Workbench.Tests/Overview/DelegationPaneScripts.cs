using Avala.Delegation.Contracts;
using System.Collections.Immutable;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts;
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
using Microsoft.Extensions.Logging.Abstractions;

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
        await bench.Ui.PresentedAsync(pane, () => Jobs(pane).Count == 2, () => $"{Jobs(pane).Count} children");

        var shown = await bench.Ui.ReadAsync(() => (
            pane.IsOpen,
            Root(pane)?.Title,
            Root(pane)?.IsViewing,
            Jobs(pane).Select(child => (child.Title, child.IsViewing)).ToList(),
            Agents(pane, bench.Root.Job).Select(agent => (agent.Name, agent.State)).ToList(),
            Agents(pane, bench.Endpoint.Job).Select(agent => (agent.Name, agent.Activity, agent.State)).ToList(),
            pane.Caption,
            pane.Footer));
        Assert.True(shown.Item1);
        Assert.Equal(("Ship the refunds flow", false), (shown.Item2, shown.Item3));
        Assert.Equal([("Refunds screen", false), ("Refund API endpoint", true)], shown.Item4);
        Assert.Equal([("Plan the work", SubagentState.Completed)], shown.Item5);
        Assert.Equal([("Find the refund call sites", "Reading tests/Payments", SubagentState.Working)], shown.Item6);
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
        await bench.Ui.PresentedAsync(pane, () => Jobs(pane).Count == 2, () => $"{Jobs(pane).Count} children");
        await bench.Ui.InvokeAsync(() => Jobs(pane)[1].OpenCommand.Execute(bench.Screen.Job), Cancellation);
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
        await bench.Ui.PresentedAsync(pane, () => Jobs(pane).Count == 2, () => $"{Jobs(pane).Count} children");
        var before = await bench.Ui.ReadAsync(() => (pane.Revision, Root(pane), Jobs(pane)));

        await bench.Ui.InvokeAsync(() => pane.OnRegionContextChanged(bench.Endpoint.Job), Cancellation);
        await bench.Ui.PresentedAsync(pane, () => pane.Revision > before.Revision, () => $"revision {pane.Revision}");

        var after = await bench.Ui.ReadAsync(() => (Root(pane), Jobs(pane)));
        Assert.Same(before.Item2, after.Item1);
        Assert.Equal(before.Item3, after.Item2, ReferenceEqualityComparer.Instance);
    }

    [Fact]
    public async Task ThePaneOpensByItselfWhenTheShownJobStartsASubagentAndAfterBeingClosedOnlyForTheNextOneAsync()
    {
        var alone = bench.Alone();
        using var pane = bench.Pane(alone.Job);
        var before = await bench.Ui.ReadAsync(() => pane.IsOpen);

        bench.Starts(alone, "survey", "Survey the greeting");
        await bench.Ui.PresentedAsync(pane, () => Agents(pane, alone.Job).Count == 1, () => $"open {pane.IsOpen}");
        await bench.Ui.InvokeAsync(pane.Close, Cancellation);
        var revision = await bench.Ui.ReadAsync(() => pane.Revision);
        bench.Starts(alone, "survey", "Survey the greeting");
        await bench.Ui.PresentedAsync(pane, () => pane.Revision > revision, () => $"revision {pane.Revision}");
        var reopened = await bench.Ui.ReadAsync(() => pane.IsOpen);
        bench.Starts(alone, "check", "Check the build");
        await bench.Ui.PresentedAsync(pane, () => Agents(pane, alone.Job).Count == 2, () => $"open {pane.IsOpen}");

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
        await bench.Ui.PresentedAsync(pane, () => Jobs(pane).Count == 2, () => $"{Jobs(pane).Count} children");
        var row = await bench.Ui.ReadAsync(() => Jobs(pane).Single(child => child.Title == "Refunds screen"));

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
        var composer = await bench.Ui.ReadAsync(() => Assert.IsType<Avala.Workbench.Conversation.ComposerViewModel>(row.Peek?.Composer));
        await bench.Ui.InvokeAsync(() => composer.Draft = "Keep the old endpoint too", Cancellation);
        var sending = await bench.Ui.ReadAsync(() => composer.SendCommand.ExecuteAsync(null));
        await sending;

        Assert.Equal((true, 0), before);
        Assert.Equal("Run pnpm test refunds", await bench.Ui.ReadAsync(() => row.Doing));
        Assert.Equal(bench.Screen.Job, composer.Job);
        Assert.Equal("Keep the old endpoint too", bench.Jobs.Messages[^1].Text);
    }

    [Fact]
    public async Task FoldingAPeekedChildStopsFollowingItAsync()
    {
        using var pane = bench.Pane(bench.Screen.Job);
        await bench.Ui.InvokeAsync(() => bench.Messenger.Send(new DelegationPaneRequested(bench.Screen.Job)), Cancellation);
        await bench.Ui.PresentedAsync(pane, () => Jobs(pane).Count == 2, () => $"{Jobs(pane).Count} children");
        var row = await bench.Ui.ReadAsync(() => Jobs(pane).Single(child => child.Title == "Refunds screen"));
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

    private static List<DelegationNodeViewModel> Jobs(DelegationPaneViewModel pane) => [.. pane.Shown.Jobs];

    private static OrchestratorCardViewModel? Root(DelegationPaneViewModel pane) => pane.Shown.Root.Match<OrchestratorCardViewModel?>(root => root, () => null);

    private static List<SubagentViewModel> Agents(DelegationPaneViewModel pane, JobId job) => [.. pane.Shown.Branches.OfType<SubagentViewModel>().Where(agent => agent.Job == job)];
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
        ViewModelScript.Given(Shown(Running))
            .Then(agent => Assert.Equal(("Find the refund call sites", "Reading tests/Payments", SubagentState.Working, "working"), (agent.Name, agent.Activity, agent.State, agent.StateText)));

    [Fact]
    public void AWorkingSubagentShowsTheCommandItRunsNowAndHowManyToolsAndTokensItHasUsedUntilItEnds()
    {
        var working = Running with { Steps = 3, Doing = "Run dotnet build", Tokens = 5_400 };

        ViewModelScript.Given(Shown(working))
            .Then(agent => Assert.Equal(("Run dotnet build", "3 tool uses · 5,400 tokens"), (agent.Activity, agent.Meter)));
        ViewModelScript.Given(Shown(working with { Outcome = ItemOutcome.Succeeded }))
            .Then(agent => Assert.Equal(("Reading tests/Payments", "3 tool uses · 5,400 tokens"), (agent.Activity, agent.Meter)));
    }

    [Fact]
    public void ASubagentOpensOntoTheToolsItRanNewestFirstAndStaysOpenWhenItsCardRefreshes()
    {
        var agent = Shown(Running with { Trail = ["Search RefundParams", "Read refund.go"] });
        agent.ToggleCommand.Execute(null);
        var opened = agent.Trail;

        Show(agent, Running with { Trail = ["Search RefundParams", "Read refund.go", "Run go test"], Doing = "Run go test" });

        Assert.Equal(["Read refund.go", "Search RefundParams"], opened);
        Assert.Equal((true, "Run go test"), (agent.IsOpen, agent.Trail[0]));
    }

    [Fact]
    public void ASubagentThatRanNoToolHasNothingToOpen() =>
        Assert.False(Shown(Running).ToggleCommand.CanExecute(null));

    [Theory]
    [InlineData(null, false, "working")]
    [InlineData(null, true, "waiting")]
    [InlineData(ItemOutcome.Succeeded, false, "completed")]
    [InlineData(ItemOutcome.Failed, false, "failed")]
    [InlineData(ItemOutcome.Expired, false, "stopped")]
    [InlineData(ItemOutcome.Cancelled, false, "stopped")]
    [InlineData(ItemOutcome.Abandoned, false, "lost")]
    public void ASubagentShowsItIsWorkingWaitingForYouOrHowItEnded(ItemOutcome? outcome, bool waiting, string state) =>
        ViewModelScript.Given(Shown(Running with { Outcome = outcome is { } ended ? ended : Option<ItemOutcome>.None }, waiting))
            .Then(agent => Assert.Equal(state, agent.StateText));

    private static SubagentViewModel Shown(ToolEntry entry, bool waiting = false)
    {
        var agent = new SubagentViewModel(new BranchPlaceViewModel(entry.Key, _ => { }), JobId.New());
        Show(agent, entry, waiting);

        return agent;
    }

    private static void Show(SubagentViewModel agent, ToolEntry entry, bool waiting = false) =>
        agent.Show(new AgentBranch(entry.Key, BranchKind.InProcess, AgentBranches.ToolState(entry, waiting), []) { Tool = entry }, 1, false);
}

internal sealed class TreeBench : IDisposable
{
    public static SessionId Session { get; } = SessionId.New();

    public static TurnId Turn { get; } = TurnId.New();

    private readonly TreeCatalog catalog = new();

    private readonly FakeUsage usage = new();

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

    public SessionBook Sessions { get; private set; } = new(new FakeUsage());

    public JobSummary Child(string instruction, JobStatus status, JobId parent, string connection = "claude-work")
    {
        var child = Pages.Summary(instruction, status, connection, parent);
        catalog.Jobs.Add(child);
        Board.Publish(Board.Jobs.Add(child.Job, new BoardJob(child, Transcript.Empty)));

        return child;
    }

    public Task RunsOnAsync(JobSummary job, string harness) => RunsOnAsync(job, new ProviderInfo(harness.ToLowerInvariant().Replace(' ', '-'), harness));

    public async Task RunsOnAsync(JobSummary job, ProviderInfo harness)
    {
        var session = SessionId.New();
        await Sessions.HandleAsync(new SessionOpened(session, harness, "/worktrees/1", job.Connection.Match(name => name, () => new Avala.Agents.Contracts.Connections.ConnectionName("claude-work"))), CancellationToken.None);
        await Sessions.HandleAsync(new JobSessionStarted(job.Job, session), CancellationToken.None);
    }

    public void Backgrounds(JobSummary job, string subagent, string id, string command)
    {
        Apply(job, new ItemStarted(Session, Turn, new ItemId(id), ItemKind.Command, command) { Parent = new ItemId(subagent) });
        Apply(job, new ItemBackgrounded(Session, Turn, new ItemId(id)));
    }

    public void Denies(JobSummary job, string id)
    {
        Apply(job, new PermissionRequested(Session, Turn, new ItemId(id), "Launch a sub-agent", ItemKind.Subagent, id));
        Apply(job, new PermissionResolved(Session, Turn, new ItemId(id), PermissionAnswer.Deny));
        Apply(job, new ItemCompleted(Session, Turn, new ItemId(id), ItemOutcome.Cancelled));
    }

    public void Forgets(JobSummary job)
    {
        catalog.Jobs.RemoveAll(found => found.Job == job.Job || found.Parent == Option<JobId>.Some(job.Job));
        Board.Publish(Board.Jobs.RemoveRange(Board.Jobs.Keys.Where(key => key == job.Job || Board.Jobs[key].Summary.Parent == Option<JobId>.Some(job.Job)).ToList()));
    }

    public void Ends(JobSummary job, string id, ItemOutcome outcome) => Apply(job, new ItemCompleted(Session, Turn, new ItemId(id), outcome));

    public void Asks(JobSummary job, string id, string command)
    {
        var tool = new ItemId($"{id}-tool");
        Apply(job, new ItemStarted(Session, Turn, tool, ItemKind.Command, command) { Parent = new ItemId(id) });
        Apply(job, new PermissionRequested(Session, Turn, tool, $"Run {command}", ItemKind.Command, command));
        Decide(job, new Avala.Permissions.Contracts.PolicyDecision(Session, Turn, tool, Option<JobId>.None, ItemKind.Command, command, Avala.Permissions.Contracts.PolicyAnswer.Ask, Option<Avala.Permissions.Contracts.PolicyRule>.None, Avala.Permissions.Contracts.DecisionDelivery.LeftToHuman, DateTimeOffset.UnixEpoch));
    }

    public JobSummary Companion(string instruction, JobId parent)
    {
        var companion = Pages.Summary(instruction, JobStatus.Running, "claude-work", parent) with { Companion = true };
        catalog.Jobs.Add(companion);
        Board.Publish(Board.Jobs.Add(companion.Job, new BoardJob(companion, Transcript.Empty)));

        return companion;
    }

    public RowActions Actions() => Focusing.Actions(new JobFocus(new TestRegions(), Messenger), Jobs);

    public int Refreshes { get; private set; }

    public SubagentTreeViewModel Tree(TimeProvider clock) => new(new CommunityToolkit.Mvvm.Input.RelayCommand<JobId>(job => Selected.Add(job)), Focusing.Actions(new JobFocus(new TestRegions(), Messenger), Jobs), clock, () => Refreshes++);

    public SubagentTreeViewModel Tree() => Tree(TimeProvider.System);

    public async Task<DelegationTree> ReadAsync() => Outcomes.Present(await Reader().TreeAsync(Root.Job, CancellationToken.None));

    private void Apply(JobSummary job, IAgentEvent happened)
    {
        var shown = Board.Jobs[job.Job];
        Board.Publish(Board.Jobs.SetItem(job.Job, shown with { Transcript = shown.Transcript.Apply(happened, DateTimeOffset.UnixEpoch) }));
    }

    private void Decide(JobSummary job, Avala.Permissions.Contracts.PolicyDecision decision)
    {
        var shown = Board.Jobs[job.Job];
        Board.Publish(Board.Jobs.SetItem(job.Job, shown with { Transcript = shown.Transcript.Apply(decision) }));
    }

    public DelegationPaneViewModel Pane(JobId shown)
    {
        var pane = new DelegationPaneViewModel(Reader(), new LiveFeed(new Pulse(Board), Ui, NullLogger<LiveFeed>.Instance), Focusing.Actions(new JobFocus(new TestRegions(), Messenger), Jobs), Messenger);
        pane.OnRegionContextChanged(shown);
        pane.Activate();

        return pane;
    }

    public DelegationToggleViewModel Toggle() => new(Reader(), new LiveFeed(new Pulse(Board), Ui, NullLogger<LiveFeed>.Instance), Messenger);

    public DelegationViewModel Page()
    {
        var page = new DelegationViewModel(Reader(), new LiveFeed(new Pulse(Board), Ui, NullLogger<LiveFeed>.Instance), Focusing.Actions(new JobFocus(new TestRegions(), Messenger), Jobs));
        page.Activate();

        return page;
    }

    public JobSummary Orchestration(string instruction)
    {
        var root = Pages.Summary(instruction, JobStatus.Running, "claude-work");
        var child = Pages.Summary($"{instruction}: the child", JobStatus.Running, "claude-work", root.Job);
        catalog.Jobs.AddRange([root, child]);
        Board.Publish(Board.Jobs.Add(root.Job, new BoardJob(root, Transcript.Empty)).Add(child.Job, new BoardJob(child, Transcript.Empty)));

        return root;
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
        return new DelegationReader(catalog, new FakeDelegations(), Focusing.Spending(usage, Sessions), Board);
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
