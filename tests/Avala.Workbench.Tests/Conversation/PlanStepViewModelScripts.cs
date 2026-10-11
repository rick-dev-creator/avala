using Avala.Agents.Contracts.Events;
using Avala.Testing;
using Avala.Workbench.Conversation;

namespace Avala.Workbench.Tests.Conversation;

public sealed class PlanStepViewModelScripts
{
    [Fact]
    public void EachStepKeepsItsTitleAndStatusInThePlansOrder() =>
        ViewModelScript.Given(PlanStepViewModel.Of([new("Read the code", PlanStepStatus.Done), new("Write the change", PlanStepStatus.InProgress), new("Run the tests", PlanStepStatus.Pending)]))
            .Then(steps => Assert.Equal(
                [("Read the code", PlanStepStatus.Done), ("Write the change", PlanStepStatus.InProgress), ("Run the tests", PlanStepStatus.Pending)],
                steps.Select(step => (step.Title, step.Status))));
}
