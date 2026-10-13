using Avala.Projects.Contracts;
using Avala.Sdk;

namespace Avala.Projects.Tests.Placing;

public sealed class RepositoryProjectsTests
{
    [Fact]
    public async Task ARepositorysFirstJobCreatesItsProjectNamedAfterItsFolderAsync()
    {
        await using var projected = new Projected();
        await projected.Repositories.RunAsync(Samples.Cancellation);

        await projected.SubmitAsync(Samples.Ledger);

        var created = Assert.Single(projected.Created);
        Assert.Equal(("ledger", Option<string>.Some(Samples.Ledger)), (created.Name, created.Repository));
        Assert.Equal([created], projected.Entry.Current.Projects);
    }

    [Fact]
    public async Task AnotherJobOfTheSameRepositoryCreatesNoProjectAsync()
    {
        await using var projected = new Projected();
        await projected.Repositories.RunAsync(Samples.Cancellation);

        await projected.SubmitAsync(Samples.Ledger);
        await projected.SubmitAsync(Samples.Ledger);

        Assert.Single(projected.Created);
    }

    [Fact]
    public async Task ARepositoryWithTheSameFolderNameIsToldApartByItsParentFolderAsync()
    {
        await using var projected = new Projected();
        await projected.Repositories.RunAsync(Samples.Cancellation);

        await projected.SubmitAsync("/home/rick/work/api");
        await projected.SubmitAsync("/home/rick/play/api/");

        Assert.Equal(["api", "api (play)"], projected.Created.Select(project => project.Name));
    }

    [Fact]
    public async Task AJobInAScratchRepositoryCreatesNoProjectAsync()
    {
        await using var projected = new Projected();
        await projected.Repositories.RunAsync(Samples.Cancellation);

        await projected.SubmitAsync(Samples.Scratch);

        Assert.Empty(projected.Created);
    }

    [Fact]
    public async Task StartingCreatesTheProjectsOfEarlierJobsAndRestoresEveryProjectAndPlacementAsync()
    {
        var store = new MemoryStore();
        var website = new ProjectRecord(ProjectId.New(), "Website", Option<string>.None);
        await store.KeepAsync(website, Samples.Cancellation);
        await using var projected = new Projected(store);
        var moved = projected.Catalog.Add(Samples.Ledger);
        projected.Catalog.Add(Samples.Ledger);
        projected.Catalog.Add(Samples.Scratch);
        await store.PlaceAsync(moved, website.Project, Samples.Cancellation);

        await projected.Repositories.RunAsync(Samples.Cancellation);

        var restored = Assert.Single(projected.Bus.Published.OfType<ProjectsRestored>()).Map;
        Assert.Equal(["Website", "ledger"], restored.Projects.Select(project => project.Name));
        Assert.Equal(website, restored.Of(moved, Samples.Ledger));
    }
}
