using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Supervision.Contracts;
using Avala.Supervision.Storage;
using Avala.Supervision.Tests.Supervising;
using Avala.Testing;
using static Avala.Supervision.Tests.Supervising.Supervised;

namespace Avala.Supervision.Tests.Storage;

public sealed class InterventionHistoryTests
{
    private static readonly DateTimeOffset Nine = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnInterventionIsStoredAndTheInterventionsOfEarlierRunsCountForTheirJobAsync()
    {
        await using var supervised = new Supervised();
        var earlier = Stalled(supervised.Job, Nine.AddDays(-1));
        supervised.Store.Earlier = [earlier, Stalled(JobId.New(), Nine)];
        await supervised.Book.RunAsync(Cancellation);
        await supervised.RunningAsync();

        await supervised.AdvanceAsync(Window);

        var stalled = Assert.Single(supervised.Store.Recorded);
        Assert.Equal([earlier, stalled], supervised.Interventions);
    }

    [Fact]
    public async Task InterventionsSurviveAReopeningAndEarlierRunsLeaveOutWhatThisRunStoredAsync()
    {
        using var folder = new TemporaryFolder();
        var earlier = Stalled(JobId.New(), Nine);
        await using (var first = new SqliteInterventionStore(new AvalaPaths(folder.Path)))
        {
            await first.RecordAsync(earlier, Cancellation);
        }

        await using var second = new SqliteInterventionStore(new AvalaPaths(folder.Path));
        await second.RecordAsync(Stalled(JobId.New(), Nine.AddHours(1)), Cancellation);

        Assert.Equal([earlier], await second.EarlierRunsAsync(Cancellation));
    }

    private static SupervisionIntervention Stalled(JobId job, DateTimeOffset at) =>
        new(new JobHold(job, SessionId.New(), HoldReason.Stalled, SessionHalt.Interrupted), new SilenceMeasure(TimeSpan.FromMinutes(11), Window), at);
}
