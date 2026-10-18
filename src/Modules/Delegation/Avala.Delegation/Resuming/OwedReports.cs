using Avala.Delegation.Records;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Resuming;

internal sealed class OwedReports(DelegationBook book, DelegationJournal journal) : IJobBriefing
{
    public async ValueTask<Option<JobBrief>> BriefAsync(JobId job, CancellationToken cancellationToken)
    {
        var claimed = await journal.ClaimedAsync([.. book.OwedTo(job).Where(record => record.Report.IsSome)], cancellationToken);

        return claimed.Count == 0
            ? Option<JobBrief>.None
            : new JobBrief(ToolAnswers.Briefing(claimed))
            {
                Delivered = token => journal.ConfirmedAsync(claimed, token),
                Undelivered = token => journal.ReleasedAsync(claimed, token),
            };
    }
}
