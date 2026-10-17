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
}
