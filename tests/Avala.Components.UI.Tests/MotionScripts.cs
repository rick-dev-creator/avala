using Avala.Components.Status;
using Avala.Components.UI.Theme;
using Avala.Testing.UI;
using Avalonia.Controls;

namespace Avala.Components.UI.Tests;

public sealed class MotionScripts(HeadlessUi ui)
{
    private static readonly string[] Entrances = ["fade-in", "arrive-snappy", "arrive-gentle", "sheet-in", "pop-in", "fold-in", "rise-in", "stamp"];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task WithFullMotionTheEntrancesStartFromTheirFirstFrameAsync() =>
        ui.RunAsync(() =>
        {
            var view = Arrive(Entering(), reduced: false);

            Assert.All(Entrances, entrance => Assert.Equal(0, view.Find(entrance).Opacity));
        }, Cancellation);

    [Fact]
    public Task WithReducedMotionTheEntrancesShowTheirFinalStateAtOnceAsync() =>
        ui.RunAsync(() =>
        {
            var view = Arrive(Entering(), reduced: true);

            Assert.All(Entrances, entrance => Assert.Equal((1d, true), (view.Find(entrance).Opacity, Unmoved(view.Find(entrance)))));
        }, Cancellation);

    [Fact]
    public Task SwitchingReducedMotionStopsAndRestartsTheWorkingPulseAsync() =>
        ui.RunAsync(() =>
        {
            var view = ViewScript.Show(new StatusDotViewModel(StatusKind.Working));
            var pulsing = view.Find("Pulse").Opacity;

            Motion.SetIsReduced(view.Window, true);
            view.Settle();
            var still = view.Find("Pulse").Opacity;
            Motion.SetIsReduced(view.Window, false);
            view.Settle();

            Assert.Equal((0.7, 1d, 0.7), (pulsing, still, view.Find("Pulse").Opacity));
        }, Cancellation);

    [Fact]
    public Task WithReducedMotionTheThinkingShimmerIsHiddenAndItsDotsAndCaretHoldStillAsync() =>
        ui.RunAsync(() =>
        {
            var moving = Arrive(Thinking(), reduced: false);
            var held = Arrive(Thinking(), reduced: true);

            Assert.Equal((1d, 0.25, 1d), (moving.Find("Shimmer").Opacity, moving.Find("Dot").Opacity, moving.Find("Caret").Opacity));
            Assert.False(Unmoved(moving.Find("Shimmer")));
            Assert.Equal((0d, 0.6, 1d), (held.Find("Shimmer").Opacity, held.Find("Dot").Opacity, held.Find("Caret").Opacity));
            Assert.True(Unmoved(held.Find("Shimmer")));
        }, Cancellation);

    [Fact]
    public Task ReducedMotionReachesEveryControlInsideTheWindowAsync() =>
        ui.RunAsync(() =>
        {
            var inner = new Border();
            var view = ViewScript.Show(new Border { Child = new Decorator { Child = inner } }, new object());

            Motion.SetIsReduced(view.Window, true);

            Assert.True(Motion.GetIsReduced(inner));
        }, Cancellation);

    private static bool Unmoved(Control control) => control.RenderTransform is not { } transform || transform.Value.IsIdentity;

    private static ViewScript Arrive(Control content, bool reduced)
    {
        var view = ViewScript.Show(new Border(), new object());
        Motion.SetIsReduced(view.Window, reduced);
        view.Window.Content = content;

        return view.Settle();
    }

    private static StackPanel Entering()
    {
        var panel = new StackPanel();

        foreach (var entrance in Entrances)
        {
            panel.Children.Add(new Border { Name = entrance, Width = 10, Height = 10, Classes = { entrance } });
        }

        return panel;
    }

    private static StackPanel Thinking() => new()
    {
        Children =
        {
            new Border { Name = "Shimmer", Width = 48, Height = 12, Classes = { "shimmer-window" } },
            new Border { Name = "Dot", Width = 4, Height = 4, Classes = { "think-dot" } },
            new TextBlock { Name = "Caret", Text = "|", Classes = { "caret" } },
        },
    };
}
