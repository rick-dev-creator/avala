using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Inspection;

internal sealed record JobRecord(JobHistory History, Option<WorkspaceInfo> Workspace, Option<PortLease> Ports)
{
    public Option<JobSummary> Parent { get; init; }

    public IReadOnlyList<JobSummary> Children { get; init; } = [];

    public IReadOnlyList<DelegationRecord> Delegated { get; init; } = [];
}

internal sealed class JobRecords(IJobCatalog catalog, IWorkspaces workspaces, IResources resources, IDelegations delegations)
{
    public async Task<Option<JobRecord>> HistoryAsync(JobId job, CancellationToken cancellationToken) =>
        (await catalog.HistoryAsync(job, cancellationToken)).Map(history => new JobRecord(history, Option<WorkspaceInfo>.None, Option<PortLease>.None));

    public async Task<JobRecord> CompleteAsync(JobRecord record, CancellationToken cancellationToken)
    {
        var history = record.History;
        var workspace = await history.Summary.Workspace.Match(
            async id => (await workspaces.FindAsync(id, cancellationToken)).Match(Option<WorkspaceInfo>.Some, _ => Option<WorkspaceInfo>.None),
            () => Task.FromResult(Option<WorkspaceInfo>.None));
        var parent = await history.Summary.Parent.Match(
            async id => (await catalog.HistoryAsync(id, cancellationToken)).Map(found => found.Summary),
            () => Task.FromResult(Option<JobSummary>.None));

        return record with
        {
            Workspace = workspace,
            Ports = workspace.Bind(LeaseOf),
            Parent = parent,
            Children = [.. (await catalog.ChildrenAsync(history.Summary.Job, cancellationToken)).Where(child => !child.Companion)],
            Delegated = delegations.OfParent(history.Summary.Job),
        };
    }

    private Option<PortLease> LeaseOf(WorkspaceInfo workspace) =>
        resources.Leases().FirstOrDefault(lease => SamePath(lease.Worktree, workspace.Path)).ToOption();

    private static bool SamePath(string one, string other) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(one)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(other)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
