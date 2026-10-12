using Avala.Jobs.Contracts;
using Avala.Workbench.Steering;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Conversation;

internal sealed class Conversations(JobSteering steering, TimelineItems items, SendKeys keys, TranscriptExport export)
{
    public ConversationViewModel Open(JobId job) =>
        new(new ComposerViewModel(job, steering, keys), items, new PlanPanelViewModel(), export);
}
