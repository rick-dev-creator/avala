using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;
using Avala.Sdk;

namespace Avala.Projects.Tests.Contracts;

public sealed class ProjectMapTests
{
    [Fact]
    public void AJobIsInTheProjectItWasMovedToAndOtherwiseInItsRepositorysProject()
    {
        var ledger = new ProjectRecord(ProjectId.New(), "ledger", Samples.Ledger);
        var website = new ProjectRecord(ProjectId.New(), "Website", Option<string>.None);
        var stayed = new JobId(Guid.NewGuid());
        var moved = new JobId(Guid.NewGuid());
        var map = new ProjectMap([ledger, website], ImmutableDictionary<JobId, ProjectId>.Empty.Add(moved, website.Project));

        Assert.Equal(
            [ledger, website, default],
            [map.Of(stayed, Samples.Ledger), map.Of(moved, Samples.Ledger), map.Of(stayed, Samples.Scratch)]);
    }
}
