using Avala.Preview.Pane;
using Avala.Preview.Contracts;
using Avala.Preview.Tests.Pane;
using Avala.Preview.UI;
using Avala.Testing.UI;
using Avalonia.Automation;
using Avalonia.Controls.Primitives;
using CommunityToolkit.Mvvm.Messaging;

[assembly: AssemblyFixture(typeof(HeadlessUi))]

namespace Avala.Preview.Tests.Views;

internal static class Screen
{
    private static bool registered;

    public static ViewScript Show(object viewModel)
    {
        if (!registered)
        {
            new PreviewPlugin().RegisterViews(HeadlessApp.Views);
            registered = true;
        }

        return ViewScript.Show(viewModel);
    }
}

public sealed class PreviewPaneViewScripts(HeadlessUi ui) : IDisposable
{
    private static readonly string[] IconButtons = ["Back", "Reload", "Network", "OpenInBrowser", "Close"];

    private readonly PaneBench bench = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheToolbarShowsTheAddressItsStateAndNamesEveryIconButtonAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(Opened());

            Assert.Equal(("localhost:24000", PreviewPhrases.Running), (view.TextOf("Address"), view.TextOf("State")));
            Assert.Equal(
                ["Back", "Reload", "Allow network", "Open in browser", "Close preview"],
                IconButtons.Select(name => AutomationProperties.GetName(view.Find(name))));
            Assert.True(view.Shows("Page"));
            Assert.False(view.Shows("RefusalBar"));
        }, Cancellation);

    [Fact]
    public Task ClosingFromTheToolbarClosesThePaneAsync() =>
        ui.RunAsync(() =>
        {
            var pane = Opened();
            var view = Screen.Show(pane);

            view.Click("Close");

            Assert.False(pane.IsOpen);
            Assert.True(bench.Surfaces.Last.IsClosed);
        }, Cancellation);

    [Fact]
    public Task ARefusedPageAsksBeforeOpeningItInTheBrowserAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(Opened());

            bench.Surfaces.Last.Follow(new Uri("https://example.com/"));
            view.Settle();
            Assert.Equal("Open example.com in your browser?", view.TextOf("Refusal"));
            view.Click("OpenRefused");

            Assert.Equal([new Uri("https://example.com/")], bench.Links.Opened);
            Assert.False(view.Shows("RefusalBar"));
        }, Cancellation);

    [Fact]
    public Task TheNetworkToggleShowsWhetherNetworkIsAllowedAsync() =>
        ui.RunAsync(() =>
        {
            var pane = Opened();
            var view = Screen.Show(pane);

            view.Click("Network");

            Assert.True(pane.AllowsNetwork);
            Assert.True(view.Find<ToggleButton>("Network").IsChecked);
        }, Cancellation);

    [Fact]
    public Task WithoutAnEmbeddableWebViewTheNoticeOffersTheBrowserAsync() =>
        ui.RunAsync(() =>
        {
            bench.Surfaces.Embedding = SurfaceEmbedding.None;
            var view = Screen.Show(Opened());

            view.Click("NoticeOpen");

            Assert.False(view.Shows("Page"));
            Assert.Equal(PreviewPhrases.Notice(SurfaceEmbedding.None), view.TextOf("NoticeText"));
            Assert.Equal([new Uri("http://localhost:24000/")], bench.Links.Opened);
        }, Cancellation);

    public void Dispose() => bench.Dispose();

    private PreviewPaneViewModel Opened()
    {
        bench.Serve(bench.Job, 24_000);
        var pane = bench.Pane();
        pane.OnRegionContextChanged(bench.Job);
        bench.Messenger.Send(new PreviewRequested(bench.Job));

        return pane;
    }
}

public sealed class RunningChipViewScripts(HeadlessUi ui) : IDisposable
{
    private readonly PaneBench bench = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ClickingTheChipAsksForThePreviewAsync() =>
        ui.RunAsync(() =>
        {
            bench.Serve(bench.Job, 24_000);
            var asked = new List<PreviewRequested>();
            bench.Messenger.Register<List<PreviewRequested>, PreviewRequested>(asked, (list, message) => list.Add(message));
            var chip = bench.Chip(ChipPlacement.Header);
            chip.OnRegionContextChanged(bench.Job);
            var view = Screen.Show(chip);

            view.Click("Chip");

            Assert.Equal("Running · localhost:24000", view.TextOf("ChipText"));
            Assert.False(view.Shows("Unleased"));
            Assert.Equal([new PreviewRequested(bench.Job)], asked);
        }, Cancellation);

    [Fact]
    public Task AnUnleasedListenerCarriesItsTagAsync() =>
        ui.RunAsync(() =>
        {
            bench.Serve(bench.Job, 5_173, leased: false);
            var chip = bench.Chip(ChipPlacement.Header);
            chip.OnRegionContextChanged(bench.Job);

            Assert.True(Screen.Show(chip).Shows("Unleased"));
        }, Cancellation);

    public void Dispose() => bench.Dispose();
}

public sealed class RunScriptViewScripts(HeadlessUi ui) : IDisposable
{
    private readonly PaneBench bench = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheRunButtonNamesItsScriptAndBecomesStopOnceClickedAsync() =>
        ui.RunAsync(async () =>
        {
            bench.Previews.Scripts[bench.Job] = "dev";
            var run = bench.Run();
            run.OnRegionContextChanged(bench.Job);
            await run.Finding;
            var view = Screen.Show(run);
            Assert.Equal("Run dev", view.TextOf("RunLabel"));

            view.Click("Run");
            await run.ToggleCommand.ExecutionTask!;
            view.Settle();

            Assert.Equal("Stop dev", view.TextOf("RunLabel"));
        }, Cancellation);

    [Fact]
    public Task ANothingToRunJobShowsNoButtonAsync() =>
        ui.RunAsync(async () =>
        {
            var run = bench.Run();
            run.OnRegionContextChanged(bench.Job);
            await run.Finding;

            Assert.False(Screen.Show(run).Shows("Run"));
        }, Cancellation);

    public void Dispose() => bench.Dispose();
}
