using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;

namespace Avala.Projects.Tests.Placing;

public sealed class ProjectsEntryTests
{
    [Fact]
    public async Task MovingAJobPlacesItInTheProjectAndSaysSoAsync()
    {
        await using var projected = new Projected();
        await projected.Repositories.RunAsync(Samples.Cancellation);
        await projected.SubmitAsync("/home/rick/code/website");
        var job = await projected.SubmitAsync(Samples.Ledger);
        var website = projected.Created[0];

        var moved = await projected.Entry.MoveAsync(job, website.Project, Samples.Cancellation);

        Assert.True(moved.TryGetValue(out var announced, out _));
        Assert.Equal(new ConversationMoved(job, website.Project), announced);
        Assert.Contains(announced, projected.Bus.Published);
        Assert.Equal(website.Project, projected.Store.Placements[job]);
        Assert.Equal(website, projected.Entry.Current.Of(job, Samples.Ledger));
    }

    [Fact]
    public async Task MovingIsRefusedForAnUnknownProjectAnUnknownJobOrADelegatedChildAsync()
    {
        await using var projected = new Projected();
        await projected.Repositories.RunAsync(Samples.Cancellation);
        var parent = await projected.SubmitAsync(Samples.Ledger);
        var child = await projected.SubmitAsync(Samples.Ledger, parent);
        var ledger = projected.Created[0].Project;

        ProjectRejection?[] refused =
        [
            Refusal(await projected.Entry.MoveAsync(parent, ProjectId.New(), Samples.Cancellation)),
            Refusal(await projected.Entry.MoveAsync(new JobId(Guid.NewGuid()), ledger, Samples.Cancellation)),
            Refusal(await projected.Entry.MoveAsync(child, ledger, Samples.Cancellation)),
        ];

        Assert.Equal([ProjectRejection.UnknownProject, ProjectRejection.UnknownJob, ProjectRejection.FollowsItsParent], refused);
        Assert.Empty(projected.Store.Placements);
        Assert.Empty(projected.Bus.Published.OfType<ConversationMoved>());
    }

    private static ProjectRejection? Refusal(Avala.Sdk.Result<ConversationMoved, ProjectRejection> result) =>
        result.Match(_ => default(ProjectRejection?), rejection => rejection);
}
