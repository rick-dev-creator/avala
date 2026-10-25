using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Resources.History;
using Avala.Sdk;

namespace Avala.Resources.Tests.History;

public sealed class WorktreeHistoryTests
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeHistoryStore store = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASampleRecordsTheDaysPeakItsWorktreesAndThoseSeenForTheFirstTimeAsync()
    {
        var history = new WorktreeHistory(store);

        await history.HandleAsync(Sampled(Morning, ("one", 100), ("two", 50)), Cancellation);
        await history.HandleAsync(Sampled(Morning.AddHours(1), ("one", 120)), Cancellation);

        Assert.Equal(new WorktreeDay(Day(Morning), 150, 2, 2, 0, 0), Assert.Single(store.Days.Values));
    }

    [Fact]
    public async Task AFreedWorktreeIsRecordedWithHowLongItLivedAndCountedForItsDayAsync()
    {
        var history = new WorktreeHistory(store);
        var job = JobId.New();
        await history.HandleAsync(Sampled(Morning, ("one", 100)), Cancellation);

        await history.HandleAsync(new WorktreeFreed(new FreedWorktree(job, "one", JobStatus.Approved, WorktreeMoment.Delivered, 100, Morning.AddHours(5))), Cancellation);

        var recorded = Assert.Single(store.Events);
        Assert.Equal((job, Option<WorktreeMoment>.Some(WorktreeMoment.Delivered), 100L, Option<TimeSpan>.Some(TimeSpan.FromHours(5))), (recorded.Job, recorded.Moment, recorded.Bytes, recorded.Lifetime));
        Assert.Equal((1, 100L), (store.Days[Day(Morning)].Freed, store.Days[Day(Morning)].Recovered));
    }

    [Fact]
    public async Task DeletedWorkIsRecordedWithoutAMomentAsync()
    {
        var history = new WorktreeHistory(store);

        await history.HandleAsync(new WorktreeReclaimed(new ReclaimedWorktree(JobId.New(), "one", JobStatus.Discarded, Morning)), Cancellation);

        Assert.Equal((Option<WorktreeMoment>.None, Option<TimeSpan>.None), (Assert.Single(store.Events).Moment, store.Events[0].Lifetime));
    }

    private static ResourcesSampled Sampled(DateTimeOffset at, params (string Path, long Bytes)[] folders) =>
        new(new ResourceSample(at, [], [.. folders.Select(folder => new FolderUsage(folder.Path, folder.Bytes))], 0));

    private static DateOnly Day(DateTimeOffset at) => DateOnly.FromDateTime(at.ToLocalTime().DateTime);

    private sealed class FakeHistoryStore : IWorktreeHistoryStore
    {
        private readonly Dictionary<string, DateTimeOffset> seen = [];

        public List<WorktreeEvent> Events { get; } = [];

        public Dictionary<DateOnly, WorktreeDay> Days { get; } = [];

        public Task RecordAsync(WorktreeEvent recorded, CancellationToken cancellationToken)
        {
            Events.Add(recorded);

            return Task.CompletedTask;
        }

        public Task SaveDayAsync(WorktreeDay day, CancellationToken cancellationToken)
        {
            Days[day.Day] = day;

            return Task.CompletedTask;
        }

        public Task<Option<WorktreeDay>> DayAsync(DateOnly day, CancellationToken cancellationToken) =>
            Task.FromResult(Days.TryGetValue(day, out var found) ? found : Option<WorktreeDay>.None);

        public Task<bool> SeeAsync(string path, DateTimeOffset at, CancellationToken cancellationToken) => Task.FromResult(seen.TryAdd(path, at));

        public Task<Option<DateTimeOffset>> FirstSeenAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult(seen.TryGetValue(path, out var first) ? first : Option<DateTimeOffset>.None);

        public Task<WorktreeReport> ReadAsync(DateOnly from, CancellationToken cancellationToken) =>
            Task.FromResult(new WorktreeReport([.. Days.Values.Where(day => day.Day >= from)], [.. Events]));
    }
}
