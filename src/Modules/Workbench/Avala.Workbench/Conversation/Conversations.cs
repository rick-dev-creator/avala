using Avala.Jobs.Contracts;
using Avala.Workbench.ModelChoices;
using Avala.Workbench.NewJob;
using Avala.Workbench.Steering;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Conversation;

internal sealed class Conversations(Composers composers, TimelineItems items, TranscriptExport export)
{
    public ConversationViewModel Open(JobId job) =>
        new(composers.Open(job), items, new PlanPanelViewModel(), export);
}

internal sealed class Composers(JobSteering steering, SendKeys keys, JobHarnesses harnesses)
{
    public ComposerViewModel Open(JobId job) => new(job, steering, keys, harnesses.Open(job));
}

internal sealed class JobHarnesses(JobSwitching switching, NewJobReadings readings)
{
    public JobHarness Open(JobId job) => new(job, switching, readings, new ModelPickerViewModel());
}
