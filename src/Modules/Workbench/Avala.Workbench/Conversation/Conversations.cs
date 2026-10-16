using Avala.Components.Pictures;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Drafting;
using Avala.Workbench.Mentioning;
using Avala.Workbench.ModelChoices;
using Avala.Workbench.NewJob;
using Avala.Workbench.Steering;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Conversation;

internal sealed class Conversations(Composers composers, TimelineItems items, TranscriptExport export)
{
    public const int Kept = 4;

    private readonly List<ConversationViewModel> recent = [];

    public ConversationViewModel Open(JobId job)
    {
        var opened = recent.Find(conversation => conversation.Job == job)
            ?? new(composers.Open(job), items, new PlanPanelViewModel(), export);
        recent.Remove(opened);
        recent.Add(opened);

        if (recent.Count > Kept)
        {
            recent.RemoveAt(0);
        }

        return opened;
    }
}

internal sealed record ComposerTools(SendKeys Keys, AttachmentTrayViewModel Attachments, MentionsViewModel Mentions, DraftBook Drafts);

internal sealed record ComposerParts(AttachmentTrays Trays, MentionSource Mentions, DraftBook Drafts);

internal sealed class Composers(JobSteering steering, SendKeys keys, JobHarnesses harnesses, ComposerParts parts)
{
    public ComposerViewModel Open(JobId job) => new(job, steering, new ComposerTools(keys, parts.Trays.Open(), new MentionsViewModel(job, parts.Mentions), parts.Drafts), harnesses.Open(job));
}

internal sealed class AttachmentTrays(IJobAttachments store, IFilePicker picker, IPictureDecoder pictures)
{
    public AttachmentTrayViewModel Open() => new(store, picker, pictures);
}

internal sealed class JobHarnesses(JobSwitching switching, NewJobReadings readings)
{
    public JobHarness Open(JobId job) => new(job, switching, readings, new ModelPickerViewModel());
}
