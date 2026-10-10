using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Resources.Contracts;

public interface IResources
{
    Option<ResourceSample> Latest { get; }

    ResourceUsage Global();

    ResourceUsage OfJob(JobId job);

    ResourceUsage OfSession(SessionId session);

    IReadOnlyList<ConnectionResources> ByConnection();

    IReadOnlyList<ProviderResources> ByProvider();

    IReadOnlyList<PortLease> Leases();

    IReadOnlyList<PortConflict> Conflicts();

    ValueTask<ResourceSettings> SettingsAsync(CancellationToken cancellationToken);
}

public interface IPortLeases
{
    ValueTask<Option<PortLease>> LeaseAsync(string holder, CancellationToken cancellationToken);

    Task ReleaseAsync(string holder, CancellationToken cancellationToken);
}

public interface IOrphans
{
    IReadOnlyList<OrphanReport> Audit();

    IReadOnlyList<OrphanReport> OfJob(JobId job);

    ValueTask<Result<IReadOnlyList<OrphanReport>, ResourceError>> ReapAsync(JobId job, CancellationToken cancellationToken);
}

public interface IWorktreeHousekeeping
{
    IReadOnlyList<ReclaimedWorktree> Reclaimed();

    ValueTask<WorktreeReconciliation> ReconcileAsync(CancellationToken cancellationToken);

    ValueTask<WorktreeReconciliation> CleanAsync(CancellationToken cancellationToken);
}
