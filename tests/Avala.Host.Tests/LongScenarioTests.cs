using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Delegation.Contracts;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

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

    private static readonly (string File, string Content)[] TwoLogins =
    [
        ("connections.json", Connections),
        ("connections/work/.login", string.Empty),
        ("connections/personal/.login", string.Empty),
    ];

    private static readonly (string Path, string Content)[] Rules =
    [
        (".avala/checks.json", """{ "checks": [ { "name": "git", "command": "git", "arguments": ["--version"], "timeoutSeconds": 60 } ] }"""),
        (".avala/permissions.json", """{ "autonomy": "autonomous" }"""),
        (".avala/jobs.json", """{ "delegation": {}, "limits": { "resumeAtReset": "auto" } }"""),
    ];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobThatDelegatesRestsRunsOutOfTwoWindowsIsReopenedByItsChecksAndHandedOffKeepsEveryInvariantAtEveryStepAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, TwoLogins, Rules);
        run.Cues.Cue("This job was handed off", "reply");
        var progress = run.Watch<JobProgressed>();
        var reported = run.Watch<ChildReported>();
        var stopped = run.Watch<SessionStopped>();
        var waits = run.Watch<JobWaitsForReset>();
        var resumed = run.Watch<JobResumedAtReset>();
        var started = run.Clock.GetUtcNow();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("long-haul")) { Connection = new ConnectionName("work") }));

        var report = Outcomes.Present((await reported.UntilAsync(_ => true)).Delegation.Report);
        await ReviewedAsync(run, progress, job);
        Assert.Equal(ChildOutcome.Integrated, report.Outcome);

        var rested = SessionOf(await HistoryAsync(run, job));
        run.Clock.Advance(PromptCache);
        _ = await stopped.UntilAsync(found => found.Session == rested);
        await KeptAsync(run);

        Assert.Equal(ContinuedIn.ResumedConversation, Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, "Write the to-do list too.", Cancellation)).Conversation);
        await HeldAsync(progress, job);
        var firstWait = (await waits.UntilAsync(found => found.Wait.Job == job)).Wait;
        await KeptAsync(run);
        run.AdvanceTo(Outcomes.Present(firstWait.ResumesAt));
        _ = await resumed.UntilAsync(found => found.Job == job);
        await ReviewedAsync(run, progress, job);

        _ = Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(job, Cancellation));
        await KeptAsync(run);
        _ = Outcomes.Succeeds(await run.Get<IOpenDeliveries>().ReopenAsync(job, "The checks failed on main after the merge: TODO.md breaks the build.", Cancellation));
        await ReviewedAsync(run, progress, job);

        run.Clock.Advance(PromptCache);
        await KeptAsync(run);
        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, "Start the changelog.", Cancellation));
        await HeldAsync(progress, job);
        var secondWait = (await waits.UntilAsync(found => found.Wait.Job == job)).Wait;
        await KeptAsync(run);
        _ = Outcomes.Succeeds(await run.Get<IHandoffRequests>().HandOffAsync(job, new ConnectionName("personal"), Cancellation));
        await ReviewedAsync(run, progress, job);

        _ = Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(job, Cancellation));
        await KeptAsync(run);
        var history = await HistoryAsync(run, job);
        Assert.Equal((JobStatus.Approved, Option<ConnectionName>.Some(new ConnectionName("personal"))), (history.Summary.Status, history.Summary.Connection));
        Assert.Equal(("5h", "7d"), (firstWait.Window, secondWait.Window));
        Assert.Equal(
            [AttemptOrigin.Initial, AttemptOrigin.SendBack, AttemptOrigin.Hint, AttemptOrigin.Reopen, AttemptOrigin.SendBack, AttemptOrigin.Handoff],
            history.Attempts.Select(attempt => attempt.Origin));
        Assert.True(run.Clock.GetUtcNow() - started >= TimeSpan.FromHours(3), $"The scenario spanned only {run.Clock.GetUtcNow() - started}.");
    }

    private static async Task ReviewedAsync(SimulatedRun run, EventWatch<JobProgressed> progress, JobId job)
    {
        Assert.Equal(JobStatus.AwaitingReview, (await progress.UntilAsync(update => update.Job == job && update.Status is JobStatus.AwaitingReview or JobStatus.NeedsHelp or JobStatus.Failed)).Status);
        await KeptAsync(run);
    }

    private static async Task HeldAsync(EventWatch<JobProgressed> progress, JobId job) =>
        Assert.Equal(JobStatus.NeedsHelp, (await progress.UntilAsync(update => update.Job == job && update.Status is JobStatus.AwaitingReview or JobStatus.NeedsHelp or JobStatus.Failed)).Status);

    private static async Task KeptAsync(SimulatedRun run) => Assert.Empty(await run.BrokenInvariantsAsync());

    private static async Task<JobHistory> HistoryAsync(SimulatedRun run, JobId job) =>
        Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation));

    private static Agents.Contracts.Sessions.SessionId SessionOf(JobHistory history) => history.Sessions[^1].Session;
}
