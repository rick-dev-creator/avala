using System.Text.Json;
using Avala.Agents.Contracts.Sessions;
using Avala.Canvas.Contracts;
using Avala.Components.Canvases;
using Avala.Host.Composition;
using Avala.Sdk;
using Avala.Testing;
using Avala.Testing.UI;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

public sealed class CanvasOfferTests(HeadlessUi ui, PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheCanvasToolOffersExactlyTheMediaTypesTheComposedApplicationDrawsAsync()
    {
        await using var data = new TemporaryFolder();
        await using var root = CompositionRoot.Create(plugins.Directory, new AvalaPaths(data.Path));
        var tool = Assert.Single(root.Services.GetServices<HarnessTool>(), tool => tool.Surface == ToolSurface.Canvas);
        using var schema = JsonDocument.Parse(tool.InputSchema);

        var offered = schema.RootElement.GetProperty("properties").GetProperty("mediaType").GetProperty("enum").EnumerateArray().Select(type => type.GetString()!).ToList();

        Assert.Equal(["image/svg+xml", "text/markdown"], offered);
        Assert.Equal(offered, root.Services.GetServices<CanvasFormat>().Select(format => format.MediaType));
        Assert.All(offered, mediaType => Assert.True(root.Views.Match(new CanvasRendering(mediaType, "x", true, 1))));
        Assert.All(["text/vnd.mermaid", "text/html"], mediaType => Assert.False(root.Views.Match(new CanvasRendering(mediaType, "x", true, 1))));
    }

    [Fact]
    public async Task TheCanvasScenarioDrawsItsSvgAndMarkdownCanvasesInTheConversationAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "canvas");

        var surfaces = await CanvasSurfacesAsync(run, count: 3);

        Assert.Equal(
            [("Architecture", "Svg"), ("Job flow", "Svg"), ("Notes", "Markdown")],
            await DrawnAsync(run, surfaces));
    }

    [Fact]
    public async Task ACanvasInAMediaTypeThatIsNotOfferedIsShownAsItsSourceWithANoteAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "unoffered-canvas");

        var snapshot = (await run.CanvasSnapshotsAsync(canvasCount: 1))[^1];
        var surfaces = await CanvasSurfacesAsync(run, count: 1);

        Assert.Equal(("text/html", false, "<h1>Job report</h1>\n<p>Submitted, then running.</p>\n"), (snapshot.MediaType, snapshot.IsOffered, snapshot.Content));
        await ui.RunAsync(() => Presented(run, Assert.Single(surfaces), view =>
        {
            Assert.Equal("CanvasFallback", Drawing(view));
            Assert.Equal(
                "Avala does not offer HTML canvases to agents, so this one is not drawn. Showing its source.",
                view.TextOf("CanvasNote"));
        }), Cancellation);
    }

    private static async Task<IReadOnlyList<ICanvasSurfaceViewModel>> CanvasSurfacesAsync(SimulatedRun run, int count)
    {
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() => Canvases(conversation).Count(canvas => canvas["Status"].Value<CanvasStatus>() == CanvasStatus.Completed) == count);

        return await run.Ui.ReadAsync(() => Canvases(conversation).Select(canvas => canvas["Surface"].Value<ICanvasSurfaceViewModel>()).ToList());
    }

    private static List<Bound> Canvases(Bound conversation) =>
        [.. conversation["Entries"].Items.Where(entry => entry.Kind == "CanvasViewModel")];

    private async Task<List<(string Title, string Drawing)>> DrawnAsync(SimulatedRun run, IReadOnlyList<ICanvasSurfaceViewModel> surfaces)
    {
        List<(string, string)> drawn = [];
        await ui.RunAsync(
            () =>
            {
                foreach (var surface in surfaces)
                {
                    Presented(run, surface, view => drawn.Add((surface.Title, Drawing(view))));
                }
            },
            Cancellation);

        return drawn;
    }

    private static void Presented(SimulatedRun run, ICanvasSurfaceViewModel surface, Action<ViewScript> inspect)
    {
        Application.Current!.DataTemplates.Add(run.Views);

        try
        {
            inspect(ViewScript.Show(surface));
        }
        finally
        {
            Application.Current.DataTemplates.Remove(run.Views);
        }
    }

    private static string Drawing(ViewScript view) =>
        ((string[])["Svg", "Markdown", "CanvasFallback"]).FirstOrDefault(view.Shows) ?? "nothing";
}
