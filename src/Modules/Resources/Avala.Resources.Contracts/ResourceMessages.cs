using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.Processes;
using Avala.Workspaces.Contracts;

namespace Avala.Resources.Contracts;

public enum ResourceError
{
    Unreadable,
    TooLarge,
    Malformed,
    UnknownField,
    InvalidInterval,
    InvalidPorts,
    InvalidRetention,
    UnknownPolicy,
    NothingToReap,
}

public enum ResourceFileStatus
{
    Absent,
    Applied,
    Rejected,
}

public enum OrphanPolicy
{
    Kill,
    Report,
}

public enum ReconcilePolicy
{
    Report,
    Clean,
}

public sealed record PortRange(int First, int Last, int PerWorktree);

public sealed record WorktreeRetention(Option<TimeSpan> Discarded, Option<TimeSpan> Failed, Option<TimeSpan> Approved);

public sealed record ResourceSettings(
    TimeSpan Sampling,
    TimeSpan DiskSampling,
    OrphanPolicy Orphans,
    PortRange Ports,
    WorktreeRetention Retention,
    ReconcilePolicy Reconcile)
{
    public ResourceFileStatus File { get; init; }

    public Option<ResourceError> Error { get; init; }
}

public sealed record ProcessUsage(int Id, string Name, long MemoryBytes, TimeSpan CpuTime, IReadOnlyList<int> Ports);

public sealed record TreeUsage(ProcessTreeId Tree, string Home, IReadOnlyList<ProcessUsage> Processes, double CpuLoad)
{
    public Option<SessionId> Session { get; init; }

    public Option<JobId> Job { get; init; }

    public Option<ConnectionName> Connection { get; init; }

    public Option<string> Provider { get; init; }
}

public sealed record FolderUsage(string Path, long Bytes)
{
    public Option<JobId> Job { get; init; }
}

public sealed record ResourceSample(DateTimeOffset At, IReadOnlyList<TreeUsage> Trees, IReadOnlyList<FolderUsage> Worktrees, long DataFolderBytes);

public sealed record ResourceUsage(int Processes, long MemoryBytes, TimeSpan CpuTime, double CpuLoad, IReadOnlyList<int> Ports, long DiskBytes);

public sealed record ConnectionResources(ConnectionName Connection, ResourceUsage Usage);

public sealed record ProviderResources(string Provider, ResourceUsage Usage);

public sealed record PortLease(string Worktree, int First, int Last);

public sealed record PortConflict(int Port, PortLease Lease, DateTimeOffset At)
{
    public Option<int> Process { get; init; }

    public Option<ProcessTreeId> Tree { get; init; }
}

public enum OrphanDisposal
{
    Killed,
    LeftRunning,
}

public sealed record OrphanReport(ProcessTreeId Tree, IReadOnlyList<ProcessUsage> Processes, OrphanDisposal Disposal, IReadOnlyList<int> Survivors, DateTimeOffset At)
{
    public Option<SessionId> Session { get; init; }

    public Option<JobId> Job { get; init; }
}

public sealed record ReclaimedWorktree(JobId Job, string Path, JobStatus Status, DateTimeOffset At);

public sealed record ResourcesSampled(ResourceSample Sample) : IIntegrationEvent;

public sealed record OrphansFound(OrphanReport Report) : IIntegrationEvent;

public sealed record OrphansReaped(OrphanReport Report) : IIntegrationEvent;

public sealed record PortsLeased(PortLease Lease) : IIntegrationEvent;

public sealed record PortsReleased(PortLease Lease) : IIntegrationEvent;

public sealed record PortConflictObserved(PortConflict Conflict) : IIntegrationEvent;

public sealed record WorktreeReclaimed(ReclaimedWorktree Reclaimed) : IIntegrationEvent;

public sealed record WorktreesReconciled(WorktreeReconciliation Found, bool Cleaned) : IIntegrationEvent;
