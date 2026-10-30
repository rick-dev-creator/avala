using System.Collections.ObjectModel;
using Avala.Components.UI.Theme;
using Avala.Components.UI.Virtualizing;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Avala.Components.UI.Tests;

public sealed class ArrivalsScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AnEntryScrolledBackIntoViewHoldsStillWhileANewEntryStillArrivesAsync() =>
        ui.RunAsync(() =>
        {
            var entries = new ObservableCollection<string>(Enumerable.Range(0, 200).Select(index => $"E{index}"));
            var (view, scroller) = Timeline(entries);
            view.Elapse(TimeSpan.FromSeconds(1));

            entries.Add("New");
            view.Settle();
            var arriving = view.Find("New").Opacity;
            view.Elapse(TimeSpan.FromSeconds(1));
            scroller.Offset = new Vector(0, 0);
            view.Settle();
            view.Elapse(TimeSpan.FromMilliseconds(30));

            Assert.Equal(0, arriving);
            Assert.All(Shown(view), entry => Assert.Equal(1, entry.Opacity, 2));
        }, Cancellation);

    [Fact]
    public Task EntriesScrolledBackStepByStepHoldStillAsync() =>
        ui.RunAsync(() =>
        {
            var entries = new ObservableCollection<string>(Enumerable.Range(0, 200).Select(index => $"E{index}"));
            var (view, scroller) = Timeline(entries);
            view.Elapse(TimeSpan.FromSeconds(1));

            for (var step = 0; step < 10; step++)
            {
                scroller.Offset = new Vector(0, scroller.Offset.Y - 40);
                view.Settle();
                view.Elapse(TimeSpan.FromMilliseconds(16));
            }

            Assert.All(Shown(view), entry => Assert.Equal(1, entry.Opacity, 2));
        }, Cancellation);

    private static List<Border> Shown(ViewScript view) => [.. view.All<Border>().Where(border => border.Name is { Length: > 0 } && border.IsEffectivelyVisible)];

    private static (ViewScript View, ScrollViewer Scroller) Timeline(ObservableCollection<string> entries)
    {
        var list = new ItemsControl
        {
            ItemsSource = entries,
            ItemTemplate = new FuncDataTemplate<string>((name, _) => Glitching(name)),
            ItemsPanel = new FuncTemplate<Panel?>(() => new TimelinePanel()),
        };
        Arrivals.SetOnlyNewEnter(list, true);
        var scroller = new ScrollViewer { Height = 300, Content = list };
        var view = ViewScript.Show(scroller, new object());
        Motion.SetIsReduced(view.Window, false);

        return (view.Settle(), scroller);
    }

    private static Border Glitching(string name)
    {
        var entry = new Border { Name = name, Height = 40 };
        Motion.SetEnter(entry, MotionEntrance.Glitch);

        return entry;
    }
}
