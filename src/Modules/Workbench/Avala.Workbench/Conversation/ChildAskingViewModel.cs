using Avala.Permissions.Contracts;
using Avala.Workbench.Cards;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Conversation;

internal interface IChildAskingViewModel
{
    string Headline { get; }

    string Asking { get; }

    string Status { get; }

    bool IsWaiting { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ChildAskingViewModel : IChildAskingViewModel, ITimelineItem
{
    public ChildAskingViewModel(ChildAskingEntry entry)
    {
        Headline = $"Sub-agent \"{FactPhrases.Title(entry.Child)}\" is waiting for this job";
        Asking = entry.Asking;
        Status = string.Empty;
        Update(entry);
    }

    public string Headline { get; }

    public string Asking { get; }

    public bool IsShown => true;

    [ObservableProperty]
    public partial string Status { get; private set; }

    [ObservableProperty]
    public partial bool IsWaiting { get; private set; }

    public void Update(ITimelineEntry entry)
    {
        if (entry is ChildAskingEntry asked)
        {
            IsWaiting = asked.State == ChildAskingState.Waiting;
            Status = Phrase(asked);
        }
    }

    private static string Phrase(ChildAskingEntry asked) => asked.State switch
    {
        ChildAskingState.Allowed => "Allowed by this job",
        ChildAskingState.Denied => "Denied by this job",
        ChildAskingState.Answered => "Answered by this job",
        ChildAskingState.AnsweredByPerson => "Answered by you",
        ChildAskingState.Passed => $"Went to you: {asked.Passed.Match(CardPhrases.Passed, () => CardPhrases.Passed(PassReason.ParentUnreachable))}",
        _ => "Waiting for this job's answer",
    };
}
