using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Avala.Simulator.WorkloadModes;

internal static class Modes
{
    private const string Usage = "Usage: work | env <variable> | serve <harness> | spawn <harness> | hold <harness> | stubborn <harness>";

    public static Task<int> RunAsync(string[] args)
    {
        var argument = args.Length > 1 ? args[1] : string.Empty;

        return (args.Length > 0 ? args[0] : string.Empty) switch
        {
            "work" => Task.FromResult(Work()),
            "env" => Task.FromResult(Variable(argument)),
            "serve" => ServeAsync(Harness(argument)),
            "spawn" => Task.FromResult(Spawn(Harness(argument))),
            "hold" => HoldAsync(Harness(argument)),
            "stubborn" => StubbornAsync(Harness(argument)),
            _ => RefuseAsync(),
        };
    }

    private static int Work()
    {
        Console.WriteLine($"done {Enumerable.Range(1, 200_000).Sum(number => (long)number % 7)}");

        return 0;
    }

    private static int Variable(string name)
    {
        Console.WriteLine(Environment.GetEnvironmentVariable(name) ?? string.Empty);

        return 0;
    }

    private static async Task<int> ServeAsync(int harness)
    {
        using var listener = new TcpListener(IPAddress.Loopback, Port());
        listener.Start();
        Console.WriteLine($"listening {((IPEndPoint)listener.LocalEndpoint).Port}");
        await AwaitHarnessAsync(harness);

        return 0;
    }

    private static int Spawn(int harness)
    {
        using var child = Process.Start(Hold(harness));
        Console.WriteLine($"spawned {child?.Id}");

        return 0;
    }

    private static async Task<int> HoldAsync(int harness)
    {
        Console.WriteLine($"holding {Environment.ProcessId}");
        await AwaitHarnessAsync(harness);

        return 0;
    }

    private static async Task<int> StubbornAsync(int harness)
    {
        using var refusal = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            Console.WriteLine("ignored");
        });
        Console.WriteLine($"stubborn {Environment.ProcessId}");
        await AwaitHarnessAsync(harness);

        return 0;
    }

    private static async Task<int> RefuseAsync()
    {
        await Console.Error.WriteLineAsync(Usage);

        return 2;
    }

    private static int Harness(string argument) =>
        int.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0;

    private static int Port() =>
        int.TryParse(Environment.GetEnvironmentVariable("AVALA_PORT"), NumberStyles.None, CultureInfo.InvariantCulture, out var port) ? port : 0;

    private static ProcessStartInfo Hold(int harness)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath ?? "dotnet")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Avala.Simulator.Workload.dll"));
        info.ArgumentList.Add("hold");
        info.ArgumentList.Add(harness.ToString(CultureInfo.InvariantCulture));

        return info;
    }

    private static async Task AwaitHarnessAsync(int harness)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        try
        {
            using var process = Process.GetProcessById(harness);
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OperationCanceledException)
        {
        }
    }
}
