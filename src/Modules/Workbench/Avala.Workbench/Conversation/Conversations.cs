using Avala.Jobs.Contracts;
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

internal sealed class Composers(JobSteering steering, SendKeys keys, JobHarnesses harnesses)
{
    public ComposerViewModel Open(JobId job) => new(job, steering, keys, harnesses.Open(job));
}

internal sealed class JobHarnesses(JobSwitching switching, NewJobReadings readings)
{
    public JobHarness Open(JobId job) => new(job, switching, readings, new ModelPickerViewModel());
}
