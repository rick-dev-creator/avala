using Avala.Workbench.Timeline;

namespace Avala.Workbench.Conversation;

internal interface ITimelineItem
{
    bool IsShown { get; }

    void Update(ITimelineEntry entry);
}
