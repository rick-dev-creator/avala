using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Cards;
using Avala.Workbench.Linking;
using Avala.Workbench.Replies;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Conversation;

internal sealed class TimelineItems(HumanReplies replies, Links links, IClipboard clipboard, FileViewing files)
{
    public ITimelineItem Create(JobId job, ITimelineEntry entry) => entry switch
    {
        PromptEntry prompt => new PromptViewModel(prompt, clipboard),
        InterjectionEntry interjection => new InterjectionViewModel(interjection),
        MessageEntry message => new MessageViewModel(message, links, clipboard),
        ReasoningEntry reasoning => new ReasoningViewModel(reasoning),
        ToolEntry tool => new ToolViewModel(tool, clipboard, files.For(job)),
        PlanEntry plan => new PlanViewModel(plan),
        CanvasEntry canvas => new CanvasViewModel(canvas),
        PermissionEntry permission => new PermissionCardViewModel(permission, replies),
        DeniedEntry denied => new DeniedCardViewModel(denied, replies),
        FormEntry form => new FormCardViewModel(form, replies),
        TurnEndEntry ended => new TurnEndViewModel(ended),
        ChildAskingEntry asked => new ChildAskingViewModel(asked),
        RestartEntry restart => new RestartViewModel(restart),
        _ => new RestartViewModel(new RestartEntry(entry.Key, Kept: false)),
    };
}
