using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Cards;
using Avala.Workbench.Linking;
using Avala.Workbench.Replies;
using Avala.Workbench.Reviewing;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Conversation;

internal sealed class TimelineItems(HumanReplies replies, Links links, IClipboard clipboard, WorkedFiles files)
{
    public ITimelineItem Create(JobId job, ITimelineEntry entry) => entry switch
    {
        PromptEntry prompt => new PromptViewModel(prompt, clipboard),
        InterjectionEntry interjection => new InterjectionViewModel(interjection),
        MessageEntry message => new MessageViewModel(message, links, clipboard),
        ReasoningEntry reasoning => new ReasoningViewModel(reasoning),
        ToolEntry tool => new ToolViewModel(tool, clipboard, new FileViews(job, files)),
        PlanEntry plan => new PlanViewModel(plan),
        CanvasEntry canvas => new CanvasViewModel(canvas),
        PermissionEntry permission => new PermissionCardViewModel(permission, replies),
        FormEntry form => new FormCardViewModel(form, replies),
        TurnEndEntry ended => new TurnEndViewModel(ended),
        ChildAskingEntry asked => new ChildAskingViewModel(asked),
        RestartEntry restart => new RestartViewModel(restart),
        _ => new RestartViewModel(new RestartEntry(entry.Key, Kept: false)),
    };
}
