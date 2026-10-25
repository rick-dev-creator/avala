using Avala.Components.Canvases;
using Avala.Testing.UI;
using Avalonia;

namespace Avala.Host.Tests;

public sealed class FilePreviewTests(HeadlessUi ui, PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AWrittenSvgIsDrawnInItsPreviewAndAWrittenPageOpensFromTheWorktreeBesideTheConversationAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "previews");
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() => Previewing(conversation).Count == 2);
        var (drawing, page) = await run.Ui.ReadAsync(() => (Previewing(conversation)[0]["Preview"], Previewing(conversation)[1]["Preview"]));

        await run.Ui.RunAsync(() => drawing.ExecuteAsync("OpenCommand"));
        await run.Ui.RunAsync(() => page.ExecuteAsync("OpenCommand"));
        var shown = await run.Ui.ReadAsync(() => (drawing["IsOpen"].Value<bool>(), drawing["HasThumbnail"].Value<bool>(), drawing["Drawing"].Value<CanvasRendering>().Content, page["IsOpen"].Value<bool>()));
        var drawn = false;
        await ui.RunAsync(
            async () =>
            {
                Application.Current!.DataTemplates.Add(run.Views);

                try
                {
                    drawn = (await ViewScript.Show(drawing.Target).DrawnAsync()).Shows("Svg");
                }
                finally
                {
                    Application.Current.DataTemplates.Remove(run.Views);
                }
            },
            Cancellation);

        Assert.Equal((true, true, await File.ReadAllTextAsync(Path.Combine(run.Worktree, "orbits.svg"), Cancellation), false), shown);
        Assert.True(drawn);
        await workbench.ShowsAsync(() => run.Surfaces.Opened.Count == 1 && run.Surfaces.Opened[0].Visited.Count == 1);
        var visited = run.Surfaces.Opened[0].Visited[0];
        Assert.Equal(("127.0.0.1", "/sistema-solar.html", "?v=1"), (visited.Host, visited.AbsolutePath, visited.Query));
        Assert.Empty(run.Links.Opened);
    }

    private static List<Bound> Previewing(Bound conversation) =>
        [.. conversation["Entries"].Items.Where(entry => entry.Kind == "ToolViewModel" && entry["ShowsPreview"].Value<bool>())];
}
