using System.Collections.Concurrent;
using Avala.Jobs.Contracts;
using Avala.Preview.Contracts;
using Avala.Preview.Detection;
using Avala.Preview.Running;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Preview.Tests.Running;

internal sealed class FixedLaunches(Option<Launch> launch) : IProjectLaunches
{
    public ValueTask<Option<Launch>> FindAsync(string worktree, CancellationToken cancellationToken) => ValueTask.FromResult(launch);
}

internal sealed class FixedWorktrees(JobId owner, string worktree) : IJobWorktrees
{
    public ValueTask<Option<string>> FindAsync(JobId job, CancellationToken cancellationToken) =>
        ValueTask.FromResult(job == owner ? Option<string>.Some(worktree) : Option<string>.None);
}

internal sealed class RecordingPages(int port) : IPageServers
{
    public ConcurrentDictionary<JobId, bool> Network { get; } = new();

    public ConcurrentQueue<JobId> Stopped { get; } = new();

    public ValueTask<Result<int, PreviewError>> StartAsync(JobId job, string worktree, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<int, PreviewError>.Success(port));

    public ValueTask StopAsync(JobId job, CancellationToken cancellationToken)
    {
        Stopped.Enqueue(job);

        return ValueTask.CompletedTask;
    }

    public void AllowNetwork(JobId job, bool allowed) => Network[job] = allowed;
}

internal sealed class RunBench : IAsyncDisposable
{
    public static readonly IReadOnlyDictionary<string, string> Leased = new Dictionary<string, string> { ["AVALA_PORT"] = "24000", ["AVALA_PORTS"] = "24000-24009" };

    public RunBench(Option<Launch> launch, IReadOnlyDictionary<string, string> environment)
    {
        Trees = new RecordingProcessTrees(environment);
        Runs = new RunBook(Bus);
        Pages = new RecordingPages(24_010);
        Previews = new Previews(
            Book,
            Runs,
            new PreviewRunner(new FixedWorktrees(Job, Worktree.Path), new FixedLaunches(launch), new ScriptRuns(Trees, Runs), new PageRuns(Pages, Book, Runs, Bus)));
    }

    public JobId Job { get; } = JobId.New();

    public TemporaryFolder Worktree { get; } = new();

    public RecordingBus Bus { get; } = new();

    public PreviewBook Book { get; } = new();

    public RecordingProcessTrees Trees { get; }

    public RunBook Runs { get; }

    public RecordingPages Pages { get; }

    public Previews Previews { get; }

    public static Launch Workload(string mode, string argument) =>
        new("dev", LaunchKind.Script, Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", [Workloads.Program, mode, argument]);

    public async ValueTask DisposeAsync()
    {
        await Trees.DisposeAsync();
        await Worktree.DisposeAsync();
    }
}
