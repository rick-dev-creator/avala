using Avala.Components.UI.Virtualizing;
using Avala.Components.Virtualizing;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Avala.Components.UI.Tests;

public sealed class TimelinePanelScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ALongTimelineOpensAtItsNewestEntryWithoutBuildingItsFirstOnesAsync() =>
        ui.RunAsync(() =>
        {
            var list = List(Rows(200, _ => 50));
            var built = new List<int>();
            list.ContainerPrepared += (_, prepared) => built.Add(prepared.Index);
            var view = ViewScript.Show(new ScrollViewer { Name = "Scroller", Content = list }, new object());
            var scroller = view.Find<ScrollViewer>("Scroller");

            Assert.NotNull(list.ContainerFromIndex(199));
            Assert.InRange(built.Min(), 170, 199);
            Assert.Equal(scroller.Extent.Height - scroller.Viewport.Height, scroller.Offset.Y, 1);
        }, Cancellation);

    [Fact]
    public Task WhenIdleTheScreenfulAboveTheViewIsBuiltAheadAndNothingFurtherAsync() =>
        ui.RunAsync(() =>
        {
            var list = List(Rows(200, _ => 50));
            ViewScript.Show(new ScrollViewer { Content = list }, new object());

            Assert.NotNull(list.ContainerFromIndex(184));
            Assert.Null(list.ContainerFromIndex(150));
        }, Cancellation);

    [Fact]
    public Task EntriesOnceMeasuredKeepTheirHeightsSoScrollingBackOverThemLeavesTheExtentAloneAsync() =>
        ui.RunAsync(() =>
        {
            var list = List(Rows(120, index => index % 3 == 0 ? 180 : 24));
            var view = ViewScript.Show(new ScrollViewer { Name = "Scroller", Content = list }, new object());
            var scroller = view.Find<ScrollViewer>("Scroller");
            Walk(view, scroller, -200, () => scroller.Offset.Y > 0);
            var measured = scroller.Extent.Height;
            var extents = new List<double>();

            Walk(view, scroller, 200, () =>
            {
                extents.Add(scroller.Extent.Height);

                return scroller.Offset.Y < scroller.Extent.Height - scroller.Viewport.Height;
            });

            Assert.All(extents, extent => Assert.Equal(measured, extent, 1));
        }, Cancellation);

    [Fact]
    public Task AnEntryNotYetBuiltIsEstimatedTallerTheLongerItsTextAsync() =>
        ui.RunAsync(() =>
        {
            var shortText = Extent([new Text("First", 40, 20), .. Rows(60, _ => 50)]);
            var longText = Extent([new Text("First", 40, 6000), .. Rows(60, _ => 50)]);

            Assert.True(longText - shortText > 500, $"a 6,000-character entry adds {longText - shortText} px over a 20-character one");
        }, Cancellation);

    [Fact]
    public Task BuildingEntriesAboveTheViewKeepsWhatTheReaderSeesInPlaceAsync() =>
        ui.RunAsync(() =>
        {
            var list = List(Rows(200, index => index < 180 ? 200 : 20));
            var view = ViewScript.Show(new ScrollViewer { Name = "Scroller", Content = list }, new object());
            var scroller = view.Find<ScrollViewer>("Scroller");
            scroller.Offset = scroller.Offset.WithY(scroller.Offset.Y / 2);
            view.Window.UpdateLayout();
            var seen = Topmost(view, list);
            var before = seen.TranslatePoint(default, scroller)!.Value.Y;

            view.Settle();

            Assert.Equal(before, seen.TranslatePoint(default, scroller)!.Value.Y, 1);
        }, Cancellation);

    [Fact]
    public Task NewItemsOpenAtTheirNewestEntryAsync() =>
        ui.RunAsync(() =>
        {
            var list = List(Rows(10, _ => 50));
            var view = ViewScript.Show(new ScrollViewer { Name = "Scroller", Content = list }, new object());
            var scroller = view.Find<ScrollViewer>("Scroller");

            list.ItemsSource = Rows(300, _ => 40);
            view.Settle();

            Assert.NotNull(list.ContainerFromIndex(299));
            Assert.Null(list.ContainerFromIndex(0));
            Assert.Equal(scroller.Extent.Height - scroller.Viewport.Height, scroller.Offset.Y, 1);
        }, Cancellation);

    [Fact]
    public Task ScrollingAnEntryIntoViewBuildsItWhereTheReaderCanSeeItAsync() =>
        ui.RunAsync(() =>
        {
            var list = List(Rows(200, _ => 50));
            var view = ViewScript.Show(new ScrollViewer { Name = "Scroller", Content = list }, new object());

            list.ScrollIntoView(10);
            view.Settle();
            var top = list.ContainerFromIndex(10)!.TranslatePoint(default, view.Find<ScrollViewer>("Scroller"))!.Value.Y;

            Assert.InRange(top, 0, view.Find<ScrollViewer>("Scroller").Viewport.Height - 50);
        }, Cancellation);

    private static ItemsControl List(IEnumerable<object> items) =>
        new()
        {
            ItemsSource = items.ToList(),
            ItemTemplate = new FuncDataTemplate<object>((item, _) => item switch
            {
                Row row => new Border { Name = row.Name, Height = row.Height },
                Text text => new Border { Name = text.Name, Height = text.Height },
                _ => new Border(),
            }),
            ItemsPanel = new FuncTemplate<Panel?>(() => new TimelinePanel()),
        };

    private static List<object> Rows(int count, Func<int, double> height) =>
        [.. Enumerable.Range(0, count).Select(index => new Row($"Row {index}", height(index)))];

    private static double Extent(IEnumerable<object> items)
    {
        var view = ViewScript.Show(new ScrollViewer { Name = "Scroller", Content = List(items) }, new object());

        return view.Find<ScrollViewer>("Scroller").Extent.Height;
    }

    private static void Walk(ViewScript view, ScrollViewer scroller, double step, Func<bool> more)
    {
        for (var guard = 0; guard < 500 && more(); guard++)
        {
            scroller.Offset = scroller.Offset.WithY(Math.Max(0, scroller.Offset.Y + step));
            view.Settle();
        }
    }

    private static Control Topmost(ViewScript view, ItemsControl list) =>
        list.GetRealizedContainers()
            .Where(container => container.IsVisible)
            .Select(container => (Container: container, Top: container.TranslatePoint(default, view.Window)!.Value.Y))
            .Where(pair => pair.Top >= 0)
            .MinBy(pair => pair.Top).Container;

    private sealed record Row(string Name, double Height);

    private sealed record Text(string Name, double Height, int TextLength) : ITextSized;
}
