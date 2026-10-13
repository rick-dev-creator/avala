using Avala.Jobs.Contracts;
using Avala.Projects.Catalog;
using Avala.Projects.Contracts;

namespace Avala.Projects.Tests.Catalog;

public sealed class ProjectTests
{
    [Theory]
    [InlineData(new string[0], "api")]
    [InlineData(new[] { "api" }, "api (rick)")]
    [InlineData(new[] { "API", "api (rick)" }, "api (rick) 2")]
    public void ARepositorysProjectIsNamedAfterItsFolderAndThenItsParentWhenTheNameIsTaken(string[] taken, string expected)
    {
        Assert.True(Project.ForRepository(ProjectId.New(), "/home/rick/api", new RepositoryFolder("api", "rick"), taken).TryGetValue(out var project, out _));

        Assert.Equal(expected, project.Name);
    }

    [Fact]
    public void ADelegatedChildFollowsItsParentInsteadOfBeingPlaced()
    {
        Assert.True(Project.ForRepository(ProjectId.New(), "/home/rick/api", new RepositoryFolder("api", "rick"), []).TryGetValue(out var project, out _));

        Assert.Equal(ProjectError.FollowsItsParent, project.Place(new JobId(Guid.NewGuid()), new JobId(Guid.NewGuid())).Match(_ => default(ProjectError?), error => error));
    }
}
