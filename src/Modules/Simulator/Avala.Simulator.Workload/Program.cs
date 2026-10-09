using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

var mode = args.Length > 0 ? args[0] : string.Empty;
var harness = args.Length > 1 && int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0;

switch (mode)
{
    case "work":
        Console.WriteLine($"done {Enumerable.Range(1, 200_000).Sum(number => (long)number % 7)}");
        return 0;
    case "env":
        Console.WriteLine(Environment.GetEnvironmentVariable(args.Length > 1 ? args[1] : string.Empty) ?? string.Empty);
        return 0;
    case "serve":
        using (var listener = new TcpListener(IPAddress.Loopback, Port()))
        {
            listener.Start();
            Console.WriteLine($"listening {((IPEndPoint)listener.LocalEndpoint).Port}");
            await HoldAsync(harness);
        }

        return 0;
    case "spawn":
        using (var child = Process.Start(Hold(harness)))
        {
            Console.WriteLine($"spawned {child?.Id}");
        }

        return 0;
    case "hold":
        Console.WriteLine($"holding {Environment.ProcessId}");
        await HoldAsync(harness);
        return 0;
    default:
        await Console.Error.WriteLineAsync("Usage: work | env <variable> | serve <harness> | spawn <harness> | hold <harness>");
        return 2;
}

static int Port() =>
    int.TryParse(Environment.GetEnvironmentVariable("AVALA_PORT"), NumberStyles.None, CultureInfo.InvariantCulture, out var port) ? port : 0;

static ProcessStartInfo Hold(int harness)
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

static async Task HoldAsync(int harness)
{
    var deadline = DateTime.UtcNow.AddMinutes(10);

    while (DateTime.UtcNow < deadline && Alive(harness))
    {
        await Task.Delay(TimeSpan.FromMilliseconds(250));
    }
}

static bool Alive(int harness)
{
    try
    {
        using var process = Process.GetProcessById(harness);

        return !process.HasExited;
    }
    catch (ArgumentException)
    {
        return false;
    }
    catch (InvalidOperationException)
    {
        return false;
    }
}
