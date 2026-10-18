using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Documentation.Writing;

internal sealed class PlanBrief(DocumentedJobs subjects) : IJobBriefing
{
    public const string Text = "Avala documents this job's feature from your task list. Before you change anything, record your plan as a task list with your task tools, then keep it updated as you work: mark each task in progress when you start it and completed when it is done, and add or remove tasks when the plan changes.";

    public const string Kept = "Avala documents this job's feature from your task list. Before you act on this message, bring your task list up to date with it: record it again with your task tools if you no longer have it, add, remove or reword the tasks this message changes, and keep marking each task in progress when you start it and completed when it is done.";

    public async ValueTask<Option<JobBrief>> BriefAsync(JobId job, CancellationToken cancellationToken) =>
        (await subjects.OfAsync(job, cancellationToken)).Map(subject => new JobBrief(subject.Documented.Planned ? Kept : Text));
}
