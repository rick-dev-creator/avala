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
        await (await store.SnapshotAsync(job, cancellationToken)).Match(
            async found => Option<JobHistory>.Some(History(found) with { Choice = await store.ChoiceOfAsync(job, cancellationToken) }),
            () => Task.FromResult(Option<JobHistory>.None));

    public async ValueTask<IReadOnlyList<JobSummary>> ChildrenAsync(JobId parent, CancellationToken cancellationToken) =>
        [.. (await store.SnapshotsAsync(cancellationToken)).Where(job => job.Parent == Option<JobId>.Some(parent)).OrderBy(job => job.Submitted).ThenBy(job => job.Id.Value).Select(Summary)];

    public async ValueTask<Option<JobTree>> TreeAsync(JobId root, CancellationToken cancellationToken)
    {
        var jobs = (await store.SnapshotsAsync(cancellationToken)).OrderBy(job => job.Submitted).ThenBy(job => job.Id.Value).ToList();
        var children = jobs.Where(job => job.Parent.IsSome).ToLookup(job => job.Parent.Match(parent => parent, () => default));

        return jobs.FirstOrDefault(job => job.Id == root).ToOption().Map(found => Grow(found, children));
    }

    private static JobTree Grow(Job job, ILookup<JobId, Job> children) =>
        new(Summary(job), [.. children[job.Id].Select(child => Grow(child, children))]);

    private static JobSummary Summary(Job job) =>
        new(job.Id, job.Repository.Value, job.Instruction.Text, job.Submitted, job.State.Status, job.Connection, job.Autonomy, job.Workspace)
        {
            Parent = job.Parent,
        };

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
