using Avala.Components.Canvases;
using Avala.Components.UI.Markdown;
using Avala.Rendering.UI;
using Avala.Sdk.UI;
using Avala.Testing.UI;
using Avalonia.Controls;

namespace Avala.Rendering.Tests.Renderers;

[CollectionDefinition(Leaks.Collection, DisableParallelization = true)]
public sealed class LeaksRunAlone;

[Collection(Leaks.Collection)]
public sealed class MarkdownLeakScripts(HeadlessUi ui)
{
    [Fact]
    public Task ShowingAndClosingAMarkdownCanvasTwentyTimesReturnsMemoryToItsBaselineAsync() =>
        ui.RunAsync(async () =>
        {
            var views = new ViewRegistry();
            new RenderingPlugin().RegisterViews(views);
            var document = string.Concat(Enumerable.Range(0, 20).Select(step => $"## Step {step}\n\nWhy **step {step}** matters, with `code`.\n\n- one\n- two\n\n"));
            var rendering = new CanvasRendering(CanvasMediaTypes.Markdown, document, true, 1);
            var view = ViewScript.Show(new TextBlock(), new object());
            view.Window.DataTemplates.Add(views);
            var shown = 0;

            var retention = await Leaks.ManagedAcrossAsync(async () =>
            {
                view.Window.Content = new ContentControl { Content = rendering };
                await view.Settle().DrawnAsync();
                shown += view.All<MarkdownText>().Count;
                view.Window.Content = null;
                view.Settle();
            });

            Assert.Equal(Leaks.Cycles, shown);
            Assert.True(retention.IsBounded, retention.ToString());
        }, TestContext.Current.CancellationToken);
}
