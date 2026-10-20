using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Conversation;

internal interface IReasoningViewModel
{
    FindMark Found { get; }

    string Text { get; }

    bool IsThinking { get; }

    TimeSpan Duration { get; }

    string Summary { get; }

    bool IsExpanded { get; }

    bool HasText { get; }

    IRelayCommand ToggleCommand { get; }

    bool InRun { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ReasoningViewModel : IReasoningViewModel, ITimelineItem, ISearchable, IRunMember
{
    public ReasoningViewModel(ReasoningEntry entry)
    {
        Text = string.Empty;
        Summary = string.Empty;
        Update(entry);
    }

    public bool IsShown => true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasText))]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
    public partial string Text { get; private set; }

    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    [ObservableProperty]
    public partial bool IsThinking { get; private set; }

    [ObservableProperty]
    public partial TimeSpan Duration { get; private set; }

    [ObservableProperty]
    public partial string Summary { get; private set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; private set; }

    [ObservableProperty]
    public partial FindMark Found { get; set; }

    [ObservableProperty]
    public partial bool InRun { get; set; }

    public string SearchText => Text;

    public bool Unfold()
    {
        var folded = !IsExpanded;
        IsExpanded = true;

        return folded;
    }

    public void Update(ITimelineEntry entry)
    {
        if (entry is ReasoningEntry reasoning)
        {
            Text = reasoning.Text;
            IsThinking = reasoning.Outcome.IsNone;
            Duration = reasoning.Duration.Match(duration => duration, () => TimeSpan.Zero);
            Summary = reasoning.Duration.Match(
                duration => HasText ? ConversationPhrases.Thought(duration) : ConversationPhrases.ThoughtUnshared(duration),
                () => "Thinking");
            IsExpanded = IsExpanded && HasText;
        }
    }

    [RelayCommand(CanExecute = nameof(HasText))]
    private void Toggle() => IsExpanded = !IsExpanded;
}
