using Avala.Agents.Contracts.Events;
using Avala.Testing;
using Avala.Workbench.Conversation;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class PlanViewModelScripts
{
    [Fact]
    public void APlanCountsItsDoneStepsAndIsReplacedInPlace() =>
        ViewModelScript.Given(new PlanViewModel(new PlanEntry("plan", [new("Find the rounding", PlanStepStatus.InProgress), new("Add tests", PlanStepStatus.Pending)])))
            .Then(plan => Assert.Equal("0 of 2", plan.Progress))
            .When(plan => plan.Update(new PlanEntry("plan", [new("Find the rounding", PlanStepStatus.Done), new("Add tests", PlanStepStatus.Done), new("Run the suite", PlanStepStatus.Pending)])))
            .ThenNotified(nameof(PlanViewModel.Steps), nameof(PlanViewModel.Progress))
            .Then(plan => Assert.Equal(("2 of 3", 3), (plan.Progress, plan.Steps.Count)));

    [Fact]
    public void AnEmptyPlanHasNothingDone() =>
        ViewModelScript.Given(new PlanViewModel(new PlanEntry("plan", [])))
            .Then(plan => Assert.Equal(("0 of 0", 0), (plan.Progress, plan.Steps.Count)));
}
