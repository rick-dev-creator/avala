using Avala.Jobs.Contracts;
using Avala.Preview.Pane;
using Avala.Preview.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Testing;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Preview.Tests.Pane;

public sealed class PreviewPaneViewModelScripts : IDisposable
{
    private readonly PaneBench bench = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void AskingForTheSelectedJobsPreviewOpensThePaneOnItsAddress()
    {
        var target = bench.Serve(bench.Job, 24_000);

        Selected(bench.Pane())
            .When(_ => bench.Messenger.Send(new PreviewRequested(bench.Job)))
            .Then(pane =>
            {
                Assert.Equal((true, "localhost:24000", PreviewPhrases.Running, true), (pane.IsOpen, pane.Address, pane.State, pane.IsLive));
                Assert.Equal([FakeGateways.Through(target)], bench.Surfaces.Last.Visited);
                Assert.Same(bench.Surfaces.Last, pane.Surface);
            });
    }

    [Fact]
    public async Task APageThatLeavesTheAllowListIsRefusedAndOpensInTheBrowserOnlyWhenAskedAsync()
    {
        bench.Serve(bench.Job, 24_000);
        var script = Opened();
        var elsewhere = new Uri("https://example.com/signup");

        await script.WhenPresentedAsync(_ => bench.Surfaces.Last.Follow(elsewhere), Cancellation);
        script.Then(pane => Assert.Equal((true, "Open example.com in your browser?"), (pane.HasRefusal, pane.Refusal)));
        Assert.Empty(bench.Links.Opened);

        await script.ViewModel.OpenRefusedCommand.ExecuteAsync(null);

        Assert.Equal([elsewhere], bench.Links.Opened);
        script.Then(pane => Assert.False(pane.HasRefusal));
    }

    [Fact]
    public async Task DismissingARefusalKeepsThePageWhereItWasAsync()
    {
        var target = bench.Serve(bench.Job, 24_000);
        var script = Opened();
        await script.WhenPresentedAsync(_ => bench.Surfaces.Last.Follow(new Uri("http://localhost:8080/")), Cancellation);

        script.Invoke(nameof(IPreviewPaneViewModel.DismissRefusalCommand))
            .Then(pane =>
            {
                Assert.False(pane.HasRefusal);
                Assert.Equal(Option<Uri>.Some(FakeGateways.Through(target)), bench.Surfaces.Last.Address);
            });
        Assert.Empty(bench.Links.Opened);
    }

    [Fact]
    public void ReloadReloadsThePage()
    {
        bench.Serve(bench.Job, 24_000);

        Opened()
            .Invoke(nameof(IPreviewPaneViewModel.ReloadCommand))
            .Then(_ => Assert.Equal(1, bench.Surfaces.Last.Reloads));
    }

    [Fact]
    public void BackIsOfferedOnceThePageHasMovedAndReturnsToTheEarlierPage()
    {
        var target = bench.Serve(bench.Job, 24_000);
        var script = Opened();
        Assert.False(script.CanInvoke(nameof(IPreviewPaneViewModel.BackCommand)));

        bench.Surfaces.Last.Follow(new Uri("http://localhost:24000/orders"));

        script.Invoke(nameof(IPreviewPaneViewModel.BackCommand));
        Assert.Equal(Option<Uri>.Some(FakeGateways.Through(target)), bench.Surfaces.Last.Address);
        Assert.False(script.CanInvoke(nameof(IPreviewPaneViewModel.BackCommand)));
    }

    [Fact]
    public async Task OpenInBrowserOpensThePageShownOnTheServersOwnAddressAsync()
    {
        var target = bench.Serve(bench.Job, 24_000);
        var script = Opened();
        bench.Surfaces.Last.Follow(new Uri(FakeGateways.Through(target), "/orders?page=2"));

        await script.ViewModel.OpenInBrowserCommand.ExecuteAsync(null);

        Assert.Equal([new Uri("http://localhost:24000/orders?page=2")], bench.Links.Opened);
    }

    [Fact]
    public void LeavingTheJobsPageClosesThePageAndComingBackOpensItAgain()
    {
        var target = bench.Serve(bench.Job, 24_000);
        var script = Opened();
        var left = bench.Surfaces.Last;

        script.ViewModel.Deactivate();

        Assert.True(left.IsClosed);
        Assert.Null(script.ViewModel.Surface);

        script.ViewModel.Activate();

        script.Then(pane =>
        {
            Assert.True(pane.IsOpen);
            Assert.NotSame(left, pane.Surface);
            Assert.Equal([FakeGateways.Through(target)], bench.Surfaces.Last.Visited);
        });
    }

    [Fact]
    public void EveryPageThePaneOpenedIsClosedOnceItsJobIsLeft()
    {
        var pane = bench.Pane();

        for (var round = 0; round < 5; round++)
        {
            var job = JobId.New();
            bench.Serve(job, 24_000 + round);
            pane.OnRegionContextChanged(job);
            bench.Messenger.Send(new PreviewRequested(job));
        }

        pane.OnRegionContextChanged(bench.Other);

        Assert.Equal(5, bench.Surfaces.Opened.Count);
        Assert.All(bench.Surfaces.Opened, surface => Assert.True(surface.IsClosed));
        pane.Dispose();
    }

    [Fact]
    public void CloseClosesThePageAndThePane()
    {
        bench.Serve(bench.Job, 24_000);

        Opened()
            .Invoke(nameof(IPreviewPaneViewModel.CloseCommand))
            .Then(pane =>
            {
                Assert.Equal((false, null), (pane.IsOpen, pane.Surface));
                Assert.True(bench.Surfaces.Last.IsClosed);
            });
    }

    [Fact]
    public async Task AServerThatStopsLeavesThePaneOpenAsStoppedUntilItListensAgainAsync()
    {
        var target = bench.Serve(bench.Job, 24_000);
        var script = Opened();

        await script.WhenPresentedAsync(_ => bench.Stop(target), Cancellation);
        script.Then(pane => Assert.Equal((true, PreviewPhrases.Stopped, false), (pane.IsOpen, pane.State, pane.IsLive)));

        await script.WhenPresentedAsync(_ => bench.Serve(bench.Job, 24_001), Cancellation);
        script.Then(pane => Assert.Equal(("localhost:24001", true), (pane.Address, pane.IsLive)));
        Assert.Equal(new Uri("http://127.0.0.1:34001/"), bench.Surfaces.Last.Visited[^1]);
    }

    [Fact]
    public void ThePaneFollowsTheSelectionToAnotherJobsPreviewOrClosesWhenItHasNone()
    {
        bench.Serve(bench.Job, 24_000);
        var other = bench.Serve(bench.Other, 24_005);
        var script = Opened();

        script.When(pane => pane.OnRegionContextChanged(bench.Other))
            .Then(pane =>
            {
                Assert.Equal((true, "localhost:24005"), (pane.IsOpen, pane.Address));
                Assert.True(bench.Surfaces.Opened[0].IsClosed);
                Assert.Equal([FakeGateways.Through(other)], bench.Surfaces.Last.Visited);
            })
            .When(pane => pane.OnRegionContextChanged(JobId.New()))
            .Then(pane => Assert.Equal((false, null), (pane.IsOpen, pane.Surface)));
    }

    [Fact]
    public void ResourcesFromOtherOriginsLoadOnlyOnceNetworkIsAllowedForThatJobThisSession()
    {
        bench.Serve(bench.Job, 24_000);
        bench.Serve(bench.Other, 24_005);
        var cdn = new Uri("https://unpkg.com/three@0.170.0/build/three.module.js");
        var script = Opened();
        Assert.False(bench.Surfaces.Last.Loads(cdn));

        script.Invoke(nameof(IPreviewPaneViewModel.ToggleNetworkCommand))
            .Then(pane =>
            {
                Assert.True(pane.AllowsNetwork);
                Assert.True(bench.Surfaces.Last.Loads(cdn));
                Assert.Equal(1, bench.Surfaces.Last.Reloads);
                Assert.True(bench.Pages.Network[bench.Job]);
            })
            .When(pane => pane.OnRegionContextChanged(bench.Other))
            .Then(pane => Assert.False(pane.AllowsNetwork))
            .When(pane => pane.OnRegionContextChanged(bench.Job))
            .Then(pane => Assert.True(pane.AllowsNetwork));
        Selected(bench.Pane()).Then(pane => Assert.False(pane.AllowsNetwork));
    }

    [Fact]
    public void WithoutAnEmbeddableWebViewThePaneSaysSoAndStillOpensInTheBrowser()
    {
        bench.Surfaces.Embedding = SurfaceEmbedding.None;
        bench.Serve(bench.Job, 24_000);

        Opened()
            .Then(pane =>
            {
                Assert.False(pane.IsEmbedded);
                Assert.Equal(PreviewPhrases.Notice(SurfaceEmbedding.None), pane.Notice);
                Assert.True(pane.OpenInBrowserCommand.CanExecute(null));
            });
    }

    [Fact]
    public async Task APreviewAskedForBeforeItsServerListensOpensAsStartingAndShowsThePageOnceItIsUpAsync()
    {
        var script = Selected(bench.Pane())
            .When(_ => bench.Messenger.Send(new PreviewRequested(bench.Job)))
            .Then(pane => Assert.Equal((true, PreviewPhrases.Starting, null), (pane.IsOpen, pane.State, pane.Surface)));

        var shown = await script.PresentedAsync(_ => bench.Serve(bench.Job, 24_000), pane => (pane.State, pane.Address), Cancellation);

        Assert.Equal((PreviewPhrases.Running, "localhost:24000"), shown);
    }

    [Fact]
    public async Task AServerThatStartsWhileItsJobIsSelectedOpensThePaneOnItsOwnAsync()
    {
        var script = Selected(bench.Pane());

        var shown = await script.PresentedAsync(_ => bench.Serve(bench.Job, 24_000), pane => (pane.IsOpen, pane.Address, pane.IsLive), Cancellation);

        Assert.Equal((true, "localhost:24000", true), shown);
    }

    [Fact]
    public async Task APaneClosedAfterOpeningOnItsOwnStaysClosedForThatServerAsync()
    {
        var script = Selected(bench.Pane());
        await script.WhenPresentedAsync(_ => bench.Serve(bench.Job, 24_000), Cancellation);

        script.Invoke(nameof(IPreviewPaneViewModel.CloseCommand));
        await script.WhenPresentedAsync(_ => bench.Serve(bench.Job, 24_000), Cancellation);

        script.Then(pane => Assert.Equal((false, null), (pane.IsOpen, pane.Surface)));
    }

    [Fact]
    public void AServerAlreadyRunningWhenItsJobIsSelectedLeavesThePaneClosed()
    {
        bench.Serve(bench.Job, 24_000);

        Selected(bench.Pane()).Then(pane => Assert.False(pane.IsOpen));
    }

    [Theory]
    [InlineData("http://localhost:24000/scene.html", true, "http://127.0.0.1:34000/scene.html")]
    [InlineData("http://127.0.0.1:24003/", true, "http://127.0.0.1:24003/")]
    [InlineData("http://localhost:8080/", false, "")]
    public void ALinkToOneOfTheSelectedJobsServersOpensInThePane(string link, bool shown, string visited)
    {
        bench.Serve(bench.Job, 24_000);
        var asked = new LocalPageRequested(new Uri(link));

        Selected(bench.Pane())
            .When(_ => bench.Messenger.Send(asked))
            .Then(pane =>
            {
                Assert.Equal((shown, shown), (asked.IsShown, pane.IsOpen));
                Assert.Equal(shown ? [new Uri(visited)] : [], bench.Surfaces.Opened.SelectMany(surface => surface.Visited));
            });
    }

    public void Dispose() => bench.Dispose();

    private ViewModelScript<PreviewPaneViewModel> Selected(PreviewPaneViewModel pane)
    {
        pane.OnRegionContextChanged(bench.Job);
        pane.Activate();

        return ViewModelScript.Given(pane);
    }

    private ViewModelScript<PreviewPaneViewModel> Opened() =>
        Selected(bench.Pane()).When(_ => bench.Messenger.Send(new PreviewRequested(bench.Job)));
}
