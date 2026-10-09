using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Conversation;
using Avala.Workbench.Review;
using Avala.Workbench.Reviewing;

namespace Avala.Workbench.Navigation;

internal sealed class Reviews(ReviewReader reader, ReviewDesk desk, IUiDispatcher ui)
{
    public ReviewViewModel Open(JobId job) => new(job, reader, desk, ui);
}

internal sealed class JobScreens(Conversations conversations, Reviews reviews)
{
    public ConversationViewModel Conversation(JobId job) => conversations.Open(job);

    public ReviewViewModel Review(JobId job) => reviews.Open(job);
}
