using System.Diagnostics;
using System.Globalization;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Testing;

public static class Workloads
{
    private const int BlockRangeFirst = 20_000;

    private const int BlockRangeLast = 23_999;

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

    public static int FreeBlock(int size)
    {
        var blocks = (BlockRangeLast - BlockRangeFirst + 1) / size;
        var start = Random.Shared.Next(blocks);

        foreach (var index in Enumerable.Range(0, blocks).Select(offset => (start + offset) % blocks))
        {
            var first = BlockRangeFirst + (index * size);

            if (Holds(first, size))
            {
                return first;
            }
        }

        throw new InvalidOperationException($"No block of {size} free ports between {BlockRangeFirst} and {BlockRangeLast}.");
    }

    public static Option<T> OnFreePort<T>(Func<int, Option<T>> start)
        where T : notnull =>
        Enumerable.Range(0, 5).Select(_ => start(FreePort())).FirstOrDefault(started => started.IsSome, Option<T>.None);

    private static bool Holds(int first, int size)
    {
        var held = new List<System.Net.Sockets.TcpListener>();

        try
        {
            foreach (var port in Enumerable.Range(first, size))
            {
                var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
                listener.Start();
                held.Add(listener);
            }

            return true;
        }
        catch (System.Net.Sockets.SocketException)
        {
            return false;
        }
        finally
        {
            held.ForEach(listener => listener.Stop());
        }
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
