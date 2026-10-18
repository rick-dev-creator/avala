using Avala.Jobs.Contracts;
using Avala.Projects.Catalog;
using Avala.Sdk;

namespace Avala.Projects.Tests.Catalog;

public sealed class ArchiveTests
{
    private static readonly JobId Job = new(Guid.NewGuid());

    [Theory]
    [InlineData(JobStatus.NeedsHelp)]
    [InlineData(JobStatus.AwaitingReview)]
    [InlineData(JobStatus.Approved)]
    [InlineData(JobStatus.Discarded)]
    [InlineData(JobStatus.Failed)]
    public void AConversationThatDoesNotWorkIsArchived(JobStatus status) =>
        Assert.Equal([Job], Archive.Of([new Shelved(Job, status, Option<JobId>.None)]).Match(archived => archived.Jobs.ToArray(), _ => []));

    [Theory]
    [InlineData(JobStatus.Draft)]
    [InlineData(JobStatus.Preparing)]
    [InlineData(JobStatus.Running)]
    [InlineData(JobStatus.Checking)]
    public void AConversationThatWorksIsRefusedSinceItIsStillWorking(JobStatus status) =>
        Assert.Equal(ProjectError.StillWorking, Archive.Of([new Shelved(new JobId(Guid.NewGuid()), JobStatus.Approved, Option<JobId>.None), new Shelved(Job, status, Option<JobId>.None)]).Match(_ => default(ProjectError?), error => error));

    [Fact]
    public void ADelegatedChildIsRefusedSinceItFollowsItsParent() =>
        Assert.Equal(ProjectError.FollowsItsParent, Archive.Of([new Shelved(Job, JobStatus.Approved, new JobId(Guid.NewGuid()))]).Match(_ => default(ProjectError?), error => error));

    [Fact]
    public void OnlyAnArchivedConversationIsRestored()
    {
        Assert.True(Archive.Restore(Job, new HashSet<JobId> { Job }).IsSuccess);
        Assert.Equal(ProjectError.NotArchived, Archive.Restore(Job, new HashSet<JobId>()).Match(_ => default(ProjectError?), error => error));
    }

    [Theory]
    [InlineData(JobStatus.Preparing, true)]
    [InlineData(JobStatus.Running, true)]
    [InlineData(JobStatus.Checking, true)]
    [InlineData(JobStatus.NeedsHelp, false)]
    [InlineData(JobStatus.AwaitingReview, false)]
    [InlineData(JobStatus.Approved, false)]
    [InlineData(JobStatus.Discarded, false)]
    [InlineData(JobStatus.Failed, false)]
    public void AConversationReturnsFromTheArchiveOnceItWorks(JobStatus status, bool returns) =>
        Assert.Equal(returns, Archive.Returns(status));
}
