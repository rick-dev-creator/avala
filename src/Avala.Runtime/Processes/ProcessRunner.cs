using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Runtime.Processes;

internal sealed class ProcessRunner : IProcessRunner, IAsyncDisposable
{
    private readonly IProcessTrees trees;
    private readonly CancellationTokenSource stopping = new();
    private readonly CancellationToken stopped;
    private ImmutableHashSet<Task> running = [];

    public ProcessRunner(IProcessTrees trees)
    {
        this.trees = trees;
        stopped = stopping.Token;
    }

    public ValueTask<Result<ProcessOutcome, ProcessError>> RunAsync(
        ProcessRequest request,
        CancellationToken cancellationToken)
    {
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = AdmittedAsync(admitted.Task, request, cancellationToken);
        ImmutableInterlocked.Update(ref running, runs => runs.Add(run));
        _ = run.ContinueWith(ended => ImmutableInterlocked.Update(ref running, runs => runs.Remove(ended)), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        admitted.SetResult();

        return new(run);
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        await Task.WhenAll(Volatile.Read(ref running)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        stopping.Dispose();
    }

    private async Task<Result<ProcessOutcome, ProcessError>> AdmittedAsync(Task admitted, ProcessRequest request, CancellationToken cancellationToken)
    {
        await admitted;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopped);

        return await RunUntilStoppedAsync(request, linked.Token);
    }

    private async Task<Result<ProcessOutcome, ProcessError>> RunUntilStoppedAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var launcher = request.WorkingDirectory
            .Bind(trees.In)
            .Match(tree => (IProcessLauncher)tree, () => UncontainedProcesses.Instance);

        if (!launcher.Start(Describe(request)).TryGetValue(out var started, out var error))
        {
            return error;
        }

        using var process = started;

        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var failure = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(cancellationToken);

            return new ProcessOutcome(process.ExitCode, await output.WaitAsync(cancellationToken), await failure.WaitAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, failure);
            throw;
        }
    }

    private static ProcessStartInfo Describe(ProcessRequest request)
    {
        var info = new ProcessStartInfo(request.FileName)
        {
            WorkingDirectory = request.WorkingDirectory.Match(directory => directory, () => string.Empty),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in request.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in request.Environment)
        {
            info.Environment[name] = value;
        }

        return info;
    }
}
