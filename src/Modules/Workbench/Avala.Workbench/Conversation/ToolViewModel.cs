using Avala.Agents.Contracts.Events;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Conversation;

[INotifyPropertyChanged]
internal sealed partial class ToolViewModel : ITimelineItem
{
    public ToolViewModel(ToolEntry entry)
    {
        Kind = entry.Kind;
        Title = entry.Title;
        Input = entry.Input.Match(input => input, () => string.Empty);
        Output = string.Empty;
        Outcome = string.Empty;
        Update(entry);
    }

    public ItemKind Kind { get; }

    public string Title { get; }

    public string Input { get; }

    public bool IsShown => true;

    [ObservableProperty]
    public partial string Output { get; private set; }

    [ObservableProperty]
    public partial bool IsRunning { get; private set; }

    [ObservableProperty]
    public partial bool Failed { get; private set; }

    [ObservableProperty]
    public partial string Outcome { get; private set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public void Update(ITimelineEntry entry)
    {
        if (entry is ToolEntry tool)
        {
            Output = tool.Output;
            IsRunning = tool.Outcome.IsNone;
            Failed = tool.Outcome.Match(outcome => outcome != ItemOutcome.Succeeded, () => false);
            Outcome = tool.Outcome.Match(ConversationPhrases.Outcome, () => "running");
        }
    }

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;
}
