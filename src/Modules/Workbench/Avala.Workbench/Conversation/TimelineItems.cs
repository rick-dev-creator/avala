using Avala.Workbench.Cards;
using Avala.Workbench.Linking;
using Avala.Workbench.Replies;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Conversation;

internal sealed class TimelineItems(HumanReplies replies, Links links)
{
    public ITimelineItem Create(ITimelineEntry entry) => entry switch
    {
        PromptEntry prompt => new PromptViewModel(prompt),
        InterjectionEntry interjection => new InterjectionViewModel(interjection),
        MessageEntry message => new MessageViewModel(message, links),
        ReasoningEntry reasoning => new ReasoningViewModel(reasoning),
        ToolEntry tool => new ToolViewModel(tool),
        PlanEntry plan => new PlanViewModel(plan),
        CanvasEntry canvas => new CanvasViewModel(canvas),
        PermissionEntry permission => new PermissionCardViewModel(permission, replies),
        FormEntry form => new FormCardViewModel(form, replies),
        TurnEndEntry ended => new TurnEndViewModel(ended),
        RestartEntry restart => new RestartViewModel(restart),
        _ => new RestartViewModel(new RestartEntry(entry.Key, Kept: false)),
    };
}
