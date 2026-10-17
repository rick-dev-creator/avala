using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;

namespace Avala.Projects.Tests.Placing;

public sealed class ArchivingTests
{
    [Fact]
    public async Task ArchivingConversationsKeepsThemArchivedAndSaysSoAsync()
    {
        await using var projected = new Projected();
        await projected.Repositories.RunAsync(Samples.Cancellation);
        var reviewed = await projected.SubmitAsync(Samples.Ledger, status: JobStatus.AwaitingReview);
        var approved = await projected.SubmitAsync(Samples.Ledger, status: JobStatus.Approved);

        var archived = await projected.Entry.ArchiveAsync([reviewed, approved], Samples.Cancellation);

        Assert.True(archived.TryGetValue(out var announced, out _));
        Assert.Equal([reviewed, approved], announced.Jobs);
        Assert.Contains(announced, projected.Bus.Published);
        Assert.True(projected.Store.Archived.SetEquals([reviewed, approved]));
        Assert.True(projected.Entry.Current.Archived.SetEquals([reviewed, approved]));
    }

    [Fact]
    public async Task ARefusedArchiveArchivesNoneOfItsConversationsAsync()
    {
        await using var projected = new Projected();
        await projected.Repositories.RunAsync(Samples.Cancellation);
        var approved = await projected.SubmitAsync(Samples.Ledger, status: JobStatus.Approved);
        var running = await projected.SubmitAsync(Samples.Ledger);

        ProjectRejection?[] refused =
        [
            (await projected.Entry.ArchiveAsync([approved, running], Samples.Cancellation)).Match(_ => default(ProjectRejection?), rejection => rejection),
            (await projected.Entry.ArchiveAsync([new JobId(Guid.NewGuid())], Samples.Cancellation)).Match(_ => default(ProjectRejection?), rejection => rejection),
        ];

        Assert.Equal([ProjectRejection.StillWorking, ProjectRejection.UnknownJob], refused);
        Assert.Empty(projected.Store.Archived);
        Assert.DoesNotContain(projected.Bus.Published, published => published is ConversationsArchived);
    }

    [Fact]
    public async Task RestoringAConversationTakesItOutOfTheArchiveAndSaysSoAsync()
    {
        await using var projected = new Projected();
        await projected.Repositories.RunAsync(Samples.Cancellation);
        var job = await projected.SubmitAsync(Samples.Ledger, status: JobStatus.Approved);
        await projected.Entry.ArchiveAsync([job], Samples.Cancellation);

        var restored = await projected.Entry.RestoreAsync(job, Samples.Cancellation);
        var again = await projected.Entry.RestoreAsync(job, Samples.Cancellation);

        Assert.True(restored.TryGetValue(out var announced, out _));
        Assert.Contains(announced, projected.Bus.Published);
        Assert.Empty(projected.Store.Archived);
        Assert.Empty(projected.Entry.Current.Archived);
        Assert.Equal((ProjectRejection?)ProjectRejection.NotArchived, again.Match(_ => default(ProjectRejection?), rejection => rejection));
    }

    [Fact]
    public async Task AnArchivedConversationThatWorksAgainReturnsByItselfAsync()
    {
        await using var projected = new Projected();
        await projected.Repositories.RunAsync(Samples.Cancellation);
        var job = await projected.SubmitAsync(Samples.Ledger, status: JobStatus.AwaitingReview);
        await projected.Entry.ArchiveAsync([job], Samples.Cancellation);

        await projected.Returns.HandleAsync(new JobProgressed(job, JobStatus.AwaitingReview), Samples.Cancellation);
        var stayed = projected.Entry.Current.Archived.Contains(job);
        await projected.Returns.HandleAsync(new JobProgressed(job, JobStatus.Running), Samples.Cancellation);

        Assert.True(stayed);
        Assert.Empty(projected.Store.Archived);
        Assert.Contains(new ConversationRestored(job), projected.Bus.Published);
    }

    [Fact]
    public async Task AHeldConversationIsArchivedAndStaysArchivedWhenHeldAgainAndAfterARestartAsync()
    {
        var store = new MemoryStore();
        var catalog = new FakeCatalog();
        JobId held;

        await using (var projected = new Projected(store, catalog))
        {
            await projected.Repositories.RunAsync(Samples.Cancellation);
            held = await projected.SubmitAsync(Samples.Ledger, status: JobStatus.NeedsHelp);
            Assert.True((await projected.Entry.ArchiveAsync([held], Samples.Cancellation)).IsSuccess);
            await projected.Returns.HandleAsync(new JobProgressed(held, JobStatus.NeedsHelp), Samples.Cancellation);
        }

        await using var restarted = new Projected(store, catalog);
        await restarted.Repositories.RunAsync(Samples.Cancellation);

        Assert.Equal([held], restarted.Entry.Current.Archived);
        Assert.Equal([held], store.Archived);
    }

    [Fact]
    public async Task ArchivedConversationsStayArchivedAfterARestartUnlessTheyWorkedMeanwhileAsync()
    {
        var store = new MemoryStore();
        var catalog = new FakeCatalog();
        JobId kept;
        JobId resumed;

        await using (var projected = new Projected(store, catalog))
        {
            await projected.Repositories.RunAsync(Samples.Cancellation);
            kept = await projected.SubmitAsync(Samples.Ledger, status: JobStatus.Approved);
            resumed = await projected.SubmitAsync(Samples.Ledger, status: JobStatus.AwaitingReview);
            await projected.Entry.ArchiveAsync([kept, resumed], Samples.Cancellation);
        }

        catalog.Set(resumed, JobStatus.Running);
        await using var restarted = new Projected(store, catalog);
        await restarted.Repositories.RunAsync(Samples.Cancellation);

        Assert.Equal([kept], restarted.Entry.Current.Archived);
        Assert.Equal([kept], store.Archived);
    }
}
