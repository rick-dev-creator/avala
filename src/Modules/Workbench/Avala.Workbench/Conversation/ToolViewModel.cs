using Avala.Agents.Contracts.Events;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Conversation;

internal interface IToolViewModel
{
    ItemKind Kind { get; }

    string Title { get; }

    string Input { get; }

    string Output { get; }

    bool IsRunning { get; }

    bool Failed { get; }

    string Outcome { get; }

    bool IsExpanded { get; }

    IRelayCommand ToggleCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ToolViewModel : IToolViewModel, ITimelineItem
{
    public ToolViewModel(ToolEntry entry)
    {
        Kind = entry.Kind;
        Input = entry.Input.Match(input => input, () => string.Empty);
        Title = Presenting.CommandPhrases.Title(entry.Kind, entry.Title, Input);
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
    public partial bool IsExpanded { get; private set; }

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
