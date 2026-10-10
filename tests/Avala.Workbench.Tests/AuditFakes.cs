using Avala.Agents.Contracts.Sessions;
using Avala.Autopilot.Contracts;
using Avala.Budgets.Contracts;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Permissions.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Tests;

internal sealed class FakeRunEvidence : IRunEvidence
{
    public Dictionary<JobId, RunEvidence> Runs { get; } = [];

    public ValueTask<Option<RunEvidence>> OfJobAsync(JobId job, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Runs.TryGetValue(job, out var run) ? Option<RunEvidence>.Some(run) : Option<RunEvidence>.None);
}

internal sealed class FakeChanges : IWorkspaceChanges
{
    public IReadOnlyList<FileChange> Files { get; set; } = [];

    public Dictionary<string, FileDiff> Hunks { get; } = [];

    public IReadOnlyList<string> Conflicts { get; set; } = [];

    public List<string> Calls { get; } = [];

    public ValueTask<Result<WorkspaceDiff, WorkspaceFailure>> DiffAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<WorkspaceDiff, WorkspaceFailure>.Success(new WorkspaceDiff(workspace, "ba5e", "head", Files)));

    public ValueTask<Result<FileDiff, WorkspaceFailure>> FileDiffAsync(WorkspaceId workspace, string path, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Hunks.TryGetValue(path, out var diff)
            ? Result<FileDiff, WorkspaceFailure>.Success(diff)
            : Result<FileDiff, WorkspaceFailure>.Failure(WorkspaceFailure.FileUnchanged));

    public ValueTask<Result<IReadOnlyList<string>, WorkspaceFailure>> ConflictsAsync(WorkspaceId workspace, CancellationToken cancellationToken)
    {
        Calls.Add("conflicts");

        return ValueTask.FromResult(Result<IReadOnlyList<string>, WorkspaceFailure>.Success(Conflicts));
    }

    public ValueTask<Result<MergedWork, WorkspaceFailure>> MergeAsync(WorkspaceId workspace, string message, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

internal sealed class FakeAudit : IVerifications, IPermissionAudit, IBudgets
{
    public List<VerificationReport> Reports { get; } = [];

    public List<PolicyDecision> Decisions { get; } = [];

    public List<FormDecision> Forms { get; } = [];

    public List<HumanAnswer> Answers { get; } = [];

    public List<HumanAnswer> Given { get; } = [];

    public Dictionary<SessionId, SessionAutonomy> Autonomies { get; } = [];

    public Dictionary<SessionId, SessionBudget> Budgets { get; } = [];

    public List<BudgetIntervention> Interventions { get; } = [];

    public Option<BudgetCarve> Carve { get; set; }

    public IReadOnlyList<VerificationReport> OfJob(JobId job) => Reports;

    public Option<SessionPolicy> PolicyOf(SessionId session) => Option<SessionPolicy>.None;

    public Option<SessionAutonomy> AutonomyOf(SessionId session) => Autonomies.TryGetValue(session, out var found) ? found : Option<SessionAutonomy>.None;

    public IReadOnlyList<PolicyRule> JobRulesOf(JobId job) => [];

    public IReadOnlyList<PolicyDecision> OfSession(SessionId session) => Decisions;

    IReadOnlyList<PolicyDecision> IPermissionAudit.OfJob(JobId job) => Decisions;

    public IReadOnlyList<FormDecision> FormsOfSession(SessionId session) => Forms;

    public IReadOnlyList<FormDecision> FormsOfJob(JobId job) => Forms;

    public IReadOnlyList<HumanAnswer> AnswersOfJob(JobId job) => Answers;

    public IReadOnlyList<HumanAnswer> AnswersGivenBy(JobId parent) => Given;

    public Option<SessionBudget> BudgetOf(SessionId session) => Budgets.TryGetValue(session, out var found) ? found : Option<SessionBudget>.None;

    IReadOnlyList<BudgetIntervention> IBudgets.OfJob(JobId job) => Interventions;

    public Option<BudgetCarve> CarveOf(JobId child) => Carve;

    public ValueTask<MachineBudget> MachineAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class FakeWorkspaces : IWorkspaces
{
    public Dictionary<WorkspaceId, WorkspaceInfo> Known { get; } = [];

    public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> FindAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Known.TryGetValue(workspace, out var found)
            ? Result<WorkspaceInfo, WorkspaceFailure>.Success(found)
            : Result<WorkspaceInfo, WorkspaceFailure>.Failure(WorkspaceFailure.UnknownWorkspace));

    public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> PrepareAsync(WorkspaceRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<CheckpointInfo, WorkspaceFailure>> CheckpointAsync(WorkspaceId workspace, string label, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<WorkspaceId, WorkspaceFailure>> RemoveAsync(WorkspaceId workspace, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> FindAtAsync(string folder, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<WorktreeReconciliation> ReconcileAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<WorktreeReconciliation> CleanAsync(WorktreeReconciliation found, CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class FakeRecordSources : IResources, IDelegations
{
    private static readonly ResourceUsage Nothing = new(0, 0, TimeSpan.Zero, 0, [], 0);

    public List<PortLease> Leased { get; } = [];

    public List<DelegationRecord> Delegations { get; } = [];

    public Option<ResourceSample> Latest => Option<ResourceSample>.None;

    public ResourceUsage Global() => Nothing;

    public ResourceUsage OfJob(JobId job) => Nothing;

    public ResourceUsage OfSession(SessionId session) => Nothing;

    public IReadOnlyList<ConnectionResources> ByConnection() => [];

    public IReadOnlyList<ProviderResources> ByProvider() => [];

    public IReadOnlyList<PortLease> Leases() => Leased;

    public IReadOnlyList<PortConflict> Conflicts() => [];

    public ValueTask<ResourceSettings> SettingsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public IReadOnlyList<DelegationRecord> All() => Delegations;

    public IReadOnlyList<DelegationRecord> OfParent(JobId parent) => [.. Delegations.Where(record => record.Parent == parent)];

    public Option<DelegationRecord> OfChild(JobId child) => Delegations.FirstOrDefault(record => record.Child == child).ToOption();
}
