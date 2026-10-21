using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Overview;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Tests.Overview;

public sealed class AgentTreeAdversaryScripts : IDisposable
{
    private static SessionId Session => TreeBench.Session;

    private static TurnId Turn => TreeBench.Turn;

    private static readonly string[] Reviewed = ["Write the changelog", "Bump the version", "Tag the release"];

    private readonly TreeBench bench = new();

    [Fact]
    public async Task TheOverviewHeaderCountsWhoNeedsYouAsTheTreeUnderItDoesAsync()
    {
        bench.Starts(bench.Endpoint, "install", "Install the packages");
        bench.Asks(bench.Endpoint, "install", "npm install");
        bench.Starts(bench.Endpoint, "migrate", "Migrate the schema");
        bench.Asks(bench.Endpoint, "migrate", "dotnet ef database update");
        var read = await bench.ReadAsync();

        var orchestration = new OrchestrationViewModel(read, new CommunityToolkit.Mvvm.Input.RelayCommand<JobId>(_ => { }), bench.Actions());

        Assert.Equal(orchestration.Tree.NeedsYou, orchestration.NeedsYou);
    }

    [Fact]
    public async Task AnOrchestratorThatAsksAPersonItselfIsCountedAndListedAmongThoseWhoNeedYouAsync()
    {
        AsksItself(bench.Root, "npm publish");
        var tree = bench.Tree();

        tree.Show(await bench.ReadAsync(), Option<JobId>.None);

        Assert.Equal(SubagentState.Waiting, tree.Branches[0].Place.State);
        Assert.Contains(tree.Waiting, waiting => waiting.Job == bench.Root.Job);
    }

    [Fact]
    public async Task ASubagentWaitingOnAFormSaysWhatItAsksAndItsJobDoesNotReadWaitingAsync()
    {
        bench.Starts(bench.Endpoint, "choose", "Choose the refund policy");
        AsksAForm(bench.Endpoint, "choose", "Which refund policy?");
        var tree = bench.Tree();

        tree.Show(await bench.ReadAsync(), Option<JobId>.None);

        var agent = tree.Branches.OfType<ISubagentViewModel>().Single(shown => shown.Name == "Choose the refund policy");
        var endpoint = tree.Branches.OfType<IDelegationNodeViewModel>().Single(node => node.Title == "Refund API endpoint");
        Assert.Equal((SubagentState.Waiting, SubagentState.Working), (agent.Place.State, endpoint.Place.State));
    }

    [Fact]
    public async Task ACompanionOfAChildJobIsNotDrawnAsADelegatedChildAsync()
    {
        bench.Companion("Talk through the endpoint", bench.Endpoint.Job);
        var tree = bench.Tree();

        tree.Show(await bench.ReadAsync(), Option<JobId>.None);

        Assert.DoesNotContain(tree.Branches.OfType<IDelegationNodeViewModel>(), node => node.Title == "Talk through the endpoint");
    }

    [Fact]
    public async Task AWaitingGrandchildStaysInSightWhenItsSettledParentFoldsAmongTheSettledAsync()
    {
        var reviewed = Reviewed.Select(name => bench.Child(name, JobStatus.AwaitingReview, bench.Root.Job)).ToList();
        bench.Child("Answer the reviewer", JobStatus.NeedsHelp, reviewed[0].Job);
        var tree = bench.Tree();

        tree.Show(await bench.ReadAsync(), Option<JobId>.None);

        Assert.Contains(tree.Branches.OfType<IDelegationNodeViewModel>(), node => node.Title == "Answer the reviewer");
    }

    [Fact]
    public async Task TheOrchestratorCardCountsItsOwnSubagentsAmongThoseItWaitsOnAsync()
    {
        bench.Starts(bench.Root, "survey", "Survey the codebase");
        var tree = bench.Tree();

        tree.Show(await bench.ReadAsync(), Option<JobId>.None);

        var card = tree.Root.Match<OrchestratorCardViewModel?>(found => found, () => null);
        Assert.NotNull(card);
        Assert.EndsWith("waiting on 3 sub-agents", card.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReopeningThePaneBringsBackTheSameCardsWithTheirStateAsync()
    {
        using var pane = bench.Pane(bench.Endpoint.Job);
        await bench.Ui.InvokeAsync(() => bench.Messenger.Send(new DelegationPaneRequested(bench.Endpoint.Job)), TestContext.Current.CancellationToken);
        await bench.Ui.PresentedAsync(pane, () => pane.Shown.Jobs.Count() == 2, () => "no children");
        var row = await bench.Ui.ReadAsync(() => pane.Shown.Jobs.Single(node => node.Title == "Refund API endpoint"));
        await bench.Ui.InvokeAsync(() => row.ToggleCommand.Execute(null), TestContext.Current.CancellationToken);

        await bench.Ui.InvokeAsync(() => bench.Messenger.Send(new DelegationPaneRequested(bench.Endpoint.Job)), TestContext.Current.CancellationToken);
        await bench.Ui.PresentedAsync(pane, () => !pane.HasTree, () => "still shown");
        await bench.Ui.InvokeAsync(() => bench.Messenger.Send(new DelegationPaneRequested(bench.Endpoint.Job)), TestContext.Current.CancellationToken);
        await bench.Ui.PresentedAsync(pane, () => pane.Shown.Jobs.Count() == 2, () => "no children");

        var back = await bench.Ui.ReadAsync(() => pane.Shown.Jobs.Single(node => node.Title == "Refund API endpoint"));
        Assert.Same(row, back);
        Assert.True(back.IsOpen);
    }

    [Fact]
    public async Task TheWaitingListNamesAnOrchestratorThatAsksAPersonByItsTitleAsync()
    {
        AsksItself(bench.Root, "npm publish");
        var tree = bench.Tree();

        tree.Show(await bench.ReadAsync(), Option<JobId>.None);

        var root = tree.Waiting.Single(waiting => waiting.Job == bench.Root.Job);
        Assert.Equal("Ship the refunds flow", root.Name);
    }

    public void Dispose() => bench.Dispose();

    private void AsksItself(JobSummary job, string command)
    {
        var item = new ItemId("own-tool");
        Apply(job, new TurnStarted(Session, Turn));
        Apply(job, new ItemStarted(Session, Turn, item, ItemKind.Command, command));
        Apply(job, new PermissionRequested(Session, Turn, item, $"Run {command}", ItemKind.Command, command));
        var shown = bench.Board.Jobs[job.Job];
        var decision = new PolicyDecision(Session, Turn, item, Option<JobId>.None, ItemKind.Command, command, PolicyAnswer.Ask, Option<PolicyRule>.None, DecisionDelivery.LeftToHuman, DateTimeOffset.UnixEpoch);
        bench.Board.Publish(bench.Board.Jobs.SetItem(job.Job, shown with { Transcript = shown.Transcript.Apply(decision) }));
    }

    private void AsksAForm(JobSummary job, string subagent, string question)
    {
        var item = new ItemId($"{subagent}-question");
        var form = new AgentForm(FormPurpose.Question, question, string.Empty, [new FormField("policy", "Policy", question, FieldKind.FreeText, [])]);
        var shown = bench.Board.Jobs[job.Job];
        var started = shown.Transcript.Apply(new ItemStarted(Session, Turn, item, ItemKind.Other, question) { Parent = new ItemId(subagent) }, DateTimeOffset.UnixEpoch);
        var requested = started.Apply(new FormRequested(Session, Turn, item, form), DateTimeOffset.UnixEpoch);
        var decided = requested.Apply(new FormDecision(Session, Turn, item, Option<JobId>.None, form, Autonomy.Supervised, Option<FormAnswer>.None, [], DecisionDelivery.LeftToHuman, DateTimeOffset.UnixEpoch));
        bench.Board.Publish(bench.Board.Jobs.SetItem(job.Job, shown with { Transcript = decided }));
    }

    private void Apply(JobSummary job, IAgentEvent happened)
    {
        var shown = bench.Board.Jobs[job.Job];
        bench.Board.Publish(bench.Board.Jobs.SetItem(job.Job, shown with { Transcript = shown.Transcript.Apply(happened, DateTimeOffset.UnixEpoch) }));
    }
}
