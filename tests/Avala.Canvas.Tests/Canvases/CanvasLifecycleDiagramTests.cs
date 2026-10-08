using Avala.Canvas.Canvases;
using Avala.Testing;

namespace Avala.Canvas.Tests.Canvases;

public sealed class CanvasLifecycleDiagramTests
{
    [Fact]
    public async Task TheDocumentedDiagramMatchesTheLifecycleAsync()
    {
        var diagram = await StateDiagram.CompareAsync(
            "canvas-lifecycle.md",
            "Canvas lifecycle",
            nameof(CanvasLifecycle),
            CanvasLifecycle.Create(() => CanvasState.Streaming, _ => { }).GetInfo(),
            TestContext.Current.CancellationToken);

        Assert.Equal(diagram.Expected, diagram.Documented);
    }
}
