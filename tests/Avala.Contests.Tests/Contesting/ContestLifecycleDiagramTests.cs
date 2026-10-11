using Avala.Contests.Contesting;
using Avala.Contests.Contracts;
using Avala.Testing;

namespace Avala.Contests.Tests.Contesting;

public sealed class ContestLifecycleDiagramTests
{
    [Fact]
    public async Task TheDocumentedDiagramMatchesTheLifecycleAsync()
    {
        var diagram = await StateDiagram.CompareAsync(
            "contest-lifecycle.md",
            "Contest lifecycle",
            nameof(ContestLifecycle),
            ContestLifecycle.Create(() => ContestStatus.Running, _ => { }).GetInfo(),
            TestContext.Current.CancellationToken);

        Assert.Equal(diagram.Expected, diagram.Documented);
    }
}
