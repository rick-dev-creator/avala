using System.Collections.Concurrent;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Autopilot.Tests.Looping;

internal sealed class FakeJobs : IJobs, IJobCatalog
{
    private readonly ConcurrentQueue<(JobId Job, JobRequest Request)> submitted = new();
    private readonly ConcurrentDictionary<JobId, JobHistory> histories = new();
    private readonly ConcurrentQueue<JobId> approved = new();
    private readonly ConcurrentQueue<(JobId Job, string Message)> continued = new();

    public IReadOnlyList<(JobId Job, JobRequest Request)> Submitted => [.. submitted];

    public IReadOnlyList<JobId> Approved => [.. approved];

    public IReadOnlyList<(JobId Job, string Message)> Continued => [.. continued];

    public Option<JobRejection> Rejection { get; set; }

    public Option<JobRejection> ApprovalRefusal { get; set; }

    public Option<ConnectionName> Connection { get; set; } = new ConnectionName("work");

    public ValueTask<Result<JobId, JobRejection>> SubmitAsync(JobRequest request, CancellationToken cancellationToken)
    {
        if (Rejection.IsSome)
        {
            return ValueTask.FromResult(Result<JobId, JobRejection>.Failure(Rejection.Match(rejection => rejection, () => default)));
        }

        var job = JobId.New();
        var summary = new JobSummary(job, request.RepositoryPath, request.Instruction, DateTimeOffset.UnixEpoch, JobStatus.Running, Connection, request.Autonomy, new WorkspaceId(job.Value));
        histories[job] = new JobHistory(summary, [], [new AttemptRecord(1, AttemptOrigin.Initial, AttemptOutcome.Running, Option<string>.None, Option<SessionId>.None)]);
        submitted.Enqueue((job, request));

        return ValueTask.FromResult(Result<JobId, JobRejection>.Success(job));
    }

    public void Attempts(JobId job, params AttemptRecord[] attempts) =>
        histories[job] = histories[job] with { Attempts = attempts };

    public ValueTask<Result<JobApproval, JobRejection>> ApproveAsync(JobId job, CancellationToken cancellationToken)
    {
        if (ApprovalRefusal.IsSome)
        {
            return ValueTask.FromResult(Result<JobApproval, JobRejection>.Failure(ApprovalRefusal.Match(refusal => refusal, () => default)));
        }

        approved.Enqueue(job);

        return ValueTask.FromResult(Result<JobApproval, JobRejection>.Success(new JobApproval(job, new ApprovalDelivery("merge", "main", "c0ffee"))));
    }

    public ValueTask<Result<JobContinuation, JobRejection>> ContinueAsync(JobId job, string message, CancellationToken cancellationToken)
    {
        continued.Enqueue((job, message));

        return ValueTask.FromResult(Result<JobContinuation, JobRejection>.Success(new JobContinuation(job, SessionId.New(), ContinuedIn.SameSession)));
    }

    public ValueTask<Result<JobContinuation, JobRejection>> ContinueOnAsync(
        JobId job,
        Avala.Agents.Contracts.Connections.ConnectionName connection,
        string message,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask<IReadOnlyList<JobSummary>> ListAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<JobSummary>>([.. histories.Values.Select(history => history.Summary)]);

    public ValueTask<Option<JobHistory>> HistoryAsync(JobId job, CancellationToken cancellationToken) =>
        ValueTask.FromResult(histories.TryGetValue(job, out var history) ? history : Option<JobHistory>.None);

    public ValueTask<IReadOnlyList<JobSummary>> ChildrenAsync(JobId parent, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask<Option<JobTree>> TreeAsync(JobId root, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask<Result<JobHold, JobRejection>> HoldAsync(JobId job, HoldReason reason, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask<Result<JobId, JobRejection>> DiscardAsync(JobId job, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask<Result<JobContinuation, JobRejection>> SendBackAsync(JobId job, string feedback, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask<Result<JobContinuation, JobRejection>> ResumeAsync(JobId job, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
