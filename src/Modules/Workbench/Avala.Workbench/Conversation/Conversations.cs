using Avala.Jobs.Contracts;
using Avala.Workbench.Steering;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Conversation;

internal sealed class Conversations(JobSteering steering, TimelineItems items, SendKeys keys, TranscriptExport export)
{
    public const int Kept = 4;

    private readonly List<ConversationViewModel> recent = [];

    public ConversationViewModel Open(JobId job)
    {
        var opened = recent.Find(conversation => conversation.Job == job)
            ?? new(new ComposerViewModel(job, steering, keys), items, new PlanPanelViewModel(), export);
        recent.Remove(opened);
        recent.Add(opened);

        if (recent.Count > Kept)
        {
            recent.RemoveAt(0);
        }

        return opened;
    }
}
