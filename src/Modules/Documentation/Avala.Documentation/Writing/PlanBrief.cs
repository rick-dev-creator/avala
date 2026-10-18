using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Documentation.Writing;

internal sealed class PlanBrief(DocumentedJobs subjects) : IJobBriefing
{
    public const string Text = "Avala documents this job's feature from your task list. Before you change anything, record your plan as a task list with your task tools, then keep it updated as you work: mark each task in progress when you start it and completed when it is done, and add or remove tasks when the plan changes.";

    public async ValueTask<Option<string>> BriefAsync(JobId job, CancellationToken cancellationToken) =>
        (await subjects.OfAsync(job, cancellationToken)).Bind(subject => subject.Documented.Planned ? Option<string>.None : Text);
}
