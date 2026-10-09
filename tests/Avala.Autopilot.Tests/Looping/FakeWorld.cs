using System.Collections.Concurrent;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Autopilot.Approving;
using Avala.Autopilot.Contracts;
using Avala.Autopilot.Evidence;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Autopilot.Tests.Looping;

internal sealed class FakeEvidence : IVerifications, IPermissionAudit
{
    private readonly ConcurrentDictionary<JobId, IReadOnlyList<VerificationReport>> reports = new();
    private readonly ConcurrentDictionary<JobId, IReadOnlyList<PolicyDecision>> decisions = new();
    private readonly ConcurrentDictionary<JobId, IReadOnlyList<FormDecision>> forms = new();
    private readonly ConcurrentDictionary<SessionId, SessionAutonomy> autonomies = new();

    public void Verified(JobId job, params VerificationReport[] verified) => reports[job] = verified;

    public void Decided(JobId job, params PolicyDecision[] decided) => decisions[job] = decided;

    public void Formed(JobId job, params FormDecision[] decided) => forms[job] = decided;

    public void Applied(SessionAutonomy autonomy) => autonomies[autonomy.Session] = autonomy;

    public IReadOnlyList<VerificationReport> OfJob(JobId job) => reports.GetValueOrDefault(job, []);

    IReadOnlyList<PolicyDecision> IPermissionAudit.OfJob(JobId job) => decisions.GetValueOrDefault(job, []);

    public IReadOnlyList<FormDecision> FormsOfJob(JobId job) => forms.GetValueOrDefault(job, []);

    public Option<SessionAutonomy> AutonomyOf(SessionId session) => autonomies.TryGetValue(session, out var applied) ? applied : Option<SessionAutonomy>.None;

    public Option<SessionPolicy> PolicyOf(SessionId session) => Option<SessionPolicy>.None;

    public IReadOnlyList<PolicyRule> SessionRulesOf(SessionId session) => [];

    public IReadOnlyList<PolicyDecision> OfSession(SessionId session) => [];

    public IReadOnlyList<FormDecision> FormsOfSession(SessionId session) => [];

    public IReadOnlyList<HumanAnswer> AnswersOfJob(JobId job) => [];
}

internal sealed class FakeWork : IWorkspaces, IWorkspaceChanges
{
    private readonly ConcurrentDictionary<WorkspaceId, IReadOnlyList<string>> changes = new();

    public void Changed(JobId job, params string[] files) => changes[new WorkspaceId(job.Value)] = files;

    public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> FindAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<WorkspaceInfo, WorkspaceFailure>.Success(new WorkspaceInfo(workspace, $"/worktrees/{workspace.Value}", "avala/job", "ba5e")));

    public ValueTask<Result<WorkspaceDiff, WorkspaceFailure>> DiffAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        ValueTask.FromResult(changes.TryGetValue(workspace, out var files)
            ? Result<WorkspaceDiff, WorkspaceFailure>.Success(new WorkspaceDiff(workspace, "ba5e", "head", [.. files.Select(file => new FileChange(file, ChangeKind.Added, 1, 0))]))
            : Result<WorkspaceDiff, WorkspaceFailure>.Failure(WorkspaceFailure.GitFailed));

    public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> PrepareAsync(WorkspaceRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<CheckpointInfo, WorkspaceFailure>> CheckpointAsync(WorkspaceId workspace, string label, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<WorkspaceId, WorkspaceFailure>> RemoveAsync(WorkspaceId workspace, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> FindAtAsync(string folder, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<WorktreeReconciliation> ReconcileAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<WorktreeReconciliation> CleanAsync(WorktreeReconciliation found, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<FileDiff, WorkspaceFailure>> FileDiffAsync(WorkspaceId workspace, string path, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<IReadOnlyList<string>, WorkspaceFailure>> ConflictsAsync(WorkspaceId workspace, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<MergedWork, WorkspaceFailure>> MergeAsync(WorkspaceId workspace, string message, CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class FakeUsage : IUsage, IUsageHistory
{
    private static readonly ProviderInfo Provider = new("simulator", "Simulator");

    private readonly ConcurrentDictionary<JobId, IReadOnlyList<Cost>> spent = new();

    public IReadOnlyList<UsageLimit> Limits { get; set; } = [];

    public IReadOnlyList<Cost> Window { get; set; } = [];

    public ConcurrentQueue<(DateTimeOffset From, DateTimeOffset To)> Windows { get; } = new();

    public void Spent(JobId job, params Cost[] costs) => spent[job] = costs;

    public Option<UsageSummary> OfJob(JobId job) => spent.TryGetValue(job, out var costs) ? Summary(costs, []) : Option<UsageSummary>.None;

    public IReadOnlyList<ConnectionUsage> ByConnection() => [new ConnectionUsage(new ConnectionName("work"), Provider, Summary([], Limits))];

    public ValueTask<UsagePeriod> WithinAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        Windows.Enqueue((from, to));

        return ValueTask.FromResult(new UsagePeriod(from, to, Summary(Window, []), [], []));
    }

    public IReadOnlyList<ProviderUsage> ByProvider() => [];

    public IReadOnlyList<AccountUsage> ByAccount() => [];

    public Option<UsageSummary> OfSession(SessionId session) => Option<UsageSummary>.None;

    public ValueTask<IReadOnlyList<UsagePeriod>> DailyAsync(DateOnly first, DateOnly last, TimeZoneInfo zone, CancellationToken cancellationToken) => throw new NotSupportedException();

    private static UsageSummary Summary(IReadOnlyList<Cost> costs, IReadOnlyList<UsageLimit> limits) => new(default, costs, 0, default, limits);
}

internal sealed class FixedRules(Result<AutopilotRules, AutopilotError> rules) : IAutopilotRules
{
    public Result<AutopilotRules, AutopilotError> Rules { get; set; } = rules;

    public Task<Result<AutopilotRules, AutopilotError>> OfWorktreeAsync(string worktree, CancellationToken cancellationToken) => Task.FromResult(Rules);
}

internal sealed class MemorySource(string name) : IJobSource
{
    private readonly ConcurrentQueue<SourcedTask> tasks = new();
    private readonly ConcurrentQueue<(SourcedTask Task, TaskMark Mark)> marks = new();

    public string Name => name;

    public Option<DateTimeOffset> NextDue { get; set; }

    public Option<AutopilotError> Error { get; set; }

    public IReadOnlyList<(SourcedTask Task, TaskMark Mark)> Marks => [.. marks];

    public SourcedTask Add(string key, string instruction)
    {
        var task = new SourcedTask(name, key, Pilot.Repository, instruction);
        tasks.Enqueue(task);

        return task;
    }

    public ValueTask<Result<SourceAnswer, AutopilotError>> NextAsync(SourceRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Error.Match(
            Result<SourceAnswer, AutopilotError>.Failure,
            () => tasks.TryDequeue(out var task)
                ? new SourceAnswer(task, Option<DateTimeOffset>.None)
                : SourceAnswer.Nothing with { NextDue = NextDue }));

    public ValueTask MarkAsync(SourcedTask task, TaskMark mark, CancellationToken cancellationToken)
    {
        marks.Enqueue((task, mark));

        return ValueTask.CompletedTask;
    }
}
