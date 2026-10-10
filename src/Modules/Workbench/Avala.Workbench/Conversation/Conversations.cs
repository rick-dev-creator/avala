using Avala.Jobs.Contracts;
using Avala.Workbench.Linking;
using Avala.Workbench.Replies;
using Avala.Workbench.Steering;

namespace Avala.Workbench.Conversation;

internal sealed class Conversations(JobSteering steering, HumanReplies replies, Links links)
{
    public ConversationViewModel Open(JobId job) =>
        new(job, new ComposerViewModel(job, steering), new TimelineItems(replies, links), new PlanPanelViewModel());
}
