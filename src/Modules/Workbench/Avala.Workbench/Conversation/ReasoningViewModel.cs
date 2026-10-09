using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Conversation;

internal interface IReasoningViewModel
{
    string Text { get; }

    bool IsThinking { get; }

    TimeSpan Duration { get; }

    string Summary { get; }

    bool IsExpanded { get; }

    IRelayCommand ToggleCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ReasoningViewModel : IReasoningViewModel, ITimelineItem
{
    public ReasoningViewModel(ReasoningEntry entry)
    {
        Text = string.Empty;
        Summary = string.Empty;
        Update(entry);
    }

    public bool IsShown => true;

    [ObservableProperty]
    public partial string Text { get; private set; }

    [ObservableProperty]
    public partial bool IsThinking { get; private set; }

    [ObservableProperty]
    public partial TimeSpan Duration { get; private set; }

    [ObservableProperty]
    public partial string Summary { get; private set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; private set; }

    public void Update(ITimelineEntry entry)
    {
        if (entry is ReasoningEntry reasoning)
        {
            Text = reasoning.Text;
            IsThinking = reasoning.Outcome.IsNone;
            Duration = reasoning.Duration.Match(duration => duration, () => TimeSpan.Zero);
            Summary = reasoning.Duration.Match(ConversationPhrases.Thought, () => "Thinking");
        }
    }

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;
}
