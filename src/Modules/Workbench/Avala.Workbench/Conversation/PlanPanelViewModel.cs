using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Sdk;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Conversation;

internal interface IPlanPanelViewModel
{
    bool IsShown { get; }

    string Progress { get; }

    string Current { get; }

    PlanStepStatus CurrentStatus { get; }

    bool IsExpanded { get; }

    IReadOnlyList<IPlanStepViewModel> Steps { get; }

    IRelayCommand ToggleCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class PlanPanelViewModel : IPlanPanelViewModel
{
    private Option<PlanEntry> shown;

    public PlanPanelViewModel()
    {
        Progress = string.Empty;
        Current = string.Empty;
        Steps = [];
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
    public partial bool IsShown { get; private set; }

    [ObservableProperty]
    public partial string Progress { get; private set; }

    [ObservableProperty]
    public partial string Current { get; private set; }

    [ObservableProperty]
    public partial PlanStepStatus CurrentStatus { get; private set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<IPlanStepViewModel> Steps { get; private set; }

    public void Show(Option<PlanEntry> plan)
    {
        if (shown == plan)
        {
            return;
        }

        shown = plan;
        var open = plan.Match(entry => entry.Total > entry.Done, () => false);
        Steps = plan.Match(entry => PlanStepViewModel.Of(entry.Steps), () => []);
        Progress = plan.Match(entry => string.Create(CultureInfo.InvariantCulture, $"{entry.Done} of {entry.Total}"), () => string.Empty);
        var current = Steps.Where(step => step.Status == PlanStepStatus.InProgress)
            .Concat(Steps.Where(step => step.Status == PlanStepStatus.Pending))
            .Take(1)
            .ToList();
        Current = current.Select(step => step.Title).FirstOrDefault(string.Empty);
        CurrentStatus = current.Select(step => step.Status).FirstOrDefault(PlanStepStatus.Pending);
        IsShown = open;
        IsExpanded = IsExpanded && open;
    }

    [RelayCommand(CanExecute = nameof(IsShown))]
    private void Toggle() => IsExpanded = !IsExpanded;
}
