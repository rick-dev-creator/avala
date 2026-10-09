using Avala.Delegation.Records;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Resuming;

internal sealed class OwedReports(DelegationBook book, DelegationJournal journal) : IJobBriefing
{
    public async ValueTask<Option<string>> BriefAsync(JobId job, CancellationToken cancellationToken)
    {
        var reported = book.OwedTo(job).Where(record => record.Report.IsSome).ToList();

        return reported.Count == 0
            ? Option<string>.None
            : ToolAnswers.Briefing(await journal.BriefedAsync(reported, cancellationToken));
    }
}
