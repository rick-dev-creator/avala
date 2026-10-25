using Avala.Resources.Claims;
using System.Collections.Concurrent;
using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;
using Avala.Resources.Contracts;
using Avala.Resources.Housekeeping;
using Avala.Resources.Leasing;
using Avala.Resources.Settings;
using Avala.Resources.Tracking;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avala.Resources.Tests;

internal sealed class FixedSettings(ResourceSettings settings) : IResourceSettings
{
    public static FixedSettings Defaults { get; } = new(ResourceSettingsParser.Defaults);

    public ResourceSettings Current { get; private set; } = settings;

    public ValueTask<ResourceSettings> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Current);

    public ValueTask<Result<ResourceSettings, ResourceError>> ChangeWorktreesAsync(WorktreePolicy policy, CancellationToken cancellationToken)
    {
        Current = Current with { Worktrees = policy };

        return ValueTask.FromResult(Result<ResourceSettings, ResourceError>.Success(Current));
    }
}

internal sealed class FakeFacts : IWorktreeFacts
{
    public HashSet<JobId> ArchivedJobs { get; } = [];

    public HashSet<JobId> MergedJobs { get; } = [];

    public HashSet<JobId> AwaitingMerge { get; } = [];

    public bool Archived(JobId job) => ArchivedJobs.Contains(job);

    public bool Merged(JobId job) => MergedJobs.Contains(job);

    public bool AwaitsMerge(JobId job) => AwaitingMerge.Contains(job);
}

internal sealed class FakeListening : IListeningPorts
{
    public List<Listener> Listeners { get; } = [];

    public ValueTask<IReadOnlyList<Listener>> ListAsync(IReadOnlySet<int> owners, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<Listener>>([.. Listeners]);
}

internal sealed class FakeFolders : IFolderSizes
{
    public Dictionary<string, long> Sizes { get; } = [];

    public long Data { get; set; }

    public int Measured { get; private set; }

    public Task<long> SizeAsync(string folder, CancellationToken cancellationToken)
    {
        Measured++;

        return Task.FromResult(Sizes.GetValueOrDefault(folder));
    }

    public Task<long> DataFolderAsync(CancellationToken cancellationToken) => Task.FromResult(Data);
}

internal sealed class FakeWorkspaces : IWorkspaces, IWorkspaceShelf
{
    private readonly ConcurrentDictionary<string, WorkspaceInfo> known = new();
    private readonly ConcurrentQueue<WorkspaceId> removed = new();
    private readonly ConcurrentQueue<WorkspaceId> shelved = new();

    public HashSet<WorkspaceId> Delivered { get; } = [];

    public IReadOnlyList<WorkspaceId> Shelved => [.. shelved];

    public WorktreeReconciliation Found { get; set; } = new([], []);

    public WorkspaceFailure? Failure { get; set; }

    public IReadOnlyList<WorkspaceId> Removed => [.. removed];

    public List<WorktreeReconciliation> Cleaned { get; } = [];

    public WorkspaceInfo Add(string path, bool shelved = false)
    {
        var info = new WorkspaceInfo(WorkspaceId.New(), path, "avala/branch", new string('0', 40)) { Shelved = shelved };
        known[path] = info;

        return info;
    }

    public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> FindAtAsync(string folder, CancellationToken cancellationToken) =>
        ValueTask.FromResult(known.GetValueOrDefault(folder).ToOption().ToResult(WorkspaceFailure.UnknownWorkspace));

    public ValueTask<Result<WorkspaceId, WorkspaceFailure>> RemoveAsync(WorkspaceId workspace, CancellationToken cancellationToken)
    {
        if (Failure is { } failure)
        {
            return ValueTask.FromResult(Result<WorkspaceId, WorkspaceFailure>.Failure(failure));
        }

        removed.Enqueue(workspace);
        known.TryRemove(known.Single(entry => entry.Value.Id == workspace).Key, out _);

        return ValueTask.FromResult(Result<WorkspaceId, WorkspaceFailure>.Success(workspace));
    }

    public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> ShelveAsync(WorkspaceId workspace, CancellationToken cancellationToken)
    {
        if (Failure is { } failure)
        {
            return ValueTask.FromResult(Result<WorkspaceInfo, WorkspaceFailure>.Failure(failure));
        }

        shelved.Enqueue(workspace);
        var entry = known.Single(found => found.Value.Id == workspace);
        known[entry.Key] = entry.Value with { Shelved = true };

        return ValueTask.FromResult(Result<WorkspaceInfo, WorkspaceFailure>.Success(known[entry.Key]));
    }

    public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> RebuildAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask<Result<bool, WorkspaceFailure>> DeliveredAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<bool, WorkspaceFailure>.Success(Delivered.Contains(workspace)));

    public ValueTask<WorktreeReconciliation> ReconcileAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Found);

    public ValueTask<WorktreeReconciliation> CleanAsync(WorktreeReconciliation found, CancellationToken cancellationToken)
    {
        Cleaned.Add(found);

        return ValueTask.FromResult(found);
    }

    public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> PrepareAsync(WorkspaceRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> FindAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        ValueTask.FromResult(known.Values.FirstOrDefault(info => info.Id == workspace).ToOption().ToResult(WorkspaceFailure.UnknownWorkspace));

    public ValueTask<Result<CheckpointInfo, WorkspaceFailure>> CheckpointAsync(WorkspaceId workspace, string label, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

internal sealed class FakeCatalog : IJobCatalog
{
    public List<JobSummary> Jobs { get; } = [];

    public JobSummary Add(JobStatus status, Option<WorkspaceId> workspace, Option<DateTimeOffset> ended)
    {
        var summary = new JobSummary(JobId.New(), "/repos/shop", "Add GitHub login", DateTimeOffset.UnixEpoch, status, default, default, workspace) { Ended = ended };
        Jobs.Add(summary);

        return summary;
    }

    public ValueTask<IReadOnlyList<JobSummary>> ListAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<JobSummary>>([.. Jobs]);

    public ValueTask<Option<JobHistory>> HistoryAsync(JobId job, CancellationToken cancellationToken) => ValueTask.FromResult(Option<JobHistory>.None);

    public ValueTask<IReadOnlyList<JobSummary>> ChildrenAsync(JobId parent, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<JobSummary>>([]);

    public ValueTask<Option<JobTree>> TreeAsync(JobId root, CancellationToken cancellationToken) => ValueTask.FromResult(Option<JobTree>.None);
}

internal sealed class FakeTrees : IProcessTrees
{
    private readonly ConcurrentDictionary<ProcessTreeId, FakeTree> open = new();
    private readonly ConcurrentQueue<ProcessTreeId> closed = new();

    public IReadOnlyList<IProcessTree> Open => [.. open.Values.OrderBy(tree => tree.Id.Value)];

    public IReadOnlyList<ProcessTreeId> Closed => [.. closed];

    public FakeTree Add(string home, params TreeProcess[] members)
    {
        var tree = new FakeTree(ProcessTreeId.New(), home) { Members = [.. members] };
        open[tree.Id] = tree;

        return tree;
    }

    public ValueTask<IProcessTree> OpenAsync(string home, CancellationToken cancellationToken) => ValueTask.FromResult<IProcessTree>(Add(home));

    public Option<IProcessTree> In(string folder) => Open.LastOrDefault(tree => tree.Home == folder).ToOption();

    public Option<IProcessTree> Find(ProcessTreeId tree) => open.TryGetValue(tree, out var found) ? found : Option<IProcessTree>.None;

    public async ValueTask<IReadOnlyList<IReadOnlyList<TreeProcess>>> MembersAsync(IReadOnlyList<IProcessTree> trees, CancellationToken cancellationToken)
    {
        var members = new List<IReadOnlyList<TreeProcess>>();

        foreach (var tree in trees)
        {
            members.Add(await tree.MembersAsync(cancellationToken));
        }

        return members;
    }

    public ValueTask<IReadOnlyList<TreeProcess>> CloseAsync(ProcessTreeId tree, CancellationToken cancellationToken)
    {
        if (open.TryRemove(tree, out var closing))
        {
            closed.Enqueue(tree);

            return ValueTask.FromResult<IReadOnlyList<TreeProcess>>(closing.Unkillable);
        }

        return ValueTask.FromResult<IReadOnlyList<TreeProcess>>([]);
    }
}

internal sealed class FakeTree(ProcessTreeId id, string home) : IProcessTree
{
    public ProcessTreeId Id { get; } = id;

    public string Home { get; } = home;

    public IReadOnlyDictionary<string, string> Environment { get; } = new Dictionary<string, string>();

    public List<TreeProcess> Members { get; init; } = [];

    public List<TreeProcess> Unkillable { get; init; } = [];

    public Result<System.Diagnostics.Process, ProcessError> Start(System.Diagnostics.ProcessStartInfo info) => ProcessError.Invalid;

    public ValueTask<IReadOnlyList<TreeProcess>> MembersAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<TreeProcess>>([.. Members]);
}

internal static class Lessor
{
    public static PortLeases Leases(RecordingBus bus, FakeListening listening, ResourceSettings settings, string claims) =>
        new(new PortClaimFiles(listening, claims), new FixedSettings(settings), bus, NullLogger<PortLeases>.Instance);
}

internal sealed class FakeProjects : IProjects
{
    public ProjectMap Current { get; set; } = ProjectMap.Empty;

    public ValueTask<Result<ProjectCreated, ProjectRejection>> CreateAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<ProjectRenamed, ProjectRejection>> RenameAsync(ProjectId project, string name, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<ProjectDeleted, ProjectRejection>> DeleteAsync(ProjectId project, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<ConversationMoved, ProjectRejection>> MoveAsync(JobId job, ProjectId project, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<ConversationsArchived, ProjectRejection>> ArchiveAsync(IReadOnlyList<JobId> jobs, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<ConversationRestored, ProjectRejection>> RestoreAsync(JobId job, CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class FakePullRequests : IPullRequests
{
    public Dictionary<JobId, PullRequestWatchState> Watches { get; } = [];

    public ValueTask<Option<PullRequestOffer>> OfferAsync(JobId job, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Option<PullRequestWatchState> Of(JobId job) => Watches.TryGetValue(job, out var watch) ? watch : Option<PullRequestWatchState>.None;

    public IReadOnlyList<WakeUpRecord> WakeUpsOf(JobId job) => [];

    public Option<ForgeError> RefusalOf(JobId job) => Option<ForgeError>.None;

    public ValueTask<Result<PullRequestWatchState, ForgeError>> RefreshAsync(JobId job, CancellationToken cancellationToken) => throw new NotSupportedException();
}
