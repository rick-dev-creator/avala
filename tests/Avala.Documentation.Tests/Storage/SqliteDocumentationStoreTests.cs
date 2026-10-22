using Avala.Documentation.Contracts;
using Avala.Documentation.Storage;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Documentation.Tests.Storage;

public sealed class SqliteDocumentationStoreTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobsLatestPlanIsKeptAcrossRestartsBesideTheLastPlanItsVersionsWereWrittenFromAsync()
    {
        await using var data = new TemporaryFolder();
        var paths = new AvalaPaths(data.Path);
        var job = new JobId(Guid.CreateVersion7());

        await using (var store = new SqliteDocumentationStore(paths))
        {
            await store.ChooseAsync(job, FeatureChoice.Of(new Feature("refund-webhooks", "Refund webhooks")), Cancellation);
            await store.WrittenAsync(job, new FeatureVersion(1, VersionKind.Proposed, "docs/features/refund-webhooks/v1-proposed.md", DateTimeOffset.UnixEpoch), "Add a Webhooks container", Cancellation);
            await store.PlannedAsync(job, ["Add a Webhooks container", "Keep idempotency keys in the ledger\nonce per event"], Cancellation);
        }

        await using var restarted = new SqliteDocumentationStore(paths);

        var kept = Assert.Single(await restarted.ReadAsync(Cancellation));
        Assert.Equal("Add a Webhooks container", kept.LastPlan);
        Assert.Equal(["Add a Webhooks container", "Keep idempotency keys in the ledger\nonce per event"], kept.Plan);
    }

    [Fact]
    public async Task AVersionThatWasNotWrittenIsKeptAcrossRestartsWithWhyAndTheHoldOfItsWriterAsync()
    {
        await using var data = new TemporaryFolder();
        var paths = new AvalaPaths(data.Path);
        var job = new JobId(Guid.CreateVersion7());
        UnwrittenVersion[] missed =
        [
            new(VersionKind.Proposed, VersionFailure.WriterStopped, DateTimeOffset.UnixEpoch, 1) { Hold = HoldReason.LimitNearlyReached },
            new(VersionKind.AsBuilt, VersionFailure.NoDraft, DateTimeOffset.UnixEpoch.AddMinutes(1), 2),
        ];

        await using (var store = new SqliteDocumentationStore(paths))
        {
            await store.ChooseAsync(job, FeatureChoice.Of(new Feature("refund-webhooks", "Refund webhooks")), Cancellation);
            await store.UnwrittenAsync(job, missed[0], Option<string>.None, Cancellation);
            await store.UnwrittenAsync(job, missed[1], Option<string>.None, Cancellation);
        }

        await using var restarted = new SqliteDocumentationStore(paths);

        Assert.Equal(missed, Assert.Single(await restarted.ReadAsync(Cancellation)).Unwritten);
    }
}
