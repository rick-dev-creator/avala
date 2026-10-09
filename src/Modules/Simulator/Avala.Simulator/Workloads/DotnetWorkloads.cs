using System.Diagnostics;
using System.Globalization;
using Avala.Sdk.Processes;
using Avala.Simulator.Playback;
using Avala.Simulator.Scenarios;

namespace Avala.Simulator.Workloads;

internal sealed class DotnetWorkloads : IWorkloads
{
    private static readonly string Program =
        Path.Combine(Path.GetDirectoryName(typeof(DotnetWorkloads).Assembly.Location) ?? AppContext.BaseDirectory, "Avala.Simulator.Workload.dll");

    public async Task<string> StartAsync(IProcessLauncher launcher, Workload workload, string workingDirectory, CancellationToken cancellationToken)
    {
        if (!launcher.Start(Command(workload, workingDirectory)).TryGetValue(out var process, out var error))
        {
            return $"Could not start the process: {error}";
        }

        var line = await process.StandardOutput.ReadLineAsync(cancellationToken) ?? string.Empty;
        var reaped = ReapAsync(process);

        if (workload == Workload.Build)
        {
            await reaped;
        }

        return line;
    }

    private static ProcessStartInfo Command(Workload workload, string workingDirectory)
    {
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add(Program);
        info.ArgumentList.Add(workload == Workload.Build ? "work" : "serve");
        info.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

        return info;
    }

    private static async Task ReapAsync(Process process)
    {
        using (process)
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }
}
