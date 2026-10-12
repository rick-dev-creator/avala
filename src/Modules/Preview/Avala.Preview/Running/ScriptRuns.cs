using System.Diagnostics;
using Avala.Jobs.Contracts;
using Avala.Preview.Contracts;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Preview.Running;

internal sealed class ScriptRuns(IProcessTrees trees, RunBook runs)
{
    public const string PortVariable = "AVALA_PORT";

    public const string PortsVariable = "AVALA_PORTS";

    public async ValueTask<Result<PreviewRun, PreviewError>> StartAsync(JobId job, string worktree, Launch launch, CancellationToken cancellationToken)
    {
        var tree = await trees.OpenAsync(worktree, cancellationToken);

        if (!tree.Environment.TryGetValue(PortVariable, out var port))
        {
            _ = await trees.CloseAsync(tree.Id, cancellationToken);

            return PreviewError.NoLease;
        }

        if (!tree.Start(Command(launch, worktree, port)).TryGetValue(out var process, out _))
        {
            _ = await trees.CloseAsync(tree.Id, cancellationToken);

            return PreviewError.NotStarted;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var run = new Run(job, launch.Name, tree.Id);
        await runs.StartedAsync(run, cancellationToken);
        _ = EndedAsync(process, run);

        return new PreviewRun(job, launch.Name);
    }

    public async ValueTask StopAsync(Run run, CancellationToken cancellationToken)
    {
        await run.Tree.Match(tree => trees.CloseAsync(tree, cancellationToken).AsTask(), () => Task.FromResult<IReadOnlyList<TreeProcess>>([]));
        _ = await runs.EndedAsync(run.Job, current => current == run, cancellationToken);
    }

    private async Task EndedAsync(Process process, Run run)
    {
        using (process)
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }

        await run.Tree.Match(tree => trees.CloseAsync(tree, CancellationToken.None).AsTask(), () => Task.FromResult<IReadOnlyList<TreeProcess>>([]));
        _ = await runs.EndedAsync(run.Job, current => current == run, CancellationToken.None);
    }

    private static ProcessStartInfo Command(Launch launch, string worktree, string port)
    {
        var info = new ProcessStartInfo(launch.FileName)
        {
            WorkingDirectory = worktree,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in launch.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["PORT"] = port;
        info.Environment["HOST"] = "127.0.0.1";
        info.Environment["BROWSER"] = "none";

        return info;
    }
}
