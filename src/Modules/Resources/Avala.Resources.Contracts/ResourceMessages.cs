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
    Unwritable,
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

public enum WorktreeMoment
{
    Archived,
    Delivered,
    PullRequest,
    Undelivered,
    Discarded,
    Failed,
    Idle,
    Space,
    Requested,
}

public sealed record WorktreePolicy(
    Option<TimeSpan> Archived,
    Option<TimeSpan> Delivered,
    Option<TimeSpan> PullRequest,
    Option<TimeSpan> Undelivered,
    Option<TimeSpan> Discarded,
    Option<TimeSpan> Failed)
{
    public Option<TimeSpan> DeleteDiscarded { get; init; }

    public Option<TimeSpan> DeleteFailed { get; init; }

    public Option<TimeSpan> Idle { get; init; }

    public Option<long> SpaceBudget { get; init; }

    public Option<double> DiskFloor { get; init; }

    public Option<TimeSpan> FreeAfter(WorktreeMoment moment) => moment switch
    {
        WorktreeMoment.Archived => Archived,
        WorktreeMoment.Delivered => Delivered,
        WorktreeMoment.PullRequest => PullRequest,
        WorktreeMoment.Undelivered => Undelivered,
        WorktreeMoment.Discarded => Discarded,
        WorktreeMoment.Failed => Failed,
        WorktreeMoment.Idle => Idle,
        _ => Option<TimeSpan>.None,
    };
}

public sealed record ResourceSettings(
    TimeSpan Sampling,
    TimeSpan DiskSampling,
    OrphanPolicy Orphans,
    PortRange Ports,
    WorktreePolicy Worktrees,
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

    public long BuildBytes { get; init; }
}

public sealed record ResourceSample(DateTimeOffset At, IReadOnlyList<TreeUsage> Trees, IReadOnlyList<FolderUsage> Worktrees, long DataFolderBytes)
{
    public Option<DiskSpace> Disk { get; init; }
}

public sealed record DiskSpace(long Available, long Total)
{
    public double FreeShare => Total <= 0 ? 1 : (double)Available / Total;
}

public sealed record WorktreeSpace(long Worktrees, Option<long> Budget, Option<DiskSpace> Disk, Option<double> Floor, DateTimeOffset At)
{
    public bool Within =>
        Budget.Match(budget => Worktrees <= budget, () => true)
        && Floor.Match(floor => Disk.Match(disk => disk.FreeShare >= floor, () => true), () => true);
}

public sealed record ResourceUsage(int Processes, long MemoryBytes, TimeSpan CpuTime, double CpuLoad, IReadOnlyList<int> Ports, long DiskBytes);

public sealed record ConnectionResources(ConnectionName Connection, ResourceUsage Usage);

public sealed record ProviderResources(string Provider, ResourceUsage Usage);

public sealed record PortLease(string Worktree, int First, int Last)
{
    public bool Own { get; init; }
}

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

public sealed record WorktreeDue(JobId Job, string Path, WorktreeMoment Moment, DateTimeOffset Due);

public sealed record WorktreeEvent(JobId Job, string Path, JobStatus Status, Option<WorktreeMoment> Moment, long Bytes, DateTimeOffset At)
{
    public Option<TimeSpan> Lifetime { get; init; }
}

public sealed record WorktreeDay(DateOnly Day, long Peak, int Worktrees, int Created, int Freed, long Recovered);

public sealed record WorktreeReport(IReadOnlyList<WorktreeDay> Days, IReadOnlyList<WorktreeEvent> History);

public sealed record FreedWorktree(JobId Job, string Path, JobStatus Status, WorktreeMoment Moment, long Bytes, DateTimeOffset At);

public sealed record ResourcesSampled(ResourceSample Sample) : IIntegrationEvent;

public sealed record OrphansFound(OrphanReport Report) : IIntegrationEvent;

public sealed record OrphansReaped(OrphanReport Report) : IIntegrationEvent;

public sealed record PortsLeased(PortLease Lease) : IIntegrationEvent;

public sealed record PortsReleased(PortLease Lease) : IIntegrationEvent;

public sealed record PortConflictObserved(PortConflict Conflict) : IIntegrationEvent;

public sealed record WorktreeReclaimed(ReclaimedWorktree Reclaimed) : IIntegrationEvent;

public sealed record WorktreeFreed(FreedWorktree Freed) : IIntegrationEvent;

public sealed record WorktreeSpaceShort(WorktreeSpace Space) : IIntegrationEvent;

public sealed record WorktreesReconciled(WorktreeReconciliation Found, bool Cleaned) : IIntegrationEvent;
