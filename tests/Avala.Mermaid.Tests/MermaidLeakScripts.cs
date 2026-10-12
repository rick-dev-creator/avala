using Avala.Components.Canvases;
using Avala.Mermaid.UI;
using Avala.Rendering.UI;
using Avala.Sdk.UI;
using Avala.Testing.UI;
using Avalonia.Controls;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Mermaid.Tests;

[CollectionDefinition(Leaks.Collection, DisableParallelization = true)]
public sealed class LeaksRunAlone;

[Collection(Leaks.Collection)]
public sealed class MermaidLeakScripts(HeadlessUi ui)
{
    [Fact]
    public Task ShowingAndClosingAMermaidCanvasTwentyTimesReturnsMemoryToItsBaselineAsync() =>
        ui.RunAsync(async () =>
        {
            var views = new ViewRegistry();
            new RenderingPlugin().RegisterViews(views);
            new MermaidPlugin(new FakeTimeProvider()).RegisterViews(views);
            var rendering = new CanvasRendering(CanvasMediaTypes.Mermaid, "flowchart LR\n  Opened --> Closed\n  Closed --> Opened\n", true, 1);
            var view = ViewScript.Show(new TextBlock(), new object());
            view.Window.DataTemplates.Add(views);
            var shown = 0;

            var (baseline, after) = await Leaks.ManagedAcrossAsync(async () =>
            {
                view.Window.Content = new ContentControl { Content = rendering };
                await view.Settle().DrawnAsync();
                shown += view.Shows("Svg") ? 1 : 0;
                view.Window.Content = null;
                view.Settle();
            });

            Assert.Equal(Leaks.Cycles, shown);
            Assert.InRange(after - baseline, long.MinValue, Leaks.Tolerance);
        }, TestContext.Current.CancellationToken);
}
