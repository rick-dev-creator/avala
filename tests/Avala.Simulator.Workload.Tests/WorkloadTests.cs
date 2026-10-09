using System.Globalization;
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

    [Fact]
    public async Task AnUnknownModeIsRefusedAsync()
    {
        var (refused, _) = await Workloads.StartAsync(UncontainedProcesses.Instance, Workloads.Command("unknown", "0"), Cancellation);
        await refused.WaitForExitAsync(Cancellation);

        Assert.Equal(2, refused.ExitCode);
    }
}
