using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Forges.Contracts;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

[UsesRealGit]
public sealed class LongScenarioTests(PublishedPlugins plugins)
{
    private const string Connections = """
        {
          "default": "work",
          "connections": [
            { "name": "work", "provider": "simulator", "credential": { "source": "login" } },
            { "name": "personal", "provider": "simulator", "credential": { "source": "login" } }
          ]
        }
        """;

    private static readonly TimeSpan PromptCache = TimeSpan.FromHours(1);

    private static readonly (string File, string Content)[] Machine =
    [
        ("connections.json", Connections),
        ("connections/work/.login", string.Empty),
        ("connections/personal/.login", string.Empty),
        ("forges.json", """{ "pollSeconds": 60, "forges": [{ "name": "local", "forge": "simulated" }] }"""),
    ];

    private static readonly (string Path, string Content)[] Rules =
    [
        (".avala/checks.json", """{ "checks": [ { "name": "git", "command": "git", "arguments": ["--version"], "timeoutSeconds": 60 } ] }"""),
        (".avala/permissions.json", """{ "autonomy": "autonomous" }"""),
        (".avala/jobs.json", """
            {
              "delegation": {},
              "limits": { "resumeAtReset": "auto" },
              "approval": "pull-request",
              "pullRequest": { "forge": "local", "onPullRequest": "wake-on-ci", "maxWakeUps": 3, "redeliver": "automatic" }
            }
            """),
    ];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobThatDelegatesRestsRunsOutOfTwoWindowsIsWokenByAFailingCiAndHandedOffKeepsEveryInvariantAtEveryStepAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, Machine, Rules);
        _ = await run.Repository.PublishAsync(Cancellation);
        run.Cues.Cue("This job was handed off", "reply");
        var progress = run.Watch<JobProgressed>();
        var reported = run.Watch<ChildReported>();
        var stopped = run.Watch<SessionStopped>();
        var waits = run.Watch<JobWaitsForReset>();
        var resumed = run.Watch<JobResumedAtReset>();
        var opened = run.Watch<PullRequestOpened>();
        var wakeUps = run.Watch<PullRequestWakeUp>();
        var started = run.Clock.GetUtcNow();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: long-haul] [forge: ci-fails-once] Ship the release") { Connection = new ConnectionName("work") }));

        var report = Outcomes.Present((await reported.UntilAsync(_ => true)).Delegation.Report);
        await SettledAsync(run, progress, job, JobStatus.AwaitingReview);
        Assert.Equal(ChildOutcome.Integrated, report.Outcome);

        await RestsAsync(run, stopped, job);

        Assert.Equal(ContinuedIn.ResumedConversation, Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, "Write the to-do list too.", Cancellation)).Conversation);
        await SettledAsync(run, progress, job, JobStatus.NeedsHelp);
        var firstWait = (await waits.UntilAsync(found => found.Wait.Job == job)).Wait;
        run.AdvanceTo(Outcomes.Present(firstWait.ResumesAt));
        Assert.Equal(ContinuedIn.SameSession, (await resumed.UntilAsync(found => found.Job == job)).Conversation);
        await SettledAsync(run, progress, job, JobStatus.AwaitingReview);

        _ = Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(job, Cancellation));
        var pullRequest = (await opened.UntilAsync(found => found.Job == job)).PullRequest;
        await KeptAsync(run);
        run.AdvanceTo(Outcomes.Present(Outcomes.Present(run.Get<IPullRequests>().Of(job)).NextPoll));
        var wakeUp = (await wakeUps.UntilAsync(found => found.WakeUp.Job == job)).WakeUp;
        Assert.Equal((WakeReason.ChecksFailed, Option<ContinuedIn>.Some(ContinuedIn.ResumedConversation)), (wakeUp.Reason, wakeUp.Conversation));
        await SettledAsync(run, progress, job, JobStatus.AwaitingReview);
        Assert.Equal(pullRequest.Number, (await opened.UntilAsync(found => found.Job == job)).PullRequest.Number);
        await KeptAsync(run);

        Assert.Equal(ContinuedIn.ResumedConversation, Outcomes.Succeeds(await run.Get<IJobs>().ContinueAsync(job, "Start the changelog.", Cancellation)).Conversation);
        await SettledAsync(run, progress, job, JobStatus.NeedsHelp);
        var secondWait = (await waits.UntilAsync(found => found.Wait.Job == job)).Wait;
        Assert.Equal(ContinuedIn.NewConversation, Outcomes.Succeeds(await run.Get<IHandoffRequests>().HandOffAsync(job, new ConnectionName("personal"), Cancellation)).Conversation);
        await SettledAsync(run, progress, job, JobStatus.AwaitingReview);
        await RestsAsync(run, stopped, job);
        _ = Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(job, Cancellation));
        await KeptAsync(run);

        var history = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation));
        Assert.Equal((JobStatus.Approved, Option<ConnectionName>.Some(new ConnectionName("personal"))), (history.Summary.Status, history.Summary.Connection));
        Assert.Equal(("5h", "7d"), (firstWait.Window, secondWait.Window));
        Assert.Equal(
            [AttemptOrigin.Initial, AttemptOrigin.SendBack, AttemptOrigin.Hint, AttemptOrigin.Reopen, AttemptOrigin.Hint, AttemptOrigin.Handoff],
            history.Attempts.Select(attempt => attempt.Origin));
        Assert.True(run.Clock.GetUtcNow() - started >= TimeSpan.FromHours(3), $"The scenario spanned only {run.Clock.GetUtcNow() - started}.");
    }

    private static async Task RestsAsync(SimulatedRun run, EventWatch<SessionStopped> stopped, JobId job)
    {
        var session = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Sessions[^1].Session;
        await run.DeliveredAsync();
        Assert.True(run.Get<IAgents>().IsOpen(session), $"The session {session.Value} was closed before its prompt cache expired.");

        run.Clock.Advance(PromptCache);

        Assert.Equal(session, (await stopped.UntilAsync(found => found.Session == session)).Session);
        await KeptAsync(run);
    }

    private static async Task SettledAsync(SimulatedRun run, EventWatch<JobProgressed> progress, JobId job, JobStatus expected)
    {
        Assert.Equal(expected, (await progress.UntilAsync(update => update.Job == job && update.Status is JobStatus.AwaitingReview or JobStatus.NeedsHelp or JobStatus.Failed)).Status);
        await KeptAsync(run);
    }

    private static async Task KeptAsync(SimulatedRun run) => Assert.Empty(await run.BrokenInvariantsAsync());
}
