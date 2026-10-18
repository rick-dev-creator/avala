using System.Diagnostics;
using System.Globalization;
using Avala.Host.Composition;
using Avala.Runtime;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Host.Tests;

public sealed class OrderlyExitTests(PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AQuitWhoseStopOverrunsItsLimitEndsEveryProcessTreeStillOpenAsync()
    {
        using var data = new TemporaryFolder();
        using var home = new TemporaryFolder();
        await using var runtime = new ServiceCollection().AddRuntime(new AvalaPaths(data.Path)).BuildServiceProvider();
        var trees = runtime.GetRequiredService<IProcessTrees>();
        var tree = await trees.OpenAsync(home.Path, Cancellation);
        using var held = await HoldingAsync(tree);
        var clock = new FakeTimeProvider();
        var never = new TaskCompletionSource();

        var stopping = new BoundedStop(trees, clock).StopAsync(() => never.Task);
        clock.Advance(BoundedStop.Limit);

        Assert.False(await stopping.WaitAsync(TimeSpan.FromSeconds(30), Cancellation));
        await held.WaitForExitAsync(Cancellation).WaitAsync(TimeSpan.FromSeconds(30), Cancellation);
        Assert.Empty(trees.Open);
    }

    private async Task<Process> HoldingAsync(IProcessTree tree)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
        };
        start.ArgumentList.Add(Path.Combine(plugins.Directory, "Avala.Simulator", "Avala.Simulator.Workload.dll"));
        start.ArgumentList.Add("hold");
        start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        var process = Outcomes.Succeeds(tree.Start(start));
        Assert.StartsWith("holding", await process.StandardOutput.ReadLineAsync(Cancellation));

        return process;
    }
}
