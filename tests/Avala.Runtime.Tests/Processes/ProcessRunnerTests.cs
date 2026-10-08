using Avala.Sdk.Processes;
using Avala.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Runtime.Tests.Processes;

public sealed class ProcessRunnerTests
{
    private static readonly IProcessRunner Runner =
        new ServiceCollection().AddRuntime().BuildServiceProvider().GetRequiredService<IProcessRunner>();

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
}
