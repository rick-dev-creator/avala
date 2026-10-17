using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Timeline;

public sealed class DocsLinesTests
{
    [Fact]
    public void AJobsVersionsAndChangeRequestsAreAddedOnceAndChangedInPlace()
    {
        var job = JobId.New();
        var documented = new JobDocumentation(job, new Feature("refunds", "Refund webhooks"))
        {
            Versions = [new FeatureVersion(1, VersionKind.Proposed, "docs/features/refunds/v1-proposed.md", DateTimeOffset.UnixEpoch)],
            Requests = [new ChangeRequest(1, RequestKind.Steer, "Once only", DateTimeOffset.UnixEpoch, 1)],
        };

        var first = Transcript.Empty.WithDocs(documented);
        var again = first.WithDocs(documented);
        var linked = again.WithDocs(documented with { Requests = [documented.Requests[0] with { Version = 2 }] });

        Assert.Equal(["docs:1", "change:1"], first.Entries.Select(entry => entry.Key));
        Assert.Same(first, again);
        Assert.Equal(2, linked.Entries.Count);
        Assert.Equal(2, ((ChangeEntry)linked.Entries[1]).Request.Version.Match(version => version, () => 0));
    }

    [Fact]
    public void EachLineSitsAtTheEndOfTheAttemptItBelongsToInTheOrderItHappened()
    {
        var at = DateTimeOffset.UnixEpoch;
        var shown = Transcript.Empty
            .Kept(new PromptEntry(EntryKeys.Attempt(1), 1, AttemptOrigin.Initial, "Handle refunds", Sdk.Option<AttemptOutcome>.None))
            .Kept(new PromptEntry(EntryKeys.Attempt(2), 2, AttemptOrigin.SendBack, "Apply it once", Sdk.Option<AttemptOutcome>.None));
        var documented = new JobDocumentation(JobId.New(), new Feature("refunds", "Refund webhooks"))
        {
            Versions =
            [
                new FeatureVersion(1, VersionKind.Proposed, "v1-proposed.md", at.AddMinutes(1)),
                new FeatureVersion(2, VersionKind.AsBuilt, "v2-as-built.md", at.AddMinutes(2)),
                new FeatureVersion(3, VersionKind.Deviation, "v3-deviation.md", at.AddMinutes(4)) { Attempt = 2 },
            ],
            Requests = [new ChangeRequest(1, RequestKind.SendBack, "Apply it once", at.AddMinutes(3), 2)],
        };

        var placed = shown.WithDocs(documented);

        Assert.Equal(["attempt:1", "docs:1", "docs:2", "attempt:2", "change:1", "docs:3"], placed.Entries.Select(entry => entry.Key));
        Assert.Same(placed, placed.WithDocs(documented));
    }
}
