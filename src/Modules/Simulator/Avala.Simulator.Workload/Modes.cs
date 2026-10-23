using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Avala.Simulator.WorkloadModes;

internal static class Modes
{
    private const string Usage = "Usage: work | env <variable> | serve <harness> | spawn <harness> | hold <harness> | stubborn <harness> | verdict <port> | contains <file> <text>";

    private static readonly Dictionary<string, Func<string[], Task<int>>> Named = new(StringComparer.Ordinal)
    {
        ["work"] = _ => Task.FromResult(Work()),
        ["env"] = given => Task.FromResult(Variable(First(given))),
        ["serve"] = given => ServeAsync(Harness(First(given))),
        ["spawn"] = given => Task.FromResult(Spawn(Harness(First(given)))),
        ["hold"] = given => HoldAsync(Harness(First(given))),
        ["stubborn"] = given => StubbornAsync(Harness(First(given))),
        ["verdict"] = given => VerdictAsync(Harness(First(given))),
        ["contains"] = ContainsAsync,
    };

    public static Task<int> RunAsync(string[] args) =>
        args.Length > 0 && Named.TryGetValue(args[0], out var mode) ? mode(args[1..]) : RefuseAsync();

    private static string First(string[] given) => given.Length > 0 ? given[0] : string.Empty;

    private static async Task<int> ContainsAsync(string[] given)
    {
        if (given is not [var file, var text] || !File.Exists(file))
        {
            Console.WriteLine("missing");

            return 1;
        }

        var found = (await File.ReadAllTextAsync(file)).Contains(text, StringComparison.Ordinal);
        Console.WriteLine(found ? "found" : "missing");

        return found ? 0 : 1;
    }

    private static async Task<int> VerdictAsync(int port)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, deadline.Token);
        Console.WriteLine($"awaiting the verdict on {port}");
        var verdict = new byte[1];

        try
        {
            return await client.GetStream().ReadAsync(verdict, deadline.Token) == 1 ? verdict[0] : HungUp;
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            return HungUp;
        }
    }

    private const int HungUp = 3;

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
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Console.WriteLine($"listening {port}");
        using var stopping = new CancellationTokenSource();
        var answering = AnswerAsync(listener, Page(port), stopping.Token);
        await AwaitHarnessAsync(harness);
        await stopping.CancelAsync();
        await answering;

        return 0;
    }

    private static async Task AnswerAsync(TcpListener listener, byte[] page, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken);
                await RespondAsync(client.GetStream(), page, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task RespondAsync(NetworkStream stream, byte[] page, CancellationToken cancellationToken)
    {
        try
        {
            var request = new byte[8192];
            var read = 0;

            while (read < request.Length && !System.Text.Encoding.ASCII.GetString(request, 0, read).Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var more = await stream.ReadAsync(request.AsMemory(read), cancellationToken);

                if (more == 0)
                {
                    return;
                }

                read += more;
            }

            var head = System.Text.Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {page.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(head, cancellationToken);
            await stream.WriteAsync(page, cancellationToken);
        }
        catch (IOException)
        {
        }
    }

    private static byte[] Page(int port) =>
        System.Text.Encoding.UTF8.GetBytes(
            $$"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <title>Orders service</title>
            <style>
            body { margin: 0; font: 15px/1.5 system-ui, sans-serif; background: #f6f5f2; color: #1d1c1a; }
            main { max-width: 520px; margin: 64px auto; padding: 0 24px; }
            h1 { font-size: 22px; margin: 0 0 4px; }
            p { color: #6b6862; margin: 0 0 24px; }
            .card { background: #fff; border-radius: 12px; padding: 16px 20px; box-shadow: 0 1px 3px rgba(0,0,0,.08); }
            .row { display: flex; justify-content: space-between; padding: 8px 0; border-bottom: 1px solid #eee; }
            .row:last-child { border: 0; }
            </style>
            </head>
            <body>
            <main>
            <h1>Orders service</h1>
            <p>Running on port {{port}}, served by the simulated job.</p>
            <div class="card">
            <div class="row"><span>#1042 · Coffee beans</span><span>€ 18.00</span></div>
            <div class="row"><span>#1043 · Grinder</span><span>€ 89.00</span></div>
            <div class="row"><span>#1044 · Filters</span><span>€ 4.50</span></div>
            </div>
            </main>
            </body>
            </html>
            """);

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
