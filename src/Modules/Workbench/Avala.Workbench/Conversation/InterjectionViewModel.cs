using Avala.Workbench.Timeline;

namespace Avala.Workbench.Conversation;

internal interface IInterjectionViewModel
{
    string Note { get; }

    string Text { get; }
}

internal sealed class InterjectionViewModel(InterjectionEntry entry) : IInterjectionViewModel, ITimelineItem
{
    public string Note => "You, while it worked · joined the turn";

    public string Text { get; } = entry.Text;

    public bool IsShown => true;

    public void Update(ITimelineEntry entry)
    {
    }
}
