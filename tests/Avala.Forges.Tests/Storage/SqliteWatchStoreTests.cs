using Avala.Forges.Contracts;
using Avala.Forges.Polling;
using Avala.Forges.Storage;
using Avala.Forges.Tests.Watching;
using Avala.Forges.Watching;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Triggers.Contracts;

namespace Avala.Forges.Tests.Storage;

public sealed class SqliteWatchStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WatchesWakeUpsEventCursorsAndEventEntriesAreKeptAcrossRestartsWithTheLatestWatchAndCursorAsync()
    {
        await using var data = new TemporaryFolder();
        var paths = new AvalaPaths(data.Path);
        var job = new JobId(Guid.CreateVersion7());
        var watch = new KeptWatch(WatcherWorld.Watch(job), "origin", "https://forge.example/octo/shop.git", []);
        var woken = new WakeUpRecord(job, 7, WakeReason.ChecksFailed, "build", "1 test failed", Now, WakeOutcome.Woken);
        var refused = woken with { Reason = WakeReason.Conflict, Key = "conflict", At = Now.AddMinutes(5), Outcome = WakeOutcome.Refused };
        var cursor = new EventCursor("fix-labeled", "label", "100", Now);
        var trigger = new TriggerId(TriggerId.Machine, "fix-labeled");
        var fired = new ForgeEventEntry(trigger, Now, ForgeEventKind.IssueLabeled, "7002", "alice", ForgeEventVerdict.Fired) { Number = 42, Job = job };
        var refusedEvent = new ForgeEventEntry(trigger, Now.AddMinutes(1), ForgeEventKind.IssueLabeled, "7003", "mallory", ForgeEventVerdict.NotAllowed) { Number = 43 };

        await using (var store = new SqliteWatchStore(paths))
        {
            await store.KeepAsync(watch, Cancellation);
            await store.KeepAsync(watch with { Handled = ["build"] }, Cancellation);
            await store.AddAsync(woken, Cancellation);
            await store.AddAsync(refused, Cancellation);
            await store.KeepCursorAsync(cursor, Cancellation);
            await store.KeepCursorAsync(cursor with { Position = "101" }, Cancellation);
            await store.AddEntryAsync(fired, Cancellation);
            await store.AddEntryAsync(refusedEvent, Cancellation);
        }

        await using var restarted = new SqliteWatchStore(paths);

        var kept = Assert.Single(await restarted.WatchesAsync(Cancellation));
        Assert.Equal((watch.State, watch.Remote, watch.RemoteUrl, "build"), (kept.State, kept.Remote, kept.RemoteUrl, string.Join(',', kept.Handled)));
        Assert.Equal([woken, refused], await restarted.WakeUpsAsync(Cancellation));
        Assert.Equal([cursor with { Position = "101" }], await restarted.CursorsAsync(Cancellation));
        Assert.Equal([fired, refusedEvent], await restarted.EntriesAsync(Cancellation));
    }
}
