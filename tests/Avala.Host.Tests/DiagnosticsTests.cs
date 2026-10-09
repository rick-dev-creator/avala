using Avala.Host.Composition;
using Avala.Jobs.Contracts;
using Avala.Runtime.Diagnostics;
using Avala.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Host.Tests;

public sealed class DiagnosticsTests(PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AWarningOfAModuleReachesTheLogFileInTheDataFolderAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", """{ "approval": 7 }"""));

        Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("hello"))));
        var settled = await run.SettledAsync();
        var log = await run.StopAndReadLogAsync();

        Assert.Equal(JobStatus.Failed, settled);
        Assert.Contains("Warning Avala.Jobs.JobFiles.JobFileReader: The repository's .avala/jobs.json is rejected because its approval is not a name", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoppingWithAnAgentSessionOpenStopsItBeforeTheEventBusSoNoEventIsDroppedAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "waiting-permission");
        Assert.Equal(Permissions.Contracts.DecisionDelivery.LeftToHuman, (await run.DecisionAsync()).Delivery);

        var log = await run.StopAndReadLogAsync();

        Assert.DoesNotContain("was published after the event bus stopped", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnhandledExceptionsAndAFailedStartupAreLoggedAsErrorsAsync()
    {
        await using var data = new TemporaryFolder();
        var log = new LogFile(data.Path, new FakeTimeProvider(), LogRedaction.From(new Dictionary<string, string>()), LogFile.Limit);
        var crashes = new CrashLog(log);

        crashes.Unobserved(new UnobservedTaskExceptionEventArgs(new AggregateException(new InvalidOperationException("nobody awaited me"))));
        crashes.Unhandled(new UnhandledExceptionEventArgs(new InvalidOperationException("a background thread failed"), false));
        await crashes.ObserveAsync(Task.FromException(new InvalidOperationException("a startup task failed")));
        await log.DisposeAsync();
        crashes.Unhandled(new UnhandledExceptionEventArgs(new InvalidOperationException("the process dies"), true));
        var text = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(data.Path)), Cancellation);

        Assert.Contains("Error Avala: A task failed and nothing observed it", text, StringComparison.Ordinal);
        Assert.Contains("nobody awaited me", text, StringComparison.Ordinal);
        Assert.Contains("Error Avala: An unhandled exception reached the application", text, StringComparison.Ordinal);
        Assert.Contains($"{LogLevel.Critical} Avala: Avala's startup failed{Environment.NewLine}System.InvalidOperationException: a startup task failed", text, StringComparison.Ordinal);
        Assert.Contains($"{LogLevel.Critical} Avala: Avala stopped on an unhandled exception{Environment.NewLine}System.InvalidOperationException: the process dies", text, StringComparison.Ordinal);
    }
}
