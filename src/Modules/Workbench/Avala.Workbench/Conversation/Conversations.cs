using Avala.Jobs.Contracts;
using Avala.Workbench.Steering;

namespace Avala.Workbench.Conversation;

internal sealed class Conversations(JobSteering steering, TimelineItems items, SendKeys keys)
{
    public ConversationViewModel Open(JobId job) =>
        new(job, new ComposerViewModel(job, steering, keys), items, new PlanPanelViewModel());
}
