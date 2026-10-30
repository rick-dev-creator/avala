using Avala.Components.Status;
using Avala.Components.UI.Theme;
using Avala.Testing.UI;
using Avalonia.Controls;

namespace Avala.Components.UI.Tests;

public sealed class MotionScripts(HeadlessUi ui)
{
    private static readonly MotionEntrance[] Entrances = [MotionEntrance.Fade, MotionEntrance.Arrive, MotionEntrance.Gentle, MotionEntrance.Sheet, MotionEntrance.Pop, MotionEntrance.Fold, MotionEntrance.Rise, MotionEntrance.Stamp, MotionEntrance.Scale, MotionEntrance.Linger, MotionEntrance.Boot, MotionEntrance.Glitch, MotionEntrance.Close];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task WithFullMotionTheEntrancesStartFromTheirFirstFrameAsync() =>
        ui.RunAsync(() =>
        {
            var view = Arrive(Entering(), reduced: false);

            Assert.All(Entrances, entrance => Assert.Equal(0, view.Find(entrance.ToString()).Opacity));
        }, Cancellation);

    [Fact]
    public Task WithReducedMotionTheEntrancesShowTheirFinalStateAtOnceAsync() =>
        ui.RunAsync(() =>
        {
            var view = Arrive(Entering(), reduced: true);

            Assert.All(Entrances, entrance => Assert.Equal((1d, true), (view.Find(entrance.ToString()).Opacity, Unmoved(view.Find(entrance.ToString())))));
        }, Cancellation);

    [Fact]
    public Task AnEntranceWaitsForItsConditionAndPlaysWhenItTurnsTrueAsync() =>
        ui.RunAsync(() =>
        {
            var body = new Border { Name = "Body", Width = 10, Height = 10 };
            Motion.SetEnter(body, MotionEntrance.Fold);
            Motion.SetWhen(body, false);
            var view = Arrive(new StackPanel { Children = { body } }, reduced: false);
            var waiting = (body.Opacity, Unmoved(body));

            Motion.SetWhen(body, true);
            view.Settle();

            Assert.Equal((1d, true), waiting);
            Assert.Equal((0d, false), (body.Opacity, Unmoved(body)));
        }, Cancellation);

    [Fact]
    public Task NoThemeStyleMatchesEveryVisualSoOnlyTheElementsThatOptInPayForMotionAsync() =>
        ui.RunAsync(() =>
        {
            var animated = Animated(Avalonia.Application.Current!.Styles).ToList();

            Assert.NotEmpty(animated);
            Assert.DoesNotContain(animated, selector => selector.StartsWith(":is(", StringComparison.Ordinal));
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
    public Task ALoopUnderAHiddenParentLetsTheClockRestAndPlaysAgainOnceShownAsync() =>
        ui.RunAsync(() =>
        {
            var thinking = Thinking();
            var view = Arrive(new Border { Child = thinking }, reduced: false);
            var playing = ViewScript.IsAnimating;

            thinking.IsVisible = false;
            view.Settle();
            var hidden = ViewScript.IsAnimating;
            thinking.IsVisible = true;
            view.Settle();

            Assert.Equal((true, false, true), (playing, hidden, ViewScript.IsAnimating));
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

    [Fact]
    public Task ASwapFadesItsContentBackInWhenItsValueChangesButNotWhenFirstSetAsync() =>
        ui.RunAsync(() =>
        {
            var mark = Swapping("working");
            var view = Arrive(new StackPanel { Children = { mark } }, reduced: false);
            var first = mark.Opacity;

            Motion.SetSwap(mark, "completed");
            view.Settle();
            var swapping = mark.Opacity;
            view.Elapse(TimeSpan.FromMilliseconds(180));

            Assert.Equal((1d, 0d, 1d), (first, swapping, mark.Opacity));
        }, Cancellation);

    [Fact]
    public Task WithReducedMotionASwapShowsItsNewContentAtOnceAsync() =>
        ui.RunAsync(() =>
        {
            var mark = Swapping("working");
            var view = Arrive(new StackPanel { Children = { mark } }, reduced: true);

            Motion.SetSwap(mark, "completed");
            view.Settle();

            Assert.Equal((1d, true), (mark.Opacity, Unmoved(mark)));
        }, Cancellation);

    private static IEnumerable<string> Animated(IEnumerable<Avalonia.Styling.IStyle> styles) =>
        styles.SelectMany(style => style switch
        {
            Avalonia.Styling.Style { Selector: { } selector, Animations.Count: > 0 } styled => Animated(styled.Children).Prepend(selector.ToString()),
            Avalonia.Styling.Styles nested => Animated(nested),
            Avalonia.Styling.StyleBase other => Animated(other.Children),
            _ => [],
        });

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
            var border = new Border { Name = entrance.ToString(), Width = 10, Height = 10 };
            Motion.SetEnter(border, entrance);
            panel.Children.Add(border);
        }

        return panel;
    }

    private static Border Swapping(string value)
    {
        var mark = new Border { Name = "Mark", Width = 10, Height = 10 };
        Motion.SetSwap(mark, value);

        return mark;
    }

    private static StackPanel Thinking() => new()
    {
        Children =
        {
            Looping(new Border { Name = "Shimmer", Width = 48, Height = 12 }, MotionLoop.Shimmer),
            Looping(new Border { Name = "Dot", Width = 4, Height = 4 }, MotionLoop.Think),
            Looping(new TextBlock { Name = "Caret", Text = "|" }, MotionLoop.Caret),
        },
    };

    private static Control Looping(Control control, MotionLoop loop)
    {
        Motion.SetLoop(control, loop);

        return control;
    }
}
