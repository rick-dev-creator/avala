using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Tests.Coordination;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Jobs.Tests.Catalog;

public sealed class JobCatalogTests
{
    private static CancellationToken Cancellation => JobFlow.Cancellation;

    [Fact]
    public async Task TheCatalogListsEveryJobInSubmissionOrderWithWhatAViewShowsAsync()
    {
        var flow = JobFlow.With();
        var running = await flow.RunningAsync(JobFlow.Request() with { Autonomy = Autonomy.Autonomous, Connection = new ConnectionName("work") });
        var waiting = await flow.SubmittedAsync();

        var listed = await flow.Catalog.ListAsync(Cancellation);

        Assert.Equal(
            [
                new JobSummary(running.Id, "/repos/shop", "Add GitHub login", At(0), JobStatus.Running, new ConnectionName("work"), Autonomy.Autonomous, running.Workspace),
                new JobSummary(waiting.Id, "/repos/shop", "Add GitHub login", At(1), JobStatus.Preparing, Option<ConnectionName>.None, Option<Autonomy>.None, Option<WorkspaceId>.None),
            ],
            listed);
    }

    [Fact]
    public async Task AJobsHistoryListsItsAttemptsAndTheSessionsTheyRanInAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.HeldAsync(HoldReason.SessionLost);
        var first = Outcomes.Present(job.Session);
        var continued = Outcomes.Succeeds(await flow.Jobs.ContinueAsync(job.Id, "Use the staging database", Cancellation));

        var history = Outcomes.Present(await flow.Catalog.HistoryAsync(job.Id, Cancellation));

        Assert.Equal(JobStatus.Running, history.Summary.Status);
        Assert.Equal(
            [
                new AttemptRecord(1, AttemptOrigin.Initial, AttemptOutcome.Interrupted, Option<string>.None, first),
                new AttemptRecord(2, AttemptOrigin.Hint, AttemptOutcome.Running, "Use the staging database", continued.Session),
            ],
            history.Attempts);
        Assert.Equal(
            [(first, "1"), (continued.Session, "2")],
            history.Sessions.Select(session => (session.Session, string.Join(',', session.Attempts))));
    }

    [Fact]
    public async Task AnUnknownJobHasNoHistoryAsync() =>
        Assert.Equal(Option<JobHistory>.None, await JobFlow.With().Catalog.HistoryAsync(JobId.New(), Cancellation));

    private static DateTimeOffset At(int second) => new(2026, 10, 9, 8, 0, second, TimeSpan.Zero);
}
