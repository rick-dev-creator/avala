using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;
using Avala.Sdk;

namespace Avala.Projects.Tests.Placing;

public sealed class ProjectEditingTests
{
    [Fact]
    public async Task CreatingAndRenamingAProjectSaysSoAndKeepsItAsync()
    {
        await using var projected = new Projected();
        await projected.Repositories.RunAsync(Samples.Cancellation);

        var created = await projected.Entry.CreateAsync("Side projects", Samples.Cancellation);
        Assert.True(created.TryGetValue(out var made, out _));
        var renamed = await projected.Entry.RenameAsync(made.Project.Project, "Experiments", Samples.Cancellation);

        Assert.True(renamed.TryGetValue(out var named, out _));
        Assert.Equal(new ProjectRecord(made.Project.Project, "Experiments", Option<string>.None), named.Project);
        Assert.Contains(named, projected.Bus.Published);
        Assert.Equal([named.Project], projected.Store.Projects);
        Assert.Equal([named.Project], projected.Entry.Current.Projects);
    }

    [Fact]
    public async Task CreatingOrRenamingIsRefusedForAnEmptyOrTakenNameOrAnUnknownProjectAsync()
    {
        await using var projected = new Projected();
        await projected.Repositories.RunAsync(Samples.Cancellation);
        await projected.SubmitAsync(Samples.Ledger);

        ProjectRejection?[] refused =
        [
            (await projected.Entry.CreateAsync(" ", Samples.Cancellation)).Match(_ => default(ProjectRejection?), rejection => rejection),
            (await projected.Entry.CreateAsync("Ledger", Samples.Cancellation)).Match(_ => default(ProjectRejection?), rejection => rejection),
            (await projected.Entry.RenameAsync(ProjectId.New(), "Website", Samples.Cancellation)).Match(_ => default(ProjectRejection?), rejection => rejection),
        ];

        Assert.Equal([ProjectRejection.NameEmpty, ProjectRejection.NameTaken, ProjectRejection.UnknownProject], refused);
        Assert.Single(projected.Store.Projects);
    }

    [Fact]
    public async Task DeletingAProjectAPersonMadeReturnsItsConversationsToTheirRepositorysProjectAsync()
    {
        await using var projected = new Projected();
        await projected.Repositories.RunAsync(Samples.Cancellation);
        var job = await projected.SubmitAsync(Samples.Ledger);
        var ledger = projected.Created[0];
        Assert.True((await projected.Entry.CreateAsync("Website", Samples.Cancellation)).TryGetValue(out var website, out _));
        await projected.Entry.MoveAsync(job, website.Project.Project, Samples.Cancellation);

        var deleted = await projected.Entry.DeleteAsync(website.Project.Project, Samples.Cancellation);

        Assert.True(deleted.TryGetValue(out var announced, out _));
        Assert.Contains(announced, projected.Bus.Published);
        Assert.Equal([ledger], projected.Store.Projects);
        Assert.Empty(projected.Store.Placements);
        Assert.Equal(ledger, projected.Entry.Current.Of(job, Samples.Ledger));
    }

    [Fact]
    public async Task ARepositorysProjectHoldingOnlyArchivedConversationsIsDeletedStaysDeletedAfterARestartAndReturnsWhenOneIsRestoredAsync()
    {
        var store = new MemoryStore();
        var catalog = new FakeCatalog();
        JobId job;

        await using (var projected = new Projected(store, catalog))
        {
            await projected.Repositories.RunAsync(Samples.Cancellation);
            job = await projected.SubmitAsync(Samples.Ledger, status: JobStatus.AwaitingReview);
            await projected.Entry.ArchiveAsync([job], Samples.Cancellation);

            Assert.True((await projected.Entry.DeleteAsync(projected.Created[0].Project, Samples.Cancellation)).IsSuccess);
        }

        await using var restarted = new Projected(store, catalog);
        await restarted.Repositories.RunAsync(Samples.Cancellation);
        Assert.Empty(restarted.Entry.Current.Projects);

        Assert.True((await restarted.Entry.RestoreAsync(job, Samples.Cancellation)).TryGetValue(out var restored, out _));
        await restarted.Repositories.HandleAsync(restored, Samples.Cancellation);

        Assert.Equal(["ledger"], restarted.Entry.Current.Projects.Select(project => project.Name));
        Assert.Equal(restarted.Entry.Current.Projects[0], restarted.Entry.Current.Of(job, Samples.Ledger));
    }

    [Fact]
    public async Task ARepositorysProjectIsDeletedOnlyOnceEmptyAndStaysDeletedAfterARestartAsync()
    {
        var store = new MemoryStore();
        var catalog = new FakeCatalog();

        await using (var projected = new Projected(store, catalog))
        {
            await projected.Repositories.RunAsync(Samples.Cancellation);
            var job = await projected.SubmitAsync(Samples.Ledger);
            var ledger = projected.Created[0];
            Assert.True((await projected.Entry.CreateAsync("Website", Samples.Cancellation)).TryGetValue(out var website, out _));

            var refused = await projected.Entry.DeleteAsync(ledger.Project, Samples.Cancellation);
            await projected.Entry.MoveAsync(job, website.Project.Project, Samples.Cancellation);
            var deleted = await projected.Entry.DeleteAsync(ledger.Project, Samples.Cancellation);

            Assert.Equal((ProjectRejection?)ProjectRejection.NotEmpty, refused.Match(_ => default(ProjectRejection?), rejection => rejection));
            Assert.True(deleted.IsSuccess);
        }

        await using var restarted = new Projected(store, catalog);
        await restarted.Repositories.RunAsync(Samples.Cancellation);

        Assert.Equal(["Website"], restarted.Entry.Current.Projects.Select(project => project.Name));
    }
}
