using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Testing;

public sealed class RecordingProcessTrees(IReadOnlyDictionary<string, string> environment) : IProcessTrees, IAsyncDisposable
{
    public RecordingProcessTrees()
        : this(ImmutableDictionary<string, string>.Empty)
    {
    }

    private readonly ConcurrentDictionary<ProcessTreeId, RecordingTree> open = new();
    private readonly ConcurrentQueue<RecordingTree> opened = new();
    private readonly ConcurrentQueue<ProcessTreeId> closed = new();

    public IReadOnlyList<IProcessTree> Open => [.. opened.Where(tree => open.ContainsKey(tree.Id))];

    public IReadOnlyList<RecordingTree> Opened => [.. opened];

    public IReadOnlyList<ProcessTreeId> Closed => [.. closed];

    public ValueTask<IProcessTree> OpenAsync(string home, CancellationToken cancellationToken)
    {
        var tree = new RecordingTree(ProcessTreeId.New(), home, environment);
        open[tree.Id] = tree;
        opened.Enqueue(tree);

        return ValueTask.FromResult<IProcessTree>(tree);
    }

    public Option<IProcessTree> In(string folder) => Open.LastOrDefault(tree => tree.Home == folder).ToOption();

    public Option<IProcessTree> Find(ProcessTreeId tree) => open.TryGetValue(tree, out var found) ? found : Option<IProcessTree>.None;

    public async ValueTask<IReadOnlyList<TreeProcess>> CloseAsync(ProcessTreeId tree, CancellationToken cancellationToken)
    {
        if (!open.TryRemove(tree, out var closing))
        {
            return [];
        }

        closed.Enqueue(tree);
        await closing.KillAsync();

        return await closing.MembersAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var tree in open.Keys)
        {
            _ = await CloseAsync(tree, CancellationToken.None);
        }
    }
}

public sealed class RecordingTree(ProcessTreeId id, string home, IReadOnlyDictionary<string, string> environment) : IProcessTree
{
    private readonly ConcurrentQueue<int> started = new();

    public ProcessTreeId Id { get; } = id;

    public string Home { get; } = home;

    public IReadOnlyDictionary<string, string> Environment { get; } =
        environment.ToImmutableDictionary().SetItem(ProcessTreeId.Variable, id.Value.ToString());

    public IReadOnlyList<int> Started => [.. started];

    public Result<Process, ProcessError> Start(ProcessStartInfo info)
    {
        foreach (var (name, value) in Environment)
        {
            info.Environment[name] = value;
        }

        return ProcessStarts.Start(info).Map(process =>
        {
            started.Enqueue(process.Id);

            return process;
        });
    }

    public async ValueTask<IReadOnlyList<TreeProcess>> MembersAsync(CancellationToken cancellationToken)
    {
        var members = new List<TreeProcess>();

        foreach (var member in started)
        {
            if (!await Workloads.IsGoneAsync(member))
            {
                using var process = Process.GetProcessById(member);
                members.Add(new TreeProcess(member, process.ProcessName, process.WorkingSet64, process.TotalProcessorTime));
            }
        }

        return members;
    }

    public async Task KillAsync()
    {
        foreach (var member in started)
        {
            try
            {
                using var process = Process.GetProcessById(member);
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or TimeoutException)
            {
            }
        }
    }
}
