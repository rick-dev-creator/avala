using System.Globalization;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Conversation;

internal interface IPlanViewModel
{
    IReadOnlyList<IPlanStepViewModel> Steps { get; }

    string Progress { get; }
}

[INotifyPropertyChanged]
internal sealed partial class PlanViewModel : IPlanViewModel, ITimelineItem
{
    public PlanViewModel(PlanEntry entry)
    {
        Steps = [];
        Progress = string.Empty;
        Update(entry);
    }

    public bool IsShown => true;

    [ObservableProperty]
    public partial IReadOnlyList<IPlanStepViewModel> Steps { get; private set; }

    [ObservableProperty]
    public partial string Progress { get; private set; }

    public void Update(ITimelineEntry entry)
    {
        if (entry is PlanEntry plan)
        {
            Steps = PlanStepViewModel.Of(plan.Steps);
            Progress = string.Create(CultureInfo.InvariantCulture, $"{plan.Done} of {plan.Total}");
        }
    }
}
