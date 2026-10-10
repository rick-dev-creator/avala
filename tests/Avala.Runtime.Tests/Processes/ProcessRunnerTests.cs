using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Runtime.Tests.Processes;

public sealed class ProcessRunnerTests
{
    private static readonly IProcessRunner Runner =
        new ServiceCollection().AddRuntime(new AvalaPaths(Path.GetTempPath())).BuildServiceProvider().GetRequiredService<IProcessRunner>();

    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(30);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CapturesTheOutputOfASuccessfulProcessAsync()
    {
        var outcome = Outcomes.Succeeds(await Runner.RunAsync(new ProcessRequest("git", ["--version"]), Cancellation));

        Assert.True(outcome.Succeeded);
        Assert.StartsWith("git version", outcome.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsAFailingExitCodeWithItsErrorOutputAsync()
    {
        var outcome = Outcomes.Succeeds(await Runner.RunAsync(new ProcessRequest("git", ["no-such-command"]), Cancellation));

        Assert.False(outcome.Succeeded);
        Assert.Contains("no-such-command", outcome.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsAMissingExecutableAsync() =>
        Assert.Equal(
            ProcessError.NotFound,
            Outcomes.FailsWith(await Runner.RunAsync(new ProcessRequest("avala-no-such-executable", []), Cancellation)));

    [Fact]
    public async Task CancellingStopsAProcessThatWouldWaitForeverAsync()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);

        var running = Runner.RunAsync(new ProcessRequest("git", ["cat-file", "--batch"]), cancellation.Token).AsTask();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    [Fact]
    public async Task CancellingReturnsOnlyOnceTheProcessHasEndedAndLetGoOfWhatItHeldAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var command = Workloads.Command("verdict", ((IPEndPoint)listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);

        var running = Runner.RunAsync(new ProcessRequest(command.FileName, [.. command.ArgumentList]), cancellation.Token).AsTask();
        using var held = await listener.AcceptTcpClientAsync(Cancellation).AsTask().WaitAsync(HangGuard, Cancellation);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);

        Assert.True(held.Client.Poll(0, SelectMode.SelectRead) && held.Available == 0, "The process still held its connection when cancelling returned");
    }
}
