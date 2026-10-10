using Avala.Agents.Contracts.Events;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Conversation;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class PlanPanelViewModelScripts
{
    private static readonly PlanEntry Halfway = new("plan", [new("Read the code", PlanStepStatus.Done), new("Write the change", PlanStepStatus.InProgress), new("Run the tests", PlanStepStatus.Pending)]);

    [Fact]
    public void WithoutAPlanThePanelIsHiddenAndCannotOpen() =>
        ViewModelScript.Given(new PlanPanelViewModel())
            .When(panel => panel.Show(Option<PlanEntry>.None))
            .Then(panel => Assert.Equal((false, false, string.Empty), (panel.IsShown, panel.ToggleCommand.CanExecute(null), panel.Progress)));

    [Fact]
    public void APlanWithStepsLeftShowsItsProgressAndTheStepUnderWay() =>
        ViewModelScript.Given(new PlanPanelViewModel())
            .When(panel => panel.Show(Halfway))
            .ThenNotified(nameof(PlanPanelViewModel.IsShown), nameof(PlanPanelViewModel.Progress), nameof(PlanPanelViewModel.Current), nameof(PlanPanelViewModel.Steps))
            .Then(panel => Assert.Equal((true, "1 of 3", "Write the change", 3), (panel.IsShown, panel.Progress, panel.Current, panel.Steps.Count)));

    [Fact]
    public void WithNoStepUnderWayTheNextPendingStepIsTheCurrentOne() =>
        ViewModelScript.Given(new PlanPanelViewModel())
            .When(panel => panel.Show(new PlanEntry("plan", [new("Read the code", PlanStepStatus.Done), new("Run the tests", PlanStepStatus.Pending)])))
            .Then(panel => Assert.Equal("Run the tests", panel.Current));

    [Fact]
    public void TogglingOpensAndClosesTheListOfSteps() =>
        ViewModelScript.Given(new PlanPanelViewModel())
            .When(panel => panel.Show(Halfway))
            .When(panel => panel.ToggleCommand.Execute(null))
            .ThenNotified(nameof(PlanPanelViewModel.IsExpanded))
            .Then(panel => Assert.True(panel.IsExpanded))
            .When(panel => panel.ToggleCommand.Execute(null))
            .Then(panel => Assert.False(panel.IsExpanded));

    [Theory]
    [InlineData(PlanStepStatus.Done)]
    [InlineData(null)]
    public void APlanWhoseStepsAreAllDoneOrGoneHidesThePanelAndClosesIt(PlanStepStatus? last) =>
        ViewModelScript.Given(new PlanPanelViewModel())
            .When(panel => panel.Show(Halfway))
            .When(panel => panel.ToggleCommand.Execute(null))
            .When(panel => panel.Show(new PlanEntry("plan", last is { } status ? [new("Read the code", PlanStepStatus.Done), new("Run the tests", status)] : [])))
            .Then(panel => Assert.Equal((false, false, false), (panel.IsShown, panel.IsExpanded, panel.ToggleCommand.CanExecute(null))));
}
