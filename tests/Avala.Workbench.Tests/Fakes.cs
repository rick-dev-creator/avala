using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Tests;

internal sealed class FakeCatalog : IJobCatalog
{
    private readonly Dictionary<JobId, JobHistory> histories = [];

    public JobHistory Add(string instruction, JobStatus status = JobStatus.Preparing, params AttemptRecord[] attempts)
    {
        var job = JobId.New();
        var summary = new JobSummary(job, "/repo", instruction, new DateTimeOffset(2026, 10, 9, 9, histories.Count, 0, TimeSpan.Zero), status, Option<Avala.Agents.Contracts.Connections.ConnectionName>.None, Option<Autonomy>.None, Option<Avala.Workspaces.Contracts.WorkspaceId>.None);
        histories[job] = new JobHistory(summary, [], attempts);

        return histories[job];
    }

    public void Attempted(JobId job, params AttemptRecord[] attempts) => histories[job] = histories[job] with { Attempts = attempts };

    public void Change(JobId job, Func<JobHistory, JobHistory> change) => histories[job] = change(histories[job]);

    public JobSummary Summary(JobId job) => histories[job].Summary;

    public ValueTask<IReadOnlyList<JobSummary>> ListAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<JobSummary>>([.. histories.Values.Select(history => history.Summary)]);

    public ValueTask<Option<JobHistory>> HistoryAsync(JobId job, CancellationToken cancellationToken) =>
        ValueTask.FromResult(histories.TryGetValue(job, out var history) ? Option<JobHistory>.Some(history) : Option<JobHistory>.None);

    public ValueTask<IReadOnlyList<JobSummary>> ChildrenAsync(JobId parent, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<JobSummary>>([.. histories.Values.Select(history => history.Summary).Where(summary => summary.Parent == parent)]);

    public ValueTask<Option<JobTree>> TreeAsync(JobId root, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Option<JobTree>.None);
}

internal sealed class FakeJobs : IJobs
{
    public List<string> Calls { get; } = [];

    public JobRejection Refusal { get; set; } = (JobRejection)(-1);

    public ValueTask<Result<JobId, JobRejection>> SubmitAsync(JobRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask<Result<JobHold, JobRejection>> HoldAsync(JobId job, HoldReason reason, CancellationToken cancellationToken) =>
        AnswerAsync($"hold {reason}", new JobHold(job, SessionId.New(), reason, SessionHalt.Interrupted));

    public ValueTask<Result<JobContinuation, JobRejection>> ContinueAsync(JobId job, string message, CancellationToken cancellationToken) =>
        AnswerAsync($"continue {message}", new JobContinuation(job, SessionId.New(), ContinuedIn.SameSession));

    public ValueTask<Result<JobId, JobRejection>> DiscardAsync(JobId job, CancellationToken cancellationToken) =>
        AnswerAsync("discard", job);

    public ValueTask<Result<JobApproval, JobRejection>> ApproveAsync(JobId job, CancellationToken cancellationToken) =>
        AnswerAsync("approve", new JobApproval(job, new ApprovalDelivery("keep", "avala/fix-the-test", Option<string>.None)));

    public ValueTask<Result<JobContinuation, JobRejection>> SendBackAsync(JobId job, string feedback, CancellationToken cancellationToken) =>
        AnswerAsync($"send back {feedback}", new JobContinuation(job, SessionId.New(), ContinuedIn.SameSession));

    private ValueTask<Result<T, JobRejection>> AnswerAsync<T>(string call, T value)
        where T : notnull
    {
        Calls.Add(call);

        return ValueTask.FromResult(Enum.IsDefined(Refusal) ? Result<T, JobRejection>.Failure(Refusal) : Result<T, JobRejection>.Success(value));
    }
}

internal sealed class FakePermissionAnswers : IPermissionAnswers
{
    public List<(SessionId Session, PermissionReply Reply)> Replies { get; } = [];

    public bool Refuse { get; set; }

    public ValueTask<Result<HumanAnswer, PolicyError>> AnswerAsync(SessionId session, PermissionReply reply, CancellationToken cancellationToken)
    {
        Replies.Add((session, reply));

        return ValueTask.FromResult(Refuse
            ? Result<HumanAnswer, PolicyError>.Failure(PolicyError.NotAwaitingAnswer)
            : Result<HumanAnswer, PolicyError>.Success(new HumanAnswer(session, Option<JobId>.None, reply.Item, ItemKind.Command, "target", reply.Answer, reply.Message, Option<PolicyRule>.None, DateTimeOffset.UnixEpoch)));
    }
}

internal sealed class FakeAgents : IAgents
{
    public List<(SessionId Session, FormAnswer Answer)> Answers { get; } = [];

    public ValueTask<Result<OpenedSession, AgentError>> OpenAsync(AgentRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

    public bool IsOpen(SessionId session) => true;

    public ValueTask<Result<AgentTurn, AgentError>> SendAsync(SessionId session, string message, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<ItemId, AgentError>> RespondAsync(SessionId session, PermissionDecision decision, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<ItemId, AgentError>> AnswerAsync(SessionId session, FormAnswer answer, CancellationToken cancellationToken)
    {
        Answers.Add((session, answer));

        return ValueTask.FromResult(Result<ItemId, AgentError>.Success(answer.Item));
    }

    public ValueTask<Result<ItemId, AgentError>> ReturnAsync(SessionId session, ToolResult result, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<TurnId, AgentError>> InterruptAsync(SessionId session, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<SessionId, AgentError>> StopAsync(SessionId session, CancellationToken cancellationToken) => throw new NotSupportedException();
}
