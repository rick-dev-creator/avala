using Avala.Workbench.Presenting;
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
        var waitsFor = entry.ForYou ? "is asking you" : "is waiting for this job";
        Headline = entry.Harness.Match(
            harness => $"Sub-agent \"{FactPhrases.Title(entry.Child)}\" on {harness} {waitsFor}",
            () => $"Sub-agent \"{FactPhrases.Title(entry.Child)}\" {waitsFor}");
        Asking = entry.Permission.Match(permission => $"wants to {CommandPhrases.Request(permission.Kind, permission.Target)}", () => entry.Asking);
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
        ChildAskingState.Answered => "Answered by this job",
        ChildAskingState.Declined => "Declined by this job",
        ChildAskingState.Allowed => "Allowed by you",
        ChildAskingState.Denied => "Denied by you",
        ChildAskingState.Withdrawn => "No longer waiting",
        _ => asked.ForYou ? "Waiting for you: answer it in the sub-agent's conversation or the Delegation pane" : "Waiting for this job's answer",
    };
}
