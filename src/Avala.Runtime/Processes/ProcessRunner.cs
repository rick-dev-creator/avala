using System.Diagnostics;
using System.Text;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Runtime.Processes;

internal sealed class ProcessRunner(IProcessTrees trees) : IProcessRunner
{
    public async ValueTask<Result<ProcessOutcome, ProcessError>> RunAsync(
        ProcessRequest request,
        CancellationToken cancellationToken)
    {
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

        return info;
    }
}
