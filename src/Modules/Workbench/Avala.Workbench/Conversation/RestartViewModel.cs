using Avala.Workbench.Timeline;

namespace Avala.Workbench.Conversation;

internal sealed class RestartViewModel : ITimelineItem
{
    public string Note { get; } = "Avala restarted. The agent's work before this point is summarized by its attempts above.";

    public bool IsShown => true;

    public void Update(ITimelineEntry entry)
    {
    }
}
