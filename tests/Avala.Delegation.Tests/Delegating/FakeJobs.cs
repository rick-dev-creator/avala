using System.Collections.Concurrent;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Delegation.Tests.Delegating;

internal sealed class FakeJobs : IJobs, IJobCatalog, IJobMessages
{
    public const string Repository = "/repos/shop";

    private readonly ConcurrentDictionary<JobId, JobHistory> histories = new();
    private readonly ConcurrentQueue<JobRequest> submitted = new();
    private readonly ConcurrentQueue<JobId> discarded = new();
    private readonly ConcurrentQueue<JobId> resumed = new();
    private readonly ConcurrentQueue<(JobId Job, string Message)> steered = new();
    private readonly ConcurrentQueue<MessageAuthor> steeredBy = new();
    private readonly ConcurrentQueue<(JobId Job, string Message)> continued = new();
    private readonly ConcurrentQueue<MessageAuthor> continuedBy = new();
    private readonly ConcurrentQueue<(JobId Job, JobMessage Message)> sentBack = new();

    public IReadOnlyList<JobRequest> Submitted => [.. submitted];

    public IReadOnlyList<JobId> Discarded => [.. discarded];

    public Option<JobRejection> Rejection { get; set; }

    public Result<ApprovalDelivery, JobRejection> Approval { get; set; } = new ApprovalDelivery("merge", "avala/parent", "c0ffee");

    public JobId Running(Option<JobId> parent = default, Option<ConnectionName> connection = default)
    {
        var job = JobId.New();
        histories[job] = new JobHistory(Summary(job, JobStatus.Running, connection.IsSome ? connection : new ConnectionName("work")) with { Parent = parent }, [], []);

        return job;
    }

    public void RanOn(JobId job, ConnectionName connection) =>
        histories[job] = histories[job] with { Summary = histories[job].Summary with { Connection = connection } };

    public void Attempted(JobId job, AttemptOutcome outcome) =>
        histories[job] = histories[job] with { Attempts = [new AttemptRecord(1, AttemptOrigin.Initial, outcome, Option<string>.None, Option<Agents.Contracts.Sessions.SessionId>.None)] };

    public ValueTask<Result<JobId, JobRejection>> SubmitAsync(JobRequest request, CancellationToken cancellationToken)
    {
        submitted.Enqueue(request);

        if (Rejection.IsSome)
        {
            return ValueTask.FromResult(Result<JobId, JobRejection>.Failure(Rejection.Match(rejection => rejection, () => default)));
        }

        var job = JobId.New();
        histories[job] = new JobHistory(Summary(job, JobStatus.Preparing, request.Connection) with { Parent = request.Parent }, [], []);

        return ValueTask.FromResult(Result<JobId, JobRejection>.Success(job));
    }

    public ValueTask<Result<JobApproval, JobRejection>> ApproveAsync(JobId job, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Approval.Map(delivery => new JobApproval(job, delivery)));

    public bool Steerable { get; set; }

    public IReadOnlyList<(JobId Job, string Message)> Steered => [.. steered];

    public IReadOnlyList<MessageAuthor> SteeredBy => [.. steeredBy];

    public Func<Task> BeforeSteer { get; set; } = () => Task.CompletedTask;

    public async ValueTask<Result<JobSteered, JobRejection>> SteerAsync(JobId job, JobMessage message, CancellationToken cancellationToken)
    {
        await BeforeSteer();

        if (!Steerable)
        {
            return Result<JobSteered, JobRejection>.Failure(JobRejection.NotSteerable);
        }

        steered.Enqueue((job, message.Text));
        steeredBy.Enqueue(message.Author);

        return Result<JobSteered, JobRejection>.Success(new JobSteered(job, Agents.Contracts.Sessions.SessionId.New(), Agents.Contracts.Sessions.TurnId.New()));
    }

    public ValueTask<Result<JobId, JobRejection>> DiscardAsync(JobId job, CancellationToken cancellationToken)
    {
        if (histories.TryGetValue(job, out var history) && history.Summary.Status is JobStatus.Approved or JobStatus.Discarded or JobStatus.Failed)
        {
            return ValueTask.FromResult(Result<JobId, JobRejection>.Failure(JobRejection.NotDiscardable));
        }

        discarded.Enqueue(job);

        return ValueTask.FromResult(Result<JobId, JobRejection>.Success(job));
    }

    public ValueTask<IReadOnlyList<JobSummary>> ListAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<JobSummary>>([.. histories.Values.Select(history => history.Summary)]);

    public ValueTask<Option<JobHistory>> HistoryAsync(JobId job, CancellationToken cancellationToken) =>
        ValueTask.FromResult(histories.TryGetValue(job, out var history) ? history : Option<JobHistory>.None);

    public ValueTask<IReadOnlyList<JobSummary>> ChildrenAsync(JobId parent, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<JobSummary>>(
            [.. histories.Values.Select(history => history.Summary).Where(summary => summary.Parent == Option<JobId>.Some(parent))]);

    public ValueTask<Option<JobTree>> TreeAsync(JobId root, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<JobHold, JobRejection>> HoldRunningAsync(JobId job, HoldReason reason, CancellationToken cancellationToken) => HoldAsync(job, reason, cancellationToken);


    public ValueTask<Result<JobHold, JobRejection>> HoldAsync(JobId job, HoldReason reason, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public IReadOnlyList<(JobId Job, string Message)> Continued => [.. continued];

    public Option<JobRejection> ContinueRefusal { get; set; }

    public IReadOnlyList<MessageAuthor> ContinuedBy => [.. continuedBy];

    public ValueTask<Result<JobContinuation, JobRejection>> ContinueAsync(JobId job, JobMessage message, CancellationToken cancellationToken)
    {
        continuedBy.Enqueue(message.Author);

        return ContinueAsync(job, message.Text, cancellationToken);
    }

    public ValueTask<Result<JobContinuation, JobRejection>> ContinueAsync(JobId job, string message, CancellationToken cancellationToken)
    {
        if (ContinueRefusal.IsSome)
        {
            return ValueTask.FromResult(ContinueRefusal.Match(Result<JobContinuation, JobRejection>.Failure, () => throw new InvalidOperationException()));
        }

        continued.Enqueue((job, message));

        return ValueTask.FromResult(Result<JobContinuation, JobRejection>.Success(new JobContinuation(job, Agents.Contracts.Sessions.SessionId.New(), ContinuedIn.NewConversation)));
    }

    public ValueTask<Result<JobContinuation, JobRejection>> ContinueOnAsync(JobId job, ConnectionName connection, JobMessage message, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public IReadOnlyList<(JobId Job, JobMessage Message)> SentBack => [.. sentBack];

    public ValueTask<Result<JobContinuation, JobRejection>> SendBackAsync(JobId job, JobMessage message, CancellationToken cancellationToken)
    {
        sentBack.Enqueue((job, message));

        return ValueTask.FromResult(Result<JobContinuation, JobRejection>.Success(new JobContinuation(job, Agents.Contracts.Sessions.SessionId.New(), ContinuedIn.ResumedConversation)));
    }

    public ValueTask<Result<JobContinuation, JobRejection>> SendBackAsync(JobId job, string feedback, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask<Result<JobContinuation, JobRejection>> HandOffAsync(JobId job, JobHandoff handoff, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask<Result<JobModelChosen, JobRejection>> ChooseModelAsync(JobId job, Avala.Agents.Contracts.Sessions.ModelChoice model, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public IReadOnlyList<JobId> Resumed => [.. resumed];

    public ValueTask<Result<JobContinuation, JobRejection>> ResumeAsync(JobId job, CancellationToken cancellationToken)
    {
        resumed.Enqueue(job);

        return ValueTask.FromResult(Result<JobContinuation, JobRejection>.Success(new JobContinuation(job, Agents.Contracts.Sessions.SessionId.New(), ContinuedIn.ResumedConversation)));
    }

    public void Is(JobId job, JobStatus status) =>
        histories[job] = histories[job] with { Summary = histories[job].Summary with { Status = status } };

    public void HeldFor(JobId job, HoldReason reason) =>
        histories[job] = histories[job] with { Summary = histories[job].Summary with { Status = JobStatus.NeedsHelp, Hold = reason } };

    public void Knows(JobId job, Option<JobId> parent) =>
        histories[job] = new JobHistory(Summary(job, JobStatus.Running, new ConnectionName("work")) with { Parent = parent }, [], []);

    private static JobSummary Summary(JobId job, JobStatus status, Option<ConnectionName> connection) =>
        new(job, Repository, "Ship the release", DateTimeOffset.UnixEpoch, status, connection, Option<Autonomy>.None, new WorkspaceId(job.Value));
}
