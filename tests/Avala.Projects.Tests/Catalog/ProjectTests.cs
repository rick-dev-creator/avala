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

    [Theory]
    [InlineData("  Website  ", "Website")]
    [InlineData("   ", "NameEmpty")]
    [InlineData("A name far longer than the sixty characters a project name may have", "NameTooLong")]
    [InlineData("LEDGER", "NameTaken")]
    public void AProjectsNameIsTrimmedAndMustBeShortAndFree(string name, string expected)
    {
        var created = Project.Create(ProjectId.New(), name, ["ledger"]);

        Assert.Equal(expected, created.Match(project => project.Name, error => error.ToString()));
    }

    [Fact]
    public void RenamingKeepsOnlyOtherProjectsNamesTaken()
    {
        Assert.True(Project.Create(ProjectId.New(), "website", []).TryGetValue(out var project, out _));

        var renamed = project.Rename("Website", ["website", "ledger"]).Match(named => named.Project.Name, error => error.ToString());
        var refused = project.Rename("Ledger", ["Website", "ledger"]).Match(named => named.Project.Name, error => error.ToString());

        Assert.Equal(("Website", "NameTaken", "Website"), (renamed, refused, project.Name));
    }

    [Theory]
    [InlineData(true, 0, true)]
    [InlineData(true, 2, false)]
    [InlineData(false, 2, true)]
    public void ARepositorysProjectIsDeletedOnlyWhenEmptyAndOneAPersonMadeAlways(bool repository, int conversations, bool deleted)
    {
        var project = (repository
            ? Project.ForRepository(ProjectId.New(), "/home/rick/api", new RepositoryFolder("api", "rick"), [])
            : Project.Create(ProjectId.New(), "Website", [])).Match(found => found, _ => null!);

        Assert.Equal(deleted ? "Deleted" : "NotEmpty", project.Delete(conversations).Match(_ => "Deleted", error => error.ToString()));
    }

    [Fact]
    public void ADelegatedChildFollowsItsParentInsteadOfBeingPlaced()
    {
        Assert.True(Project.ForRepository(ProjectId.New(), "/home/rick/api", new RepositoryFolder("api", "rick"), []).TryGetValue(out var project, out _));

        Assert.Equal(ProjectError.FollowsItsParent, project.Place(new JobId(Guid.NewGuid()), new JobId(Guid.NewGuid())).Match(_ => default(ProjectError?), error => error));
    }
}
