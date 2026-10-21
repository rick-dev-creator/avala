using Avala.Agents.Contracts.Events;
using Avala.Components.Graphs;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Overview;

namespace Avala.Workbench.Tests.Overview;

public sealed class SubagentTreeViewModelScripts : IDisposable
{
    private readonly TreeBench bench = new();

    [Fact]
    public async Task EverySubagentHangsUnderTheJobThatStartedItWaitingFirstAndSettledLastAsync()
    {
        var tree = bench.Tree();

        tree.Show(await bench.ReadAsync(), Option<JobId>.None);

        Assert.Equal(
            [("Ship the refunds flow", 0), ("Refunds screen", 1), ("Refund API endpoint", 1), ("Find the refund call sites", 2), ("Plan the work", 1)],
            tree.Branches.Select(branch => (Name(branch), branch.Place.Depth)));
    }

    [Fact]
    public async Task EachBranchSaysWhetherItIsTheHarnessesOwnSubagentAChildJobOrAChildOnAnotherHarnessAsync()
    {
        var codex = bench.Child("Rewrite the webhook tests", JobStatus.Running, bench.Root.Job, "codex-pro");
        await bench.RunsOnAsync(bench.Root, "Claude Code");
        await bench.RunsOnAsync(bench.Endpoint, "Claude Code");
        await bench.RunsOnAsync(codex, "Codex");
        var tree = bench.Tree();

        tree.Show(await bench.ReadAsync(), Option<JobId>.None);

        Assert.Equal(
            [("Ship the refunds flow", "orchestrator"), ("Refunds screen", "child job"), ("Refund API endpoint", "child job"), ("Find the refund call sites", "sub-agent"), ("Plan the work", "sub-agent"), ("Rewrite the webhook tests", "cross-harness")],
            tree.Branches.Where(branch => branch is not IFoldedBranchesViewModel).Select(branch => (Name(branch), branch.Place.KindText)).OrderBy(pair => pair.Item1 == "Rewrite the webhook tests"));
    }

    [Fact]
    public async Task EachBranchsStateAndEdgeFollowItsSubagentsLifecycleAsync()
    {
        foreach (var (id, outcome) in new[] { ("lost", ItemOutcome.Abandoned), ("failed", ItemOutcome.Failed) })
        {
            bench.Starts(bench.Endpoint, id, $"The {id} one");
            bench.Ends(bench.Endpoint, id, outcome);
        }

        bench.Starts(bench.Endpoint, "asking", "The asking one");
        bench.Asks(bench.Endpoint, "asking", "npm install");
        var tree = bench.Tree();

        tree.Show(await bench.ReadAsync(), Option<JobId>.None);

        Assert.Equal(
            [
                ("Refunds screen", SubagentState.Waiting, EdgeKind.Attention),
                ("Refund API endpoint", SubagentState.Working, EdgeKind.Flowing),
                ("The asking one", SubagentState.Waiting, EdgeKind.Attention),
                ("The lost one", SubagentState.Lost, EdgeKind.Failure),
                ("The failed one", SubagentState.Failed, EdgeKind.Failure),
                ("Find the refund call sites", SubagentState.Working, EdgeKind.Flowing),
                ("Plan the work", SubagentState.Completed, EdgeKind.Quiet),
            ],
            tree.Branches.Skip(1).Where(branch => branch.Place.Depth > 0 && branch is not IFoldedBranchesViewModel).Select(branch => (Name(branch), branch.Place.State, branch.Place.Edge)));
    }

    [Fact]
    public async Task ASubagentAppearsWorkingWhenItStartsAndTheSameCardReadsCompletedOnceItEndsAsync()
    {
        var tree = bench.Tree();
        tree.Show(await bench.ReadAsync(), Option<JobId>.None);
        var before = tree.Branches.Count;

        bench.Starts(bench.Endpoint, "check", "Check the build");
        tree.Show(await bench.ReadAsync(), Option<JobId>.None);
        var started = Assert.Single(tree.Branches.OfType<ISubagentViewModel>(), agent => agent.Name == "Check the build");
        var working = (tree.Branches.Count, started.Place.State, started.Place.Depth);
        bench.Ends(bench.Endpoint, "check", ItemOutcome.Succeeded);
        tree.Show(await bench.ReadAsync(), Option<JobId>.None);

        Assert.Equal((before + 1, SubagentState.Working, 2), working);
        Assert.Same(started, Assert.Single(tree.Branches.OfType<ISubagentViewModel>(), agent => agent.Name == "Check the build"));
        Assert.Equal(SubagentState.Completed, started.Place.State);
    }

    [Fact]
    public async Task AWaitingSubagentSaysWhatItAsksAndALostOneSaysItsWorkWasLostAsync()
    {
        bench.Starts(bench.Endpoint, "asking", "Install the packages");
        bench.Asks(bench.Endpoint, "asking", "npm install");
        bench.Starts(bench.Endpoint, "lost", "Survey the greeting");
        bench.Ends(bench.Endpoint, "lost", ItemOutcome.Abandoned);
        var tree = bench.Tree();

        tree.Show(await bench.ReadAsync(), Option<JobId>.None);

        Assert.Equal(
            [("Install the packages", "asks: npm install"), ("Survey the greeting", "its work was lost when its turn or session ended"), ("Find the refund call sites", string.Empty), ("Plan the work", string.Empty)],
            tree.Branches.OfType<ISubagentViewModel>().Select(agent => (agent.Name, agent.Note)));
    }

    [Fact]
    public async Task MoreThanThreeSettledBranchesFoldIntoACountThatOpensOntoThemAndStaysOpenAcrossARefreshAsync()
    {
        foreach (var name in new[] { "one", "two", "three", "four" })
        {
            bench.Starts(bench.Endpoint, name, $"Check {name}");
            bench.Ends(bench.Endpoint, name, name == "four" ? ItemOutcome.Cancelled : ItemOutcome.Succeeded);
        }

        var tree = bench.Tree();
        tree.Show(await bench.ReadAsync(), Option<JobId>.None);
        var folded = tree.Branches.Select(Name).ToList();

        Assert.Single(tree.Branches.OfType<IFoldedBranchesViewModel>()).Place.FoldCommand.Execute(null);
        tree.Show(await bench.ReadAsync(), Option<JobId>.None);

        Assert.Equal(["Ship the refunds flow", "Refunds screen", "Refund API endpoint", "Find the refund call sites", "3 completed · 1 stopped", "Plan the work"], folded);
        Assert.Equal(
            [("3 completed · 1 stopped", 2), ("Check one", 2), ("Check two", 2), ("Check three", 2), ("Check four", 2)],
            tree.Branches.Skip(4).Take(5).Select(branch => (Name(branch), branch.Place.Depth)));
    }

    [Fact]
    public async Task FoldingACardHidesItsWholeBranchAndShowsHowManyItHoldsUntilItIsUnfoldedAsync()
    {
        var tree = bench.Tree();
        tree.Show(await bench.ReadAsync(), Option<JobId>.None);
        var endpoint = tree.Branches.OfType<IDelegationNodeViewModel>().Single(node => node.Title == "Refund API endpoint");

        endpoint.Place.FoldCommand.Execute(null);
        var folded = (tree.Branches.Select(Name).ToList(), endpoint.Place.IsFolded, endpoint.Place.Branches);
        endpoint.Place.FoldCommand.Execute(null);

        Assert.Equal(["Ship the refunds flow", "Refunds screen", "Refund API endpoint", "Plan the work"], folded.Item1);
        Assert.Equal((true, 1), (folded.IsFolded, folded.Branches));
        Assert.Contains("Find the refund call sites", tree.Branches.Select(Name));
    }

    [Fact]
    public async Task UnfoldingACardBringsBackTheSameBranchesAsTheyWereLeftAsync()
    {
        bench.Starts(bench.Endpoint, "asking", "Install the packages");
        bench.Asks(bench.Endpoint, "asking", "npm install");
        var tree = bench.Tree();
        tree.Show(await bench.ReadAsync(), Option<JobId>.None);
        var endpoint = tree.Branches.OfType<IDelegationNodeViewModel>().Single(node => node.Title == "Refund API endpoint");
        var agent = tree.Branches.OfType<ISubagentViewModel>().Single(shown => shown.Name == "Install the packages");
        agent.ToggleCommand.Execute(null);

        endpoint.Place.FoldCommand.Execute(null);
        endpoint.Place.FoldCommand.Execute(null);

        var back = tree.Branches.OfType<ISubagentViewModel>().Single(shown => shown.Name == "Install the packages");
        Assert.Same(agent, back);
        Assert.True(back.IsOpen);
    }

    [Fact]
    public async Task TheHeaderCountsTheAgentsAtWorkAndThoseThatNeedYouAndChoosingOneOpensItsJobAsync()
    {
        bench.Starts(bench.Endpoint, "asking", "Install the packages");
        bench.Asks(bench.Endpoint, "asking", "npm install");
        var tree = bench.Tree();

        tree.Show(await bench.ReadAsync(), Option<JobId>.None);
        tree.Waiting[1].OpenCommand.Execute(tree.Waiting[1].Job);

        Assert.Equal(("5 agents", "2 working", "2 needs you"), (tree.Agents, tree.Working, tree.NeedsYou));
        Assert.Equal([("Refunds screen", bench.Screen.Job), ("Install the packages", bench.Endpoint.Job)], tree.Waiting.Select(waiting => (waiting.Name, waiting.Job)));
        Assert.Equal([bench.Endpoint.Job], bench.Selected);
    }

    [Fact]
    public async Task ASubagentsOwnBackgroundCommandHangsUnderItWorkingAfterTheSubagentEndsAndKeepsTheOrchestratorAtWorkUntilItEndsAsync()
    {
        bench.Starts(bench.Root, "nested", "Start the nested command");
        bench.Backgrounds(bench.Root, "nested", "sleep", "sleep 20 && echo nested-done");
        bench.Ends(bench.Root, "nested", ItemOutcome.Succeeded);
        var tree = bench.Tree();

        tree.Show(await bench.ReadAsync(), Option<JobId>.None);
        var running = Commands(tree);
        var atWork = Card(tree).Detail;
        bench.Ends(bench.Root, "sleep", ItemOutcome.Succeeded);
        tree.Show(await bench.ReadAsync(), Option<JobId>.None);

        Assert.Equal([("Start the nested command", 1, SubagentState.Completed), ("sleep 20 && echo nested-done", 2, SubagentState.Working)], running);
        Assert.Contains("waiting on 3 sub-agents", atWork, StringComparison.Ordinal);
        Assert.Equal([("Start the nested command", 1, SubagentState.Completed), ("sleep 20 && echo nested-done", 2, SubagentState.Completed)], Commands(tree));
    }

    public void Dispose() => bench.Dispose();

    private static List<(string, int, SubagentState)> Commands(SubagentTreeViewModel tree) =>
        [.. tree.Branches.OfType<ISubagentViewModel>().Where(agent => agent.Name is "Start the nested command" or "sleep 20 && echo nested-done").Select(agent => (agent.Name, agent.Place.Depth, agent.Place.State))];

    private static IOrchestratorCardViewModel Card(SubagentTreeViewModel tree) => tree.Branches.OfType<IOrchestratorCardViewModel>().Single();

    private static string Name(IAgentBranch branch) => branch switch
    {
        IOrchestratorCardViewModel card => card.Title,
        IDelegationNodeViewModel node => node.Title,
        ISubagentViewModel agent => agent.Name,
        IFoldedBranchesViewModel folded => folded.Text,
        _ => string.Empty,
    };
}

public sealed class BranchPlaceViewModelScripts
{
    [Theory]
    [InlineData(SubagentState.Working, "working", EdgeKind.Flowing, true, false, false, false)]
    [InlineData(SubagentState.Waiting, "waiting", EdgeKind.Attention, false, true, false, false)]
    [InlineData(SubagentState.Completed, "completed", EdgeKind.Quiet, false, false, false, true)]
    [InlineData(SubagentState.Stopped, "stopped", EdgeKind.Quiet, false, false, false, true)]
    [InlineData(SubagentState.Failed, "failed", EdgeKind.Failure, false, false, true, false)]
    [InlineData(SubagentState.Lost, "lost", EdgeKind.Failure, false, false, true, false)]
    public void APlaceReadsItsSubagentsStateAsAWordAnEdgeAndOneFlag(SubagentState state, string text, EdgeKind edge, bool working, bool waiting, bool alert, bool settled)
    {
        var place = new BranchPlaceViewModel("agent", _ => { });

        place.Show(2, BranchKind.CrossHarness, state, 3, false);

        Assert.Equal((text, edge, working, waiting, alert, settled), (place.StateText, place.Edge, place.IsWorking, place.IsWaiting, place.IsAlert, place.IsSettled));
        Assert.Equal((2, "cross-harness", 3, true), (place.Depth, place.KindText, place.Branches, place.HasBranches));
    }

    [Fact]
    public void FoldingAPlaceAsksItsTreeToFoldItByItsKey()
    {
        var folded = new List<string>();
        var place = new BranchPlaceViewModel("job/agent", folded.Add);

        place.FoldCommand.Execute(null);

        Assert.Equal(["job/agent"], folded);
    }
}

public sealed class FoldedBranchesViewModelScripts
{
    [Fact]
    public void TheSettledCountSaysHowManyCompletedAndHowManyWereStopped()
    {
        var folded = new FoldedBranchesViewModel(new BranchPlaceViewModel("root/settled", _ => { }));

        folded.Show(1, [Settled("a", SubagentState.Stopped), Settled("b", SubagentState.Completed), Settled("c", SubagentState.Completed)], true);

        Assert.Equal(("2 completed · 1 stopped", 3, true), (folded.Text, folded.Place.Branches, folded.Place.IsFolded));
    }

    private static AgentBranch Settled(string key, SubagentState state) => new(key, BranchKind.InProcess, state, []);
}

public sealed class WaitingAgentViewModelScripts
{
    [Fact]
    public void ChoosingAnAgentThatNeedsYouOpensItsJob()
    {
        var opened = new List<JobId>();
        var job = JobId.New();
        var waiting = new WaitingAgentViewModel(job, "Install the packages", "asks: npm install", new CommunityToolkit.Mvvm.Input.RelayCommand<JobId>(opened.Add));

        waiting.OpenCommand.Execute(waiting.Job);

        Assert.Equal([job], opened);
    }
}
