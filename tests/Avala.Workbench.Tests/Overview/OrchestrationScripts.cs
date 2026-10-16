using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Fleet;
using Avala.Workbench.Following;
using Avala.Workbench.Overview;
using Avala.Workbench.Spending;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Overview;

public sealed class OrchestrationViewModelScripts
{
    [Fact]
    public void AWorkingOrchestrationIsOpenWithItsTotalsAndAFinishedOneStartsCollapsed()
    {
        var working = new OrchestrationViewModel(Tree(JobStatus.Running, JobStatus.NeedsHelp), new RelayCommand<JobId>(_ => { }), Focusing.Actions());
        var finished = new OrchestrationViewModel(Tree(JobStatus.Approved, JobStatus.Approved), new RelayCommand<JobId>(_ => { }), Focusing.Actions());

        Assert.Equal((true, false, "2 jobs", "1 needs you", "1.2k tokens", "shop"), (working.IsActive, working.IsCollapsed, working.Size, working.NeedsYou, working.Tokens, working.Repository));
        Assert.Equal((false, true, string.Empty), (finished.IsActive, finished.IsCollapsed, finished.NeedsYou));
    }

    [Fact]
    public void ACollapseChosenByAPersonStaysWhenItsTreeRefreshes()
    {
        var tree = Tree(JobStatus.Running, JobStatus.Running);
        var orchestration = new OrchestrationViewModel(tree, new RelayCommand<JobId>(_ => { }), Focusing.Actions());

        orchestration.ToggleCommand.Execute(null);
        orchestration.Show(tree);

        Assert.True(orchestration.IsCollapsed);
    }

    private static DelegationTree Tree(JobStatus root, JobStatus child)
    {
        var parent = Pages.Summary("Migrate payments", root, "claude-work");
        var summary = Pages.Summary("Update call sites", child, "claude", parent.Job);
        var spend = new JobSpend(Option<SessionSeen>.None, new UsageSummary(new Avala.Agents.Contracts.Events.TokenUsage(1_200, 0, 0, 0, 0), [], 0, default, []), Option<Budgets.Contracts.BudgetCaps>.None, Option<Budgets.Contracts.BudgetCarve>.None);

        return new DelegationTree(parent, [new DelegationNode(summary, 1, Option<JobFact>.None, Option<Delegation.Contracts.DelegationRecord>.None, spend) { Seen = new BoardJob(summary, Transcript.Empty) }], []);
    }
}
