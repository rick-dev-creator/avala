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
        Assert.Contains(
            ("Warning", "Avala.Jobs.JobFiles.JobFileReader", "The repository's .avala/jobs.json is rejected because its approval is not a name"),
            LogEvents.Parse(log).Select(logged => (logged.Level, logged.Category, logged.Message)));
    }

    [Fact]
    public async Task WhatIsLoggedWhileAvalaWorksOnAJobNamesThatJobAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", """{ "approval": 7 }"""));

        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("hello"))));
        await run.SettledAsync();
        var rejected = LogEvents.Parse(await run.StopAndReadLogAsync()).First(logged => logged.Category == "Avala.Jobs.JobFiles.JobFileReader");

        Assert.Equal(job.Value.ToString(), rejected.Property("Job"));
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
        var logged = LogEvents.Parse(await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(data.Path)), Cancellation))
            .Select(entry => (entry.Level, entry.Category, entry.Message, entry.Exception.Split(Environment.NewLine)[0]));

        Assert.Equal(
            [
                ("Error", "Avala", "A task failed and nothing observed it", "System.AggregateException: One or more errors occurred. (nobody awaited me)"),
                ("Error", "Avala", "An unhandled exception reached the application", "System.InvalidOperationException: a background thread failed"),
                ("Critical", "Avala", "Avala's startup failed", "System.InvalidOperationException: a startup task failed"),
                ("Critical", "Avala", "Avala stopped on an unhandled exception", "System.InvalidOperationException: the process dies"),
            ],
            logged);
    }
}
