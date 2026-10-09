using System.Collections.Concurrent;
using Avala.Resources.Contracts;
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

    public ValueTask<ResourceSettings> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(settings);
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

internal sealed class FakeWorkspaces : IWorkspaces
{
    private readonly ConcurrentDictionary<string, WorkspaceInfo> known = new();
    private readonly ConcurrentQueue<WorkspaceId> removed = new();

    public WorktreeReconciliation Found { get; set; } = new([], []);

    public WorkspaceFailure? Failure { get; set; }

    public IReadOnlyList<WorkspaceId> Removed => [.. removed];

    public List<WorktreeReconciliation> Cleaned { get; } = [];

    public WorkspaceInfo Add(string path)
    {
        var info = new WorkspaceInfo(WorkspaceId.New(), path, "avala/branch", new string('0', 40));
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

    public ValueTask<WorktreeReconciliation> ReconcileAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Found);

    public ValueTask<WorktreeReconciliation> CleanAsync(WorktreeReconciliation found, CancellationToken cancellationToken)
    {
        Cleaned.Add(found);

        return ValueTask.FromResult(found);
    }

    public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> PrepareAsync(WorkspaceRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> FindAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask<Result<CheckpointInfo, WorkspaceFailure>> CheckpointAsync(WorkspaceId workspace, string label, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
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
    public static PortLeases Leases(RecordingBus bus, FakeListening listening, ResourceSettings settings) =>
        new(listening, new FixedSettings(settings), bus, NullLogger<PortLeases>.Instance);
}
