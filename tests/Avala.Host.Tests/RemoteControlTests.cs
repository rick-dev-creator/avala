using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class RemoteControlTests(PublishedPlugins plugins)
{
    private static readonly (string File, string Content)[] Login =
    [
        ("connections.json", """{ "default": "claude", "connections": [ { "name": "claude", "provider": "simulator", "credential": { "source": "login" } } ] }"""),
        ("connections/claude/.login", string.Empty),
    ];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RemoteControlTurnedOnForAJobWhoseIdleAgentEndedResumesItsConversationSoThePhoneReachesItsAgentAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, Login, []);
        var stopped = run.Watch<SessionStopped>();
        var phoned = run.Watch<RemoteMessageReceived>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("remote-control"))));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        await run.DeliveredAsync();
        var rested = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Sessions[^1].Session;
        run.Clock.Advance(TimeSpan.FromHours(1));
        _ = await stopped.UntilAsync(found => found.Session == rested);
        await run.DeliveredAsync();

        Outcomes.Succeeds(await run.Get<IRemoteJobs>().ControlRemotelyAsync(job, true, Cancellation));
        var phone = await phoned.UntilAsync(_ => true);
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));

        var history = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation));
        Assert.NotEqual(rested, phone.Session);
        Assert.Equal((AttemptOrigin.Device, Option<string>.Some("Also cover partial refunds")), (history.Attempts[^1].Origin, history.Attempts[^1].Guidance));
    }

    [Fact]
    public async Task RemoteControlTurnedOnAfterARestartResumesTheJobsConversationSoThePhoneReachesItsAgentAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, Login, []);
        var resumable = run.Watch<JobResumable>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("remote-control"))));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        _ = await resumable.UntilAsync(update => update.Job == job);
        await run.DeliveredAsync();
        var phoned = run.Watch<RemoteMessageReceived>();
        await run.RestartAsync(() => phoned = run.Watch<RemoteMessageReceived>());
        await run.StartedAsync();

        Outcomes.Succeeds(await run.Get<IRemoteJobs>().ControlRemotelyAsync(job, true, Cancellation));
        _ = await phoned.UntilAsync(_ => true);
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));

        var history = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation));
        Assert.Equal((AttemptOrigin.Device, Option<string>.Some("Also cover partial refunds")), (history.Attempts[^1].Origin, history.Attempts[^1].Guidance));
    }

    [Fact]
    public async Task AJobOpenForThePhoneWhenAvalaQuitIsReopenedForThePhoneWhenAvalaStartsAgainAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, Login, []);
        var resumable = run.Watch<JobResumable>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("reply"))));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        _ = await resumable.UntilAsync(update => update.Job == job);
        Outcomes.Succeeds(await run.Get<IRemoteJobs>().ControlRemotelyAsync(job, true, Cancellation));
        await run.DeliveredAsync();
        var before = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Sessions[^1].Session;
        var reported = run.Watch<RemoteControlReported>();

        await run.RestartAfterAsync(TimeSpan.FromMinutes(10), () => reported = run.Watch<RemoteControlReported>());
        var reopened = await reported.UntilAsync(update => update.Control.State == RemoteState.Connected);
        await run.DeliveredAsync();

        Assert.NotEqual(before, reopened.Session);
        Assert.Equal(JobStatus.AwaitingReview, Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary.Status);
        Assert.True(run.Get<IAgents>().IsOpen(reopened.Session), "The job open for the phone was not reopened when Avala started again");
    }

    [Fact]
    public async Task AJobContinuedFromThePhoneShowsTheMessageAsSentFromThePhoneAndAsksForReviewAgainAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, Login, []);
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("remote-control"))));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(job);
        var remote = workbench.Page["Regions"]["Header"].Items.Single(item => item.Kind == "RemoteControlViewModel");
        await run.Ui.PresentedAsync(remote.Presentation, () => remote["IsShown"].Value<bool>(), () => remote["Status"].Text);
        var offered = await run.Ui.ReadAsync(() => (remote["IsOn"].Value<bool>(), remote["Status"].Text));

        await run.Ui.RunAsync(() => remote.ExecuteAsync("ToggleCommand"));

        await workbench.ShowsAsync(() => OpenWorkbench.Texts(conversation, "MessageViewModel", "Text").Contains("Partial refunds are covered by the tests now."));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        await run.Ui.PresentedAsync(remote.Presentation, () => remote["IsConnected"].Value<bool>(), () => remote["Status"].Text);
        var prompts = await run.Ui.ReadAsync(() => conversation["Entries"].Items.Where(entry => entry.Kind == "PromptViewModel").Select(prompt => (prompt["Origin"].Text, prompt["Text"].Text)).ToList());
        var link = await run.Ui.ReadAsync(() => remote["Link"].Text);

        Assert.Equal((false, "Follow and continue this job from the Claude app or any browser."), offered);
        Assert.Equal(("From your phone", "Also cover partial refunds"), prompts[^1]);
        Assert.StartsWith("https://remote.simulator.invalid/session/", link, StringComparison.Ordinal);
    }
}
