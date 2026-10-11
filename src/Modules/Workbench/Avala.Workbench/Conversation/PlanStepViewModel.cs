using Avala.Agents.Contracts.Events;

namespace Avala.Workbench.Conversation;

internal interface IPlanStepViewModel
{
    string Title { get; }

    PlanStepStatus Status { get; }
}

internal sealed class PlanStepViewModel(PlanStep step) : IPlanStepViewModel
{
    public string Title { get; } = step.Title;

    public PlanStepStatus Status { get; } = step.Status;

    public static IReadOnlyList<IPlanStepViewModel> Of(IEnumerable<PlanStep> steps) => [.. steps.Select(step => new PlanStepViewModel(step))];
}
