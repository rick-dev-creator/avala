using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;

namespace Avala.Workbench.Tests.Board;

public sealed class DocsKeeperTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobsDocumentationIsKeptOnTheBoardWhenItChangesAndAtStartupAsync()
    {
        var catalog = new FakeCatalog();
        var job = catalog.Add("Handle refunds", JobStatus.Running).Summary;
        var documented = new JobDocumentation(job.Job, new Feature("refunds", "Refund webhooks"));
        var board = new JobBoard();
        var documentation = new FakeDocumentation();
        var keeper = new DocsKeeper(documentation, catalog, board);

        await keeper.HandleAsync(new FeatureChosen(job.Job, FeatureChoice.Of(documented.Feature)), Cancellation);
        var before = board.Docs.ContainsKey(job.Job);
        documentation.Documented = documented;
        await keeper.HandleAsync(new StartupCompleted(), Cancellation);

        Assert.False(before);
        Assert.Equal(documented, board.Docs[job.Job]);
    }
}
