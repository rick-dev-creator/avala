using System.Collections.Concurrent;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Handoffs.Contracts;
using Avala.Handoffs.Policy;
using Avala.Handoffs.Watching;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Transcripts.Contracts;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Handoffs.Tests.Watching;

internal sealed class Journal<T>
{
    private readonly ConcurrentQueue<T> entries = new();
    private TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<T> Entries => [.. entries];

    public void Add(T entry)
    {
        entries.Enqueue(entry);
        Interlocked.Exchange(ref changed, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
    }

    public async Task<T> WaitForAsync(Func<T, bool> match)
    {
        while (true)
        {
            var signal = Volatile.Read(ref changed);

            if (entries.FirstOrDefault(match) is { } found)
            {
                return found;
            }

            await signal.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }
}

internal sealed class KeptHandoffs : IHandoffStore
{
    private readonly ConcurrentDictionary<JobId, KeptWait> waits = new();

    public Journal<string> Calls { get; } = new();

    public List<HandoffRecord> Handoffs { get; } = [];

    public IReadOnlyCollection<KeptWait> Waits => [.. waits.Values];

    public Task<IReadOnlyList<HandoffRecord>> HandoffsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<HandoffRecord>>([.. Handoffs]);

    public Task AddAsync(HandoffRecord handoff, CancellationToken cancellationToken)
    {
        Handoffs.Add(handoff);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<KeptWait>> WaitsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<KeptWait>>([.. waits.Values]);

    public Task KeepAsync(KeptWait wait, CancellationToken cancellationToken)
    {
        waits[wait.Job] = wait;
        Calls.Add($"keep {wait.Job.Value}");

        return Task.CompletedTask;
    }

    public Task DropAsync(JobId job, CancellationToken cancellationToken)
    {
        waits.TryRemove(job, out _);
        Calls.Add($"drop {job.Value}");

        return Task.CompletedTask;
    }
}

internal sealed record JobCall(string Operation, JobId Job, string Text, Option<JobHandoff> Handoff);

internal sealed class MovingJobs : IJobs
{
    public Journal<JobCall> Calls { get; } = new();

    public Option<JobRejection> Refusal { get; set; }

    public ValueTask<Result<JobContinuation, JobRejection>> ContinueAsync(JobId job, string message, CancellationToken cancellationToken)
    {
        Calls.Add(new JobCall("continue", job, message, Option<JobHandoff>.None));

        return ContinuedAsync(job, ContinuedIn.ResumedConversation);
    }

    public ValueTask<Result<JobContinuation, JobRejection>> HandOffAsync(JobId job, JobHandoff handoff, CancellationToken cancellationToken)
    {
        Calls.Add(new JobCall("hand off", job, handoff.Brief, handoff));

        return ContinuedAsync(job, ContinuedIn.NewConversation);
    }

    public ValueTask<Result<JobId, JobRejection>> SubmitAsync(JobRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<JobHold, JobRejection>> HoldAsync(JobId job, HoldReason reason, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<JobContinuation, JobRejection>> ContinueOnAsync(JobId job, ConnectionName connection, string message, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask<Result<JobSteered, JobRejection>> SteerAsync(JobId job, string message, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<JobId, JobRejection>> DiscardAsync(JobId job, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<JobApproval, JobRejection>> ApproveAsync(JobId job, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<JobContinuation, JobRejection>> SendBackAsync(JobId job, string feedback, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<JobContinuation, JobRejection>> ResumeAsync(JobId job, CancellationToken cancellationToken) => throw new NotSupportedException();

    private ValueTask<Result<JobContinuation, JobRejection>> ContinuedAsync(JobId job, ContinuedIn conversation) =>
        ValueTask.FromResult(Refusal.Match(
            Result<JobContinuation, JobRejection>.Failure,
            () => Result<JobContinuation, JobRejection>.Success(new JobContinuation(job, SessionId.New(), conversation))));
}

internal sealed class KnownJobs : IJobCatalog
{
    public ConcurrentDictionary<JobId, JobHistory> Histories { get; } = new();

    public ValueTask<Option<JobHistory>> HistoryAsync(JobId job, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Histories.TryGetValue(job, out var history) ? history : Option<JobHistory>.None);

    public ValueTask<IReadOnlyList<JobSummary>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<IReadOnlyList<JobSummary>> ChildrenAsync(JobId parent, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Option<JobTree>> TreeAsync(JobId root, CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class Worktrees : IWorkspaces, IWorkspaceChanges
{
    public const string Path = "/worktrees/job";

    public Option<IReadOnlyList<FileChange>> Changed { get; set; }

    public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> FindAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<WorkspaceInfo, WorkspaceFailure>.Success(new WorkspaceInfo(workspace, Path, "avala/job", "base")));

    public ValueTask<Result<WorkspaceDiff, WorkspaceFailure>> DiffAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Changed.Match(
            files => Result<WorkspaceDiff, WorkspaceFailure>.Success(new WorkspaceDiff(workspace, "base", "head", files)),
            () => Result<WorkspaceDiff, WorkspaceFailure>.Failure(WorkspaceFailure.GitFailed)));

    public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> PrepareAsync(WorkspaceRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<CheckpointInfo, WorkspaceFailure>> CheckpointAsync(WorkspaceId workspace, string label, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask<Result<WorkspaceId, WorkspaceFailure>> RemoveAsync(WorkspaceId workspace, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> FindAtAsync(string folder, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<WorktreeReconciliation> ReconcileAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<WorktreeReconciliation> CleanAsync(WorktreeReconciliation found, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<FileDiff, WorkspaceFailure>> FileDiffAsync(WorkspaceId workspace, string path, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<IReadOnlyList<string>, WorkspaceFailure>> ConflictsAsync(WorkspaceId workspace, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<MergedWork, WorkspaceFailure>> MergeAsync(WorkspaceId workspace, string message, CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class FixedRules : ILimitRules
{
    public LimitRules Rules { get; set; } = LimitRules.Default;

    public ValueTask<LimitRules> OfWorktreeAsync(string worktree, CancellationToken cancellationToken) => ValueTask.FromResult(Rules);
}

internal sealed class DeclaredConnections : IConnections
{
    public List<DeclaredConnection> Declared { get; } = [];

    public HashSet<ConnectionName> Unusable { get; } = [];

    public ValueTask<ConnectionCatalog> CatalogAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ConnectionCatalog(ConnectionFileStatus.Applied, Option<ConnectionError>.None, [.. Declared], Option<ConnectionName>.None));

    public ValueTask<Result<ConnectionInfo, ConnectionError>> CheckAsync(Option<ConnectionName> connection, CancellationToken cancellationToken) =>
        ValueTask.FromResult(connection.Match(
            name => Unusable.Contains(name) || !Declared.Exists(declared => declared.Name == name)
                ? Result<ConnectionInfo, ConnectionError>.Failure(ConnectionError.UnknownConnection)
                : Result<ConnectionInfo, ConnectionError>.Success(new ConnectionInfo(name, new ProviderInfo(Declared.First(declared => declared.Name == name).Provider, "Agent"))),
            () => Result<ConnectionInfo, ConnectionError>.Failure(ConnectionError.UnknownConnection)));

    public ValueTask<Result<ConnectionCatalog, ConnectionError>> ChangeDefaultAsync(Option<ConnectionName> connection, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask<Result<ConnectionCatalog, ConnectionError>> DeclareAsync(Option<ConnectionName> replacing, ConnectionEdit connection, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask<Result<CapabilitySet, ConnectionError>> CapabilitiesAsync(Option<ConnectionName> replacing, ConnectionEdit connection, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask<Result<ConnectionCatalog, ConnectionError>> RemoveAsync(ConnectionName connection, CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class MeasuredUsage : IUsage, IUsageSessions
{
    public ConcurrentDictionary<ConnectionName, ConnectionUsage> Connections { get; } = new();

    public List<UsageSession> Opened { get; } = [];

    public Dictionary<SessionId, UsageSummary> Spent { get; } = [];

    public IReadOnlyList<ConnectionUsage> ByConnection() => [.. Connections.Values];

    public Option<UsageSummary> OfSession(SessionId session) => Spent.TryGetValue(session, out var spent) ? spent : Option<UsageSummary>.None;

    public IReadOnlyList<UsageSession> Sessions() => [.. Opened];

    public IReadOnlyList<ProviderUsage> ByProvider() => throw new NotSupportedException();

    public IReadOnlyList<AccountUsage> ByAccount() => throw new NotSupportedException();

    public Option<UsageSummary> OfJob(JobId job) => throw new NotSupportedException();
}

internal sealed class Audit : IPermissionAudit, IVerifications, ITranscripts
{
    public List<PolicyDecision> Decisions { get; } = [];

    public List<HumanAnswer> Answers { get; } = [];

    public List<FormDecision> Forms { get; } = [];

    public IReadOnlyList<PolicyDecision> OfJob(JobId job) => [.. Decisions.Where(decision => decision.Job == Option<JobId>.Some(job))];

    public IReadOnlyList<HumanAnswer> AnswersOfJob(JobId job) => [.. Answers.Where(answer => answer.Job == Option<JobId>.Some(job))];

    public IReadOnlyList<FormDecision> FormsOfJob(JobId job) => [.. Forms.Where(form => form.Job == Option<JobId>.Some(job))];

    IReadOnlyList<VerificationReport> IVerifications.OfJob(JobId job) => [];

    public ValueTask<IReadOnlyList<KeptFact>> EarlierRunsAsync(JobId job, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<KeptFact>>([]);

    public Option<SessionPolicy> PolicyOf(SessionId session) => throw new NotSupportedException();

    public Option<SessionAutonomy> AutonomyOf(SessionId session) => throw new NotSupportedException();

    public IReadOnlyList<PolicyRule> JobRulesOf(JobId job) => throw new NotSupportedException();

    public IReadOnlyList<PolicyDecision> OfSession(SessionId session) => throw new NotSupportedException();

    public IReadOnlyList<FormDecision> FormsOfSession(SessionId session) => throw new NotSupportedException();

    public IReadOnlyList<HumanAnswer> AnswersGivenBy(JobId parent) => throw new NotSupportedException();
}
