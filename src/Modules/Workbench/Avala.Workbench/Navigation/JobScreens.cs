using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Conversation;
using Avala.Workbench.Inspection;
using Avala.Workbench.Inspector;
using Avala.Workbench.Review;
using Avala.Workbench.Reviewing;

namespace Avala.Workbench.Navigation;

internal sealed class Reviews(ReviewReader reader, ReviewDesk desk, IUiDispatcher ui)
{
    public ReviewViewModel Open(JobId job) => new(job, reader, desk, ui);
}

internal sealed class Inspectors(JobInspection inspection, IUiDispatcher ui)
{
    public InspectorViewModel Open(JobId job) => new(job, inspection, ui);
}

internal sealed class JobScreens(Conversations conversations, Reviews reviews, Inspectors inspectors)
{
    public ConversationViewModel Conversation(JobId job) => conversations.Open(job);

    public ReviewViewModel Review(JobId job) => reviews.Open(job);

    public InspectorViewModel Inspector(JobId job) => inspectors.Open(job);
}
