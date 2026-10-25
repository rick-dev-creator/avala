using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Resources.Storage;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Resources.Tests.Storage;

public sealed class SqliteWorktreeHistoryStoreTests
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EventsDaysAndFirstSightingsSurviveReopeningTheDatabaseAsync()
    {
        await using var data = new TemporaryFolder();
        var recorded = new WorktreeEvent(JobId.New(), "/worktrees/one", JobStatus.Approved, WorktreeMoment.Archived, 2_048, Morning) { Lifetime = TimeSpan.FromHours(3) };
        var day = new WorktreeDay(new DateOnly(2026, 10, 9), 4_096, 2, 1, 1, 2_048);

        await using (var store = await OpenAsync(data))
        {
            await store.RecordAsync(recorded, Cancellation);
            await store.SaveDayAsync(day with { Freed = 0 }, Cancellation);
            await store.SaveDayAsync(day, Cancellation);
            Assert.True(await store.SeeAsync("/worktrees/one", Morning, Cancellation));
            Assert.False(await store.SeeAsync("/worktrees/one", Morning.AddHours(1), Cancellation));
        }

        await using var reopened = await OpenAsync(data);
        var report = await reopened.ReadAsync(new DateOnly(2026, 10, 1), Cancellation);

        Assert.Equal([recorded], report.History);
        Assert.Equal([day], report.Days);
        Assert.Equal(Option<DateTimeOffset>.Some(Morning), await reopened.FirstSeenAsync("/worktrees/one", Cancellation));
        Assert.Empty((await reopened.ReadAsync(new DateOnly(2026, 10, 10), Cancellation)).Days);
    }

    private static async Task<SqliteWorktreeHistoryStore> OpenAsync(TemporaryFolder data)
    {
        var store = new SqliteWorktreeHistoryStore(new AvalaPaths(data.Path));
        await store.RunAsync(Cancellation);

        return store;
    }
}
