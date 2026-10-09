using System.Collections.Concurrent;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Contracts;
using Avala.Delegation.Contracts;
using Avala.Delegation.Delegating;
using Avala.Delegation.Policy;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Delegation.Tests.Delegating;

internal sealed class FixedSelector : IConnectionSelector
{
    public ConcurrentQueue<ConnectionQuestion> Questions { get; } = [];

    public Option<ConnectionName> Chosen { get; set; }

    public ValueTask<Option<ConnectionChoice>> ChooseAsync(ConnectionQuestion question, CancellationToken cancellationToken)
    {
        Questions.Enqueue(question);

        return ValueTask.FromResult(Chosen.Map(connection => new ConnectionChoice(
            connection,
            ChoiceReason.MostCapacity,
            [.. question.Candidates.Select(candidate => new CandidateCapacity(candidate, candidate == connection ? 0 : 0.95, Option<UsageLimit>.None, 0.9, candidate == connection))],
            DateTimeOffset.UnixEpoch)));
    }
}

internal sealed class ReturningAgents : IAgents
{
    private readonly ConcurrentQueue<(SessionId Session, ToolResult Result)> results = new();

    public IReadOnlyList<(SessionId Session, ToolResult Result)> Results => [.. results];

    public bool Closed { get; set; }

    public ValueTask<Result<ItemId, AgentError>> ReturnAsync(SessionId session, ToolResult result, CancellationToken cancellationToken)
    {
        if (Closed)
        {
            return ValueTask.FromResult(Result<ItemId, AgentError>.Failure(AgentError.SessionClosed));
        }

        results.Enqueue((session, result));

        return ValueTask.FromResult(Result<ItemId, AgentError>.Success(result.Item));
    }

    public ValueTask<Result<OpenedSession, AgentError>> OpenAsync(AgentRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

    public bool IsOpen(SessionId session) => true;

    public ValueTask<Result<AgentTurn, AgentError>> SendAsync(SessionId session, string message, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<ItemId, AgentError>> RespondAsync(SessionId session, PermissionDecision decision, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask<Result<ItemId, AgentError>> AnswerAsync(SessionId session, FormAnswer answer, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<TurnId, AgentError>> InterruptAsync(SessionId session, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<AgentTurn, AgentError>> SteerAsync(SessionId session, string message, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<SessionId, AgentError>> StopAsync(SessionId session, CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class FixedRules(Result<Option<DelegationRules>, DelegationError> rules) : IDelegationRules
{
    public ValueTask<Result<Option<DelegationRules>, DelegationError>> OfWorktreeAsync(string worktree, CancellationToken cancellationToken) =>
        ValueTask.FromResult(rules);
}

internal sealed class FixedAudit : IPermissionAudit
{
    public Dictionary<SessionId, Autonomy> Effective { get; } = [];

    public Option<SessionAutonomy> AutonomyOf(SessionId session) =>
        Effective.TryGetValue(session, out var level)
            ? new SessionAutonomy(session, JobId.New(), Autonomy.Autonomous, Option<Autonomy>.None, level, Refused: false)
            : Option<SessionAutonomy>.None;

    public Option<SessionPolicy> PolicyOf(SessionId session) => Option<SessionPolicy>.None;

    public IReadOnlyList<PolicyRule> SessionRulesOf(SessionId session) => [];

    public IReadOnlyList<PolicyDecision> OfSession(SessionId session) => [];

    public IReadOnlyList<PolicyDecision> OfJob(JobId job) => [];

    public IReadOnlyList<FormDecision> FormsOfSession(SessionId session) => [];

    public IReadOnlyList<FormDecision> FormsOfJob(JobId job) => [];

    public IReadOnlyList<HumanAnswer> AnswersOfJob(JobId job) => [];
}

internal sealed class FixedUsage : IUsage
{
    public Dictionary<JobId, UsageSummary> Jobs { get; } = [];

    public Dictionary<ConnectionName, IReadOnlyList<UsageLimit>> Limits { get; } = [];

    public IReadOnlyList<ProviderUsage> ByProvider() => [];

    public IReadOnlyList<AccountUsage> ByAccount() => [];

    public IReadOnlyList<ConnectionUsage> ByConnection() =>
        [.. Limits.Select(limits => new ConnectionUsage(limits.Key, new ProviderInfo("simulator", "Simulator"), new UsageSummary(default, [], 0, default, limits.Value)))];

    public Option<UsageSummary> OfSession(SessionId session) => Option<UsageSummary>.None;

    public Option<UsageSummary> OfJob(JobId job) => Jobs.TryGetValue(job, out var spent) ? spent : Option<UsageSummary>.None;
}

internal sealed class FixedBudgets : IBudgets
{
    public Dictionary<JobId, BudgetCarve> Carves { get; } = [];

    public Option<BudgetCarve> CarveOf(JobId child) => Carves.TryGetValue(child, out var carve) ? carve : Option<BudgetCarve>.None;

    public Option<SessionBudget> BudgetOf(SessionId session) => Option<SessionBudget>.None;

    public IReadOnlyList<BudgetIntervention> OfJob(JobId job) => [];

    public ValueTask<MachineBudget> MachineAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class FixedChanges : IWorkspaceChanges
{
    public IReadOnlyList<FileChange> Files { get; set; } = [];

    public IReadOnlyList<string> Conflicting { get; set; } = [];

    public ValueTask<Result<WorkspaceDiff, WorkspaceFailure>> DiffAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<WorkspaceDiff, WorkspaceFailure>.Success(new WorkspaceDiff(workspace, "base", "head", Files)));

    public ValueTask<Result<IReadOnlyList<string>, WorkspaceFailure>> ConflictsAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<IReadOnlyList<string>, WorkspaceFailure>.Success(Conflicting));

    public ValueTask<Result<FileDiff, WorkspaceFailure>> FileDiffAsync(WorkspaceId workspace, string path, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask<Result<MergedWork, WorkspaceFailure>> MergeAsync(WorkspaceId workspace, string message, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

internal sealed class FixedVerifications : IVerifications
{
    public Dictionary<JobId, VerificationReport> Reports { get; } = [];

    public IReadOnlyList<VerificationReport> OfJob(JobId job) => Reports.TryGetValue(job, out var report) ? [report] : [];
}
