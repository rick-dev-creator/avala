using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Conversation;

[INotifyPropertyChanged]
internal sealed partial class PlanViewModel : ITimelineItem
{
    public PlanViewModel(PlanEntry entry)
    {
        Steps = [];
        Progress = string.Empty;
        Update(entry);
    }

    public bool IsShown => true;

    [ObservableProperty]
    public partial IReadOnlyList<PlanStep> Steps { get; private set; }

    [ObservableProperty]
    public partial string Progress { get; private set; }

    public void Update(ITimelineEntry entry)
    {
        if (entry is PlanEntry plan)
        {
            Steps = plan.Steps;
            Progress = string.Create(CultureInfo.InvariantCulture, $"{plan.Done} of {plan.Total}");
        }
    }
}
