using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Avala.Sdk.Processes;
using Avala.Testing;

namespace Avala.Simulator.Workload.Tests;

public sealed class WorkloadTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AServerListensOnThePortItsEnvironmentLeasesAsync()
    {
        var port = Workloads.FreePort();
        var command = Workloads.Holding("serve");
        command.Environment["AVALA_PORT"] = port.ToString(CultureInfo.InvariantCulture);

        var (server, listening) = await Workloads.StartAsync(UncontainedProcesses.Instance, command, Cancellation);

        try
        {
            Assert.Equal($"listening {port}", listening);
        }
        finally
        {
            server.Kill();
            await server.WaitForExitAsync(Cancellation);
            server.Dispose();
        }
    }

    [Fact]
    public async Task AHeldProcessEndsOnceItsHarnessIsGoneAsync()
    {
        var (harness, _) = await Workloads.StartAsync(UncontainedProcesses.Instance, Workloads.Command("work", "0"), Cancellation);
        await harness.WaitForExitAsync(Cancellation);

        var (held, line) = await Workloads.StartAsync(
            UncontainedProcesses.Instance,
            Workloads.Command("hold", harness.Id.ToString(CultureInfo.InvariantCulture)),
            Cancellation);
        await held.WaitForExitAsync(Cancellation);

        Assert.Equal((0, $"holding {held.Id}"), (held.ExitCode, line));
    }

    [Fact]
    public async Task AHeldProcessEndsWhenItsRunningHarnessExitsAsync()
    {
        using var harness = (await Workloads.StartAsync(UncontainedProcesses.Instance, Workloads.Holding("hold"), Cancellation)).Process;
        var (held, line) = await Workloads.StartAsync(
            UncontainedProcesses.Instance,
            Workloads.Command("hold", harness.Id.ToString(CultureInfo.InvariantCulture)),
            Cancellation);
        using var _ = held;

        harness.Kill();
        await held.WaitForExitAsync(Cancellation);

        Assert.Equal((0, $"holding {held.Id}"), (held.ExitCode, line));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task AVerdictWaitsForTheExitCodeItsListenerSendsAsync(byte verdict)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var (waiting, line) = await Workloads.StartAsync(
            UncontainedProcesses.Instance,
            Workloads.Command("verdict", port.ToString(CultureInfo.InvariantCulture)),
            Cancellation);
        using var _ = waiting;
        using var asked = await listener.AcceptTcpClientAsync(Cancellation);
        await asked.GetStream().WriteAsync(new[] { verdict }, Cancellation);
        await waiting.WaitForExitAsync(Cancellation);

        Assert.Equal((verdict, $"awaiting the verdict on {port}"), (waiting.ExitCode, line));
    }

    [Fact]
    public async Task AVerdictWhoseListenerHangsUpFailsAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var waiting = (await Workloads.StartAsync(
            UncontainedProcesses.Instance,
            Workloads.Command("verdict", port.ToString(CultureInfo.InvariantCulture)),
            Cancellation)).Process;
        (await listener.AcceptTcpClientAsync(Cancellation)).Dispose();
        await waiting.WaitForExitAsync(Cancellation);

        Assert.Equal(3, waiting.ExitCode);
    }

    [Fact]
    public async Task AnUnknownModeIsRefusedAsync()
    {
        var (refused, _) = await Workloads.StartAsync(UncontainedProcesses.Instance, Workloads.Command("unknown", "0"), Cancellation);
        await refused.WaitForExitAsync(Cancellation);

        Assert.Equal(2, refused.ExitCode);
    }
}
