using Avala.Jobs.Contracts;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Conversation;

internal interface IPromptViewModel
{
    int Attempt { get; }

    string Origin { get; }

    bool IsFromPerson { get; }

    bool ShowsOrigin { get; }

    string Text { get; }

    string Outcome { get; }
}

[INotifyPropertyChanged]
internal sealed partial class PromptViewModel : IPromptViewModel, ITimelineItem
{
    public PromptViewModel(PromptEntry entry)
    {
        Attempt = entry.Attempt;
        Origin = ConversationPhrases.Origin(entry);
        IsFromPerson = entry.Origin is AttemptOrigin.Initial or AttemptOrigin.Hint or AttemptOrigin.SendBack;
        ShowsOrigin = entry.Origin != AttemptOrigin.Initial;
        Text = entry.Text.Match(text => text, () => string.Empty);
        Outcome = string.Empty;
        Update(entry);
    }

    public int Attempt { get; }

    [ObservableProperty]
    public partial string Origin { get; private set; }

    public bool IsFromPerson { get; }

    public bool ShowsOrigin { get; }

    public string Text { get; }

    public bool IsShown => true;

    [ObservableProperty]
    public partial string Outcome { get; private set; }

    public void Update(ITimelineEntry entry)
    {
        if (entry is PromptEntry prompt)
        {
            Outcome = prompt.Outcome.Match(ConversationPhrases.Outcome, () => string.Empty);
            Origin = ConversationPhrases.Origin(prompt);
        }
    }
}
