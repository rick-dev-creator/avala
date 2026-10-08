using Avala.Agents.Domain;
using Avala.Testing;

namespace Avala.Agents.Tests.Domain;

public sealed class TurnLifecycleDiagramTests
{
    [Fact]
    public async Task TheDocumentedDiagramMatchesTheLifecycleAsync()
    {
        var diagram = await StateDiagram.CompareAsync(
            "turn-lifecycle.md",
            "Turn lifecycle",
            nameof(TurnLifecycle),
            TurnLifecycle.Create(() => TurnState.Working, _ => { }).GetInfo(),
            TestContext.Current.CancellationToken);

        Assert.Equal(diagram.Expected, diagram.Documented);
    }
}
