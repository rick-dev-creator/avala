using Avala.Components.UI.Theme;
using Avala.Testing.UI;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Avala.Components.UI.Tests;

public sealed class StageScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task NewContentIsBuiltOnlyAfterWhatTheClickChangedHasBeenLaidOutAsync() =>
        ui.RunAsync(() =>
        {
            var stage = new Stage();
            var view = ViewScript.Show(stage, new object());

            stage.Content = new Border { Name = "Page" };
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
            var waiting = stage.Children.Count;
            view.Settle();

            Assert.Equal(0, waiting);
            Assert.True(view.Shows("Page"));
        }, Cancellation);

    [Fact]
    public Task TheCurrentViewStaysOnScreenWhileTheNewOneIsBuiltAndBothCrossFadeOnceItIsReadyAsync() =>
        ui.RunAsync(() =>
        {
            var stage = new Stage();
            var view = ViewScript.Show(stage, new object());
            stage.Content = new Border { Name = "First" };
            view.Settle();

            stage.Content = new Border { Name = "Second" };
            view.Settle();

            Assert.Equal((1d, 0d), (Presenter(view, "First").Opacity, Presenter(view, "Second").Opacity));
            Assert.True(view.Shows("First"));
            Assert.Equal((false, true), (Presenter(view, "First").IsHitTestVisible, Presenter(view, "Second").IsHitTestVisible));
        }, Cancellation);

    [Fact]
    public Task WithReducedMotionTheNewViewReplacesTheCurrentOneAtOnceWhenItIsReadyAsync() =>
        ui.RunAsync(() =>
        {
            var stage = new Stage();
            var view = ViewScript.Show(stage, new object());
            Motion.SetIsReduced(view.Window, true);
            stage.Content = new Border { Name = "First" };
            view.Settle();

            stage.Content = new Border { Name = "Second" };
            view.Settle();

            Assert.Equal((false, 1d), (view.Shows("First"), Assert.Single(stage.Children).Opacity));
            Assert.True(view.Shows("Second"));
        }, Cancellation);

    [Fact]
    public Task ReducingMotionDuringACrossFadeFinishesItAtOnceAsync() =>
        ui.RunAsync(() =>
        {
            var stage = new Stage();
            var view = ViewScript.Show(stage, new object());
            stage.Content = new Border { Name = "First" };
            view.Settle();
            stage.Content = new Border { Name = "Second" };
            view.Settle();
            var fading = stage.Children.Count;

            Motion.SetIsReduced(view.Window, true);
            view.Settle();

            Assert.Equal(2, fading);
            Assert.Equal((false, 1d), (view.Shows("First"), Assert.Single(stage.Children).Opacity));
        }, Cancellation);

    [Fact]
    public Task AStageThatKeepsItsContentAliveShowsAPageAgainWithoutRebuildingItAsync() =>
        ui.RunAsync(() =>
        {
            var stage = new Stage { Keeps = 4 };
            var view = ViewScript.Show(stage, new object());
            Motion.SetIsReduced(view.Window, true);
            var first = new Border { Name = "First" };

            stage.Content = first;
            view.Settle();
            var built = first.GetVisualParent();
            stage.Content = new Border { Name = "Second" };
            view.Settle();
            var hidden = (view.Shows("First"), view.Shows("Second"));
            stage.Content = first;
            view.Settle();

            Assert.Equal((false, true), hidden);
            Assert.Same(built, first.GetVisualParent());
            Assert.Equal((true, false, 2), (view.Shows("First"), view.Shows("Second"), stage.Children.Count));
        }, Cancellation);

    [Fact]
    public Task AStageKeepsOnlyItsMostRecentViewsAliveAsync() =>
        ui.RunAsync(() =>
        {
            var stage = new Stage { Keeps = 2 };
            var view = ViewScript.Show(stage, new object());
            Motion.SetIsReduced(view.Window, true);
            var first = new Border { Name = "First" };

            foreach (var content in new[] { first, new Border { Name = "Second" }, new Border { Name = "Third" } })
            {
                stage.Content = content;
                view.Settle();
            }

            Assert.Equal((2, false), (stage.Children.Count, TopLevel.GetTopLevel(first) is not null));
        }, Cancellation);

    [Fact]
    public Task AStageThatDoesNotKeepItsContentLetsThePreviousViewGoAsync() =>
        ui.RunAsync(() =>
        {
            var stage = new Stage();
            var view = ViewScript.Show(stage, new object());
            Motion.SetIsReduced(view.Window, true);
            var first = new Border { Name = "First" };

            stage.Content = first;
            view.Settle();
            stage.Content = new Border { Name = "Second" };
            view.Settle();

            Assert.Equal((false, true, 1), (TopLevel.GetTopLevel(first) is not null, view.Shows("Second"), stage.Children.Count));
        }, Cancellation);

    [Theory]
    [InlineData(false, 0d)]
    [InlineData(true, 1d)]
    public Task NewContentFadesInUnlessMotionIsReducedAsync(bool reduced, double opacity) =>
        ui.RunAsync(() =>
        {
            var stage = new Stage();
            var view = ViewScript.Show(stage, new object());
            Motion.SetIsReduced(view.Window, reduced);

            stage.Content = new Border { Name = "Page" };
            view.Settle();

            Assert.Equal(opacity, Assert.Single(stage.Children).Opacity);
        }, Cancellation);

    [Fact]
    public Task EntrancesInsideNewlyStagedContentHoldStillWhileLaterArrivalsPlayAsync() =>
        ui.RunAsync(() =>
        {
            var stage = new Stage();
            var view = ViewScript.Show(stage, new object());
            var list = new StackPanel { Children = { Entering("Old") } };

            stage.Content = list;
            view.Settle();
            list.Children.Add(Entering("New"));
            view.Settle();

            Assert.Equal((1d, 0d), (view.Find("Old").Opacity, view.Find("New").Opacity));
        }, Cancellation);

    private static Control Presenter(ViewScript view, string name) => (Control)view.Find(name).GetVisualParent()!;

    private static Border Entering(string name)
    {
        var entry = new Border { Name = name, Width = 10, Height = 10 };
        Motion.SetEnter(entry, MotionEntrance.Fade);

        return entry;
    }
}
