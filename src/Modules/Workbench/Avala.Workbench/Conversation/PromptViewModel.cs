using Avala.Jobs.Contracts;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Conversation;

[INotifyPropertyChanged]
internal sealed partial class PromptViewModel : ITimelineItem
{
    public PromptViewModel(PromptEntry entry)
    {
        Attempt = entry.Attempt;
        Origin = ConversationPhrases.Origin(entry.Origin);
        IsFromPerson = entry.Origin is AttemptOrigin.Initial or AttemptOrigin.Hint or AttemptOrigin.SendBack;
        Text = entry.Text.Match(text => text, () => string.Empty);
        Outcome = string.Empty;
        Update(entry);
    }

    public int Attempt { get; }

    public string Origin { get; }

    public bool IsFromPerson { get; }

    public string Text { get; }

    public bool IsShown => true;

    [ObservableProperty]
    public partial string Outcome { get; private set; }

    public void Update(ITimelineEntry entry)
    {
        if (entry is PromptEntry prompt)
        {
            Outcome = prompt.Outcome.Match(ConversationPhrases.Outcome, () => string.Empty);
        }
    }
}
