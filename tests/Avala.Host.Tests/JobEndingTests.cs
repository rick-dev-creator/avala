using Avala.Jobs.Contracts;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class JobEndingTests(PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobThatFailsRecordsWhyAndItsConversationSaysSoAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", """{ "approval": 7 }"""));
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("reply"))));
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(job);
        Assert.Equal(JobStatus.Failed, await run.SettledAsync());

        var failure = Outcomes.Present(Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary.Failure);
        await workbench.ShowsAsync(() => conversation["Composer"]["Status"].Value<JobStatus>() == JobStatus.Failed);
        var shown = await run.Ui.ReadAsync(() => Shown(conversation));

        Assert.Equal(JobFailure.ConnectionUnavailable, failure);
        Assert.Contains(shown, text => text.Contains("This job failed · no connection could run it", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnAgentProcessThatCrashesHoldsItsJobAsSessionLostWithItsAttemptInterruptedRatherThanFailedAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "crash");

        var hold = await run.HoldAsync();
        Assert.Equal(JobStatus.NeedsHelp, await run.SettledAsync());

        var history = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(run.Job, Cancellation));
        Assert.Equal((HoldReason.SessionLost, JobStatus.NeedsHelp), (hold.Reason, history.Summary.Status));
        Assert.Equal([(AttemptOrigin.Initial, AttemptOutcome.Interrupted)], history.Attempts.Select(attempt => (attempt.Origin, attempt.Outcome)));
    }

    private static List<string> Shown(Bound conversation) =>
    [
        .. conversation["Entries"].Items.SelectMany(entry => entry.Target.GetType().GetProperties()
            .Where(property => property.PropertyType == typeof(string) && property.GetIndexParameters().Length == 0)
            .Select(property => (string?)property.GetValue(entry.Target) ?? string.Empty)),
        conversation["Composer"]["Placeholder"].Text,
    ];
}
