using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Cards;
using Avala.Workbench.Linking;
using Avala.Workbench.Replies;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Conversation;

internal sealed class TimelineItems(HumanReplies replies, IClipboard clipboard, FileViewing files)
{
    public ITimelineItem Create(JobId job, ITimelineEntry entry) => entry switch
    {
        PromptEntry prompt => new PromptViewModel(prompt, clipboard, Chips),
        InterjectionEntry interjection => new InterjectionViewModel(interjection) { Attachments = Chips(interjection.Attachments) },
        MessageEntry message => new MessageViewModel(message, clipboard, files.For(job)),
        ReasoningEntry reasoning => new ReasoningViewModel(reasoning),
        ToolEntry tool => new ToolViewModel(tool, clipboard, files.For(job)),
        PlanEntry plan => new PlanViewModel(plan),
        CanvasEntry canvas => new CanvasViewModel(canvas),
        MediaEntry media => new MediaViewModel(media, files.Pictures),
        PermissionEntry permission => new PermissionCardViewModel(permission, replies),
        DeniedEntry denied => new DeniedCardViewModel(denied, replies),
        FormEntry form => new FormCardViewModel(form, replies),
        TurnEndEntry ended => new TurnEndViewModel(ended),
        ChildAskingEntry asked => new ChildAskingViewModel(asked),
        _ => QuietItems.Of(entry, files.Links),
    };

    private IReadOnlyList<IAttachmentChipViewModel> Chips(IReadOnlyList<Avala.Agents.Contracts.Sessions.Attachment> attachments) =>
        [.. attachments.Select(attachment => new AttachmentChipViewModel(attachment, files.Pictures, removable: false))];
}

internal static class QuietItems
{
    public static ITimelineItem Of(ITimelineEntry entry, Links links) => entry switch
    {
        RestartEntry restart => new RestartViewModel(restart),
        CiEntry ci => new CiViewModel(ci, links),
        DocsEntry docs => new DocsLineViewModel(docs),
        ChangeEntry change => new ChangeLineViewModel(change),
        _ => new RestartViewModel(new RestartEntry(entry.Key, Kept: false)),
    };
}
