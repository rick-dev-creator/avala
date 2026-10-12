using System.Collections.Concurrent;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Autopilot.Contracts;
using Avala.Contests.Contracts;
using Avala.Contests.Running;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Contests.Tests.Running;

internal sealed class FakeJobs : IJobs, IJobCatalog
{
    private readonly ConcurrentQueue<JobRequest> submitted = new();
    private readonly ConcurrentQueue<JobId> discarded = new();
    private readonly ConcurrentDictionary<JobId, JobSummary> jobs = new();

    public IReadOnlyList<JobRequest> Submitted => [.. submitted];

    public IReadOnlyList<JobId> Discarded => [.. discarded];

    public int Accepted { get; set; } = int.MaxValue;

    public ValueTask<Result<JobId, JobRejection>> SubmitAsync(JobRequest request, CancellationToken cancellationToken)
    {
        if (submitted.Count >= Accepted)
        {
            return ValueTask.FromResult(Result<JobId, JobRejection>.Failure(JobRejection.AgentUnavailable));
        }

        var job = JobId.New();
        jobs[job] = new JobSummary(job, request.RepositoryPath, request.Instruction, DateTimeOffset.UnixEpoch, JobStatus.Running, request.Connection, request.Autonomy, new WorkspaceId(job.Value));
        submitted.Enqueue(request);

        return ValueTask.FromResult(Result<JobId, JobRejection>.Success(job));
    }

    public void Moved(JobId job, JobStatus status, Option<HoldReason> hold) =>
        jobs[job] = jobs[job] with { Status = status, Hold = hold };

    public ValueTask<Result<JobId, JobRejection>> DiscardAsync(JobId job, CancellationToken cancellationToken)
    {
        discarded.Enqueue(job);

        return ValueTask.FromResult(Result<JobId, JobRejection>.Success(job));
    }

    public ValueTask<Option<JobHistory>> HistoryAsync(JobId job, CancellationToken cancellationToken) =>
        ValueTask.FromResult(jobs.TryGetValue(job, out var summary) ? new JobHistory(summary, [], []) : Option<JobHistory>.None);

    public ValueTask<IReadOnlyList<JobSummary>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<IReadOnlyList<JobSummary>> ChildrenAsync(JobId parent, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Option<JobTree>> TreeAsync(JobId root, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<JobHold, JobRejection>> HoldAsync(JobId job, HoldReason reason, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<JobHold, JobRejection>> HoldRunningAsync(JobId job, HoldReason reason, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<JobContinuation, JobRejection>> ContinueAsync(JobId job, string message, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<JobContinuation, JobRejection>> ContinueOnAsync(JobId job, ConnectionName connection, string message, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<JobSteered, JobRejection>> SteerAsync(JobId job, string message, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<JobApproval, JobRejection>> ApproveAsync(JobId job, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<JobContinuation, JobRejection>> SendBackAsync(JobId job, string feedback, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<JobContinuation, JobRejection>> ResumeAsync(JobId job, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<JobContinuation, JobRejection>> HandOffAsync(JobId job, JobHandoff handoff, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<JobModelChosen, JobRejection>> ChooseModelAsync(JobId job, Avala.Agents.Contracts.Sessions.ModelChoice model, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

internal sealed record RunFacts(bool Verified, int Attempts, int Added, int Removed, decimal Spent, params ExceptionReason[] Exceptions);

internal sealed class FakeRuns : IRunEvidence, IWorkspaceChanges, IUsage
{
    private readonly ConcurrentDictionary<JobId, RunFacts> runs = new();

    public void Ran(JobId job, RunFacts facts) => runs[job] = facts;

    public ValueTask<Option<RunEvidence>> OfJobAsync(JobId job, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Of(job).Map(facts => new RunEvidence(
            job,
            new EvidenceSummary(facts.Attempts, facts.Verified ? VerificationOutcome.Passed : VerificationOutcome.Failed, [], 0, 0, []),
            facts.Exceptions)));

    public ValueTask<Result<WorkspaceDiff, WorkspaceFailure>> DiffAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<WorkspaceDiff, WorkspaceFailure>.Success(new WorkspaceDiff(
            workspace,
            "base",
            "head",
            [.. Of(new JobId(workspace.Value)).Match<FileChange[]>(
                facts => [new FileChange("calculator.txt", ChangeKind.Modified, facts.Added, facts.Removed), new FileChange("logo.png", ChangeKind.Added, Option<int>.None, Option<int>.None)],
                () => [])])));

    public Option<UsageSummary> OfJob(JobId job) =>
        Of(job).Map(facts => new UsageSummary(default, [new Cost(facts.Spent, "USD")], 0, default, []));

    public ValueTask<Result<FileDiff, WorkspaceFailure>> FileDiffAsync(WorkspaceId workspace, string path, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<IReadOnlyList<string>, WorkspaceFailure>> ConflictsAsync(WorkspaceId workspace, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<MergedWork, WorkspaceFailure>> MergeAsync(WorkspaceId workspace, string message, CancellationToken cancellationToken) => throw new NotSupportedException();

    public IReadOnlyList<ProviderUsage> ByProvider() => throw new NotSupportedException();

    public IReadOnlyList<AccountUsage> ByAccount() => throw new NotSupportedException();

    public IReadOnlyList<ConnectionUsage> ByConnection() => throw new NotSupportedException();

    public Option<UsageSummary> OfSession(SessionId session) => throw new NotSupportedException();

    private Option<RunFacts> Of(JobId job) => runs.TryGetValue(job, out var facts) ? facts : Option<RunFacts>.None;
}

internal sealed class FakeConnections : IConnections, IConnectionPreview
{
    public List<ConnectionName> Declared { get; } = [];

    public HashSet<ConnectionName> Usable { get; } = [];

    public Option<ConnectionName> Previewed { get; set; }

    public ValueTask<ConnectionCatalog> RediscoverAsync(CancellationToken cancellationToken) => CatalogAsync(cancellationToken);

    public ValueTask<ConnectionCatalog> CatalogAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ConnectionCatalog(
            ConnectionFileStatus.Applied,
            Option<ConnectionError>.None,
            [.. Declared.Select(name => new DeclaredConnection(name, "claude-code", Option<string>.None))],
            Option<ConnectionName>.None));

    public ValueTask<Result<ConnectionInfo, ConnectionError>> CheckAsync(Option<ConnectionName> connection, CancellationToken cancellationToken) =>
        ValueTask.FromResult(connection.Match(
            name => Usable.Contains(name)
                ? Result<ConnectionInfo, ConnectionError>.Success(new ConnectionInfo(name, new ProviderInfo("claude-code", "Claude Code")))
                : Result<ConnectionInfo, ConnectionError>.Failure(ConnectionError.UnknownConnection),
            () => Result<ConnectionInfo, ConnectionError>.Failure(ConnectionError.UnknownConnection)));

    public ValueTask<Result<ConnectionPreview, JobRejection>> PreviewAsync(string repository, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<ConnectionPreview, JobRejection>.Success(new ConnectionPreview(ConnectionRoute.Repository, Previewed)));

    public ValueTask<Result<ConnectionCatalog, ConnectionError>> ChangeDefaultAsync(Option<ConnectionName> connection, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<ConnectionCatalog, ConnectionError>> DeclareAsync(Option<ConnectionName> replacing, ConnectionEdit connection, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<Avala.Agents.Contracts.Capabilities.CapabilitySet, ConnectionError>> CapabilitiesAsync(Option<ConnectionName> replacing, ConnectionEdit connection, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<ConnectionCatalog, ConnectionError>> RemoveAsync(ConnectionName connection, CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class MemoryContests : IContestStore, IBestOfFile
{
    private readonly ConcurrentDictionary<ContestId, ContestRecord> contests = new();

    public Task<IReadOnlyList<ContestRecord>> ContestsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ContestRecord>>([.. contests.Values]);

    public Task KeepAsync(ContestRecord contest, CancellationToken cancellationToken)
    {
        contests[contest.Contest] = contest;

        return Task.CompletedTask;
    }

    public Task<Result<BestOf, ContestRejection>> OfRepositoryAsync(string repository, CancellationToken cancellationToken) =>
        Task.FromResult(Result<BestOf, ContestRejection>.Success(BestOf.Default));
}
