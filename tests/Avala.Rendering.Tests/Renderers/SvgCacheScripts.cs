using System.Globalization;
using Avala.Components.Canvases;
using Avala.Rendering.UI.Svg;
using Avala.Testing.UI;
using Avalonia.Controls;

namespace Avala.Rendering.Tests.Renderers;

public sealed class SvgCacheScripts(HeadlessUi ui)
{
    private const long Budget = 600_000;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task DrawingsShownAndClosedStayWithinTheBudgetAndThoseLetGoAreUnloadedAsync() =>
        ui.RunAsync(async () =>
        {
            var renderer = new SvgRenderer(Budget);
            var seen = new HashSet<SvgModel>();
            for (var round = 0; round < 20; round++)
            {
                await CycleAsync(renderer, seen, round);
            }

            var kept = renderer.Kept;

            Assert.InRange(kept.Sum(model => model.Bytes), 0, Budget);
            Assert.InRange(seen.Count - kept.Count, 1, 19);
            Assert.All(seen.Except(kept), model => Assert.False(model.IsLoaded));
        }, Cancellation);

    private static async Task CycleAsync(SvgRenderer renderer, HashSet<SvgModel> seen, int round)
    {
        var svg = Drawing(round);
        var drawing = renderer.Render(svg).Match(found => found, () => throw new InvalidOperationException("Not drawn."));
        var view = await ViewScript.Show(drawing, new CanvasRendering("image/svg+xml", svg, true, 1)).DrawnAsync();
        seen.UnionWith(renderer.Kept);
        view.Window.Close();
        view.Settle();
    }

    private static string Drawing(int round) =>
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1000 800\">"
        + string.Concat(Enumerable.Range(0, 400).Select(star => string.Create(
            CultureInfo.InvariantCulture,
            $"<circle cx=\"{(star * 397 + round) % 1000}\" cy=\"{(star * 211) % 800}\" r=\"{1 + (star % 4)}\" fill=\"#{(star * 2654435 + round) % 0xFFFFFF:x6}\"/>")))
        + "</svg>";
}
