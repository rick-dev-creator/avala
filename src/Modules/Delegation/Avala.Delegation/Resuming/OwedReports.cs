using Avala.Delegation.Records;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Resuming;

internal sealed class OwedReports(DelegationBook book, DelegationJournal journal) : IJobBriefing
{
    public ValueTask<Option<JobBrief>> BriefAsync(JobId job, CancellationToken cancellationToken)
    {
        var reported = book.OwedTo(job).Where(record => record.Report.IsSome).ToList();

        return ValueTask.FromResult(reported.Count == 0
            ? Option<JobBrief>.None
            : new JobBrief(ToolAnswers.Briefing(reported)) { Delivered = token => journal.BriefedAsync(reported, token) });
    }
}
