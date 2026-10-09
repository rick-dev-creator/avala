using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Ledger;
using Avala.Sdk;

namespace Avala.Jobs.Catalog;

internal sealed class JobCatalog(IJobStore store) : IJobCatalog
{
    public async ValueTask<IReadOnlyList<JobSummary>> ListAsync(CancellationToken cancellationToken) =>
        [.. (await store.SnapshotsAsync(cancellationToken)).OrderBy(job => job.Submitted).ThenBy(job => job.Id.Value).Select(Summary)];

    public async ValueTask<Option<JobHistory>> HistoryAsync(JobId job, CancellationToken cancellationToken) =>
        (await store.SnapshotAsync(job, cancellationToken)).Map(History);

    private static JobSummary Summary(Job job) =>
        new(job.Id, job.Repository.Value, job.Instruction.Text, job.Submitted, job.State.Status, job.Connection, job.Autonomy, job.Workspace);

    private static JobHistory History(Job job) =>
        new(
            Summary(job),
            [
                .. job.Attempts
                    .SelectMany(attempt => attempt.Session.Match(session => new[] { (Session: session, Attempt: attempt.Number.Value) }, () => []))
                    .GroupBy(ran => ran.Session)
                    .Select(session => new SessionRecord(session.Key, [.. session.Select(ran => ran.Attempt)])),
            ],
            [
                .. job.Attempts.Select(attempt => new AttemptRecord(
                    attempt.Number.Value,
                    attempt.Origin,
                    attempt.Outcome,
                    attempt.Guidance.Map(guidance => guidance.Text),
                    attempt.Session)),
            ]);
}
