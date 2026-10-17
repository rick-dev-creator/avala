using Avala.Documentation.Contracts;
using Avala.Documentation.Features;
using Avala.Jobs.Contracts;

namespace Avala.Documentation.Tests.Features;

public sealed class ChangeLogTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 17, 10, 31, 0, TimeSpan.Zero);

    private static readonly VersionFile[] Versions = [new(1, VersionKind.Proposed, "v1-proposed.md"), new(2, VersionKind.Deviation, "v2-deviation.md")];

    [Fact]
    public void AJobsRequestsAreListedUnderItsInstructionWithTheVersionEachProduced()
    {
        var job = JobId.New();
        ChangeRequest[] requests =
        [
            new(1, RequestKind.Steer, "Make sure a refund never applies twice", At, 1) { Version = 2 },
            new(2, RequestKind.SendBack, "No new service:\nkeep the keys in the ledger", At.AddMinutes(40), 1),
        ];

        var log = ChangeLog.Merge(string.Empty, job, "Handle Stripe refund webhooks", requests, Versions);

        Assert.Equal(
            $"# Change requests\n\nWhat people asked while the feature was built, and the version each request produced.\n\n<!-- avala:job {job.Value} -->\n## Handle Stripe refund webhooks\n\n### Change request 1 · steer · attempt 1\n\n> Make sure a refund never applies twice\n\nAsked 2026-10-17 10:31 UTC. Produced [v2](v2-deviation.md).\n\n### Change request 2 · sent back in review · attempt 1\n\n> No new service:\n> keep the keys in the ledger\n\nAsked 2026-10-17 11:11 UTC. No version yet.\n\n<!-- /avala:job {job.Value} -->\n",
            log);
    }

    [Fact]
    public void MergingAgainReplacesOnlyThatJobsSectionAndKeepsTheOthers()
    {
        var earlier = JobId.New();
        var job = JobId.New();
        var first = ChangeLog.Merge(string.Empty, earlier, "Migrate payments", [new(1, RequestKind.Message, "Keep the tests green", At, 2)], Versions);
        var second = ChangeLog.Merge(first, job, "Handle refunds", [new(1, RequestKind.Steer, "Once only", At, 1)], Versions);

        var updated = ChangeLog.Merge(second, job, "Handle refunds", [new(1, RequestKind.Steer, "Once only", At, 1) { Version = 1 }], Versions);

        var section = updated[updated.IndexOf($"<!-- avala:job {job.Value} -->", StringComparison.Ordinal)..];
        Assert.Contains("## Migrate payments\n\n### Change request 1 · message · attempt 2", updated);
        Assert.Contains("Produced [v1](v1-proposed.md).", section);
        Assert.DoesNotContain("No version yet.", section);
        Assert.Single(updated.Split($"<!-- avala:job {job.Value} -->"), part => part.Contains("Once only", StringComparison.Ordinal));
    }
}
