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
    public async Task<Option<JobRecord>> ReadAsync(JobId job, CancellationToken cancellationToken) =>
        await (await catalog.HistoryAsync(job, cancellationToken)).Match(
            async history => Option<JobRecord>.Some(await RecordAsync(history, cancellationToken)),
            () => Task.FromResult(Option<JobRecord>.None));

    private async Task<JobRecord> RecordAsync(JobHistory history, CancellationToken cancellationToken)
    {
        var workspace = await history.Summary.Workspace.Match(
            async id => (await workspaces.FindAsync(id, cancellationToken)).Match(Option<WorkspaceInfo>.Some, _ => Option<WorkspaceInfo>.None),
            () => Task.FromResult(Option<WorkspaceInfo>.None));
        var parent = await history.Summary.Parent.Match(
            async id => (await catalog.HistoryAsync(id, cancellationToken)).Map(found => found.Summary),
            () => Task.FromResult(Option<JobSummary>.None));

        return new JobRecord(history, workspace, workspace.Bind(LeaseOf))
        {
            Parent = parent,
            Children = await catalog.ChildrenAsync(history.Summary.Job, cancellationToken),
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
