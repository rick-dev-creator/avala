using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Runtime.Processes;

internal sealed class ProcessRunner : IProcessRunner
{
    public async ValueTask<Result<ProcessOutcome, ProcessError>> RunAsync(
        ProcessRequest request,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = Describe(request) };

        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            return ProcessError.NotFound;
        }

        try
        {
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return new ProcessOutcome(process.ExitCode, await output, await error);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
    }

    private static ProcessStartInfo Describe(ProcessRequest request)
    {
        var info = new ProcessStartInfo(request.FileName)
        {
            WorkingDirectory = request.WorkingDirectory ?? string.Empty,
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
