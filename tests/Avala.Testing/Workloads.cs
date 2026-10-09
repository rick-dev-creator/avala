using System.Diagnostics;
using System.Globalization;
using Avala.Sdk.Processes;

namespace Avala.Testing;

public static class Workloads
{
    public static string Program { get; } = Path.Combine(AppContext.BaseDirectory, "Avala.Simulator.Workload.dll");

    public static ProcessStartInfo Command(string mode, string argument)
    {
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add(Program);
        info.ArgumentList.Add(mode);
        info.ArgumentList.Add(argument);

        return info;
    }

    public static int FreePort()
    {
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        return port;
    }

    public static ProcessStartInfo Holding(string mode) => Command(mode, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

    public static async Task<(Process Process, string Line)> StartAsync(IProcessLauncher launcher, ProcessStartInfo command, CancellationToken cancellationToken)
    {
        var process = Outcomes.Succeeds(launcher.Start(command));

        return (process, await process.StandardOutput.ReadLineAsync(cancellationToken) ?? string.Empty);
    }

    public static async Task<bool> IsGoneAsync(int process)
    {
        try
        {
            using var found = Process.GetProcessById(process);

            return found.HasExited || await IsZombieAsync(process);
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static async Task<bool> IsZombieAsync(int process)
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        try
        {
            var stat = await File.ReadAllTextAsync($"/proc/{process}/stat");

            return stat[(stat.LastIndexOf(')') + 2)..].StartsWith('Z');
        }
        catch (IOException)
        {
            return true;
        }
    }
}
