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
            var waiting = stage.Presented;
            view.Settle();

            Assert.Null(waiting);
            Assert.True(view.Shows("Page"));
        }, Cancellation);

    [Fact]
    public Task AStageThatKeepsItsContentAliveShowsAPageAgainWithoutRebuildingItAsync() =>
        ui.RunAsync(() =>
        {
            var stage = new Stage { KeepsAlive = true };
            var view = ViewScript.Show(stage, new object());
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
    public Task AStageThatDoesNotKeepItsContentLetsThePreviousViewGoAsync() =>
        ui.RunAsync(() =>
        {
            var stage = new Stage();
            var view = ViewScript.Show(stage, new object());
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

    private static Border Entering(string name)
    {
        var entry = new Border { Name = name, Width = 10, Height = 10 };
        Motion.SetEnter(entry, MotionEntrance.Fade);

        return entry;
    }
}
