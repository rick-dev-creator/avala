using Avala.Components.UI.Theme;
using Avala.Components.UI.Zooming;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Components.UI.Tests;

public sealed class InterruptionsScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static (ViewScript View, ScrollViewer Scroller, Func<int> Runs) Interrupted()
    {
        var runs = 0;
        var scroller = new ScrollViewer { Name = "Scroller", Height = 200, Content = new Border { Height = 2000 } };
        var stage = new DockPanel { Children = { new TextBox { Name = "Draft", [DockPanel.DockProperty] = Dock.Bottom }, scroller } };
        Interruptions.SetCommand(stage, new RelayCommand(() => runs++));
        var view = ViewScript.Show(stage, new object());
        scroller.Offset = new Vector(0, 1000);
        view.Settle();

        return (view, scroller, () => runs);
    }

    private static Point Over(ViewScript view, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), view.Window) ?? default;

    [Fact]
    public Task TypingInterruptsAsync() =>
        ui.RunAsync(() =>
        {
            var (view, _, runs) = Interrupted();

            view.Type("Draft", "x");

            Assert.Equal(1, runs());
        }, Cancellation);

    [Fact]
    public Task WheelingBackInterruptsAndWheelingOnDoesNotAsync() =>
        ui.RunAsync(() =>
        {
            var (view, scroller, runs) = Interrupted();

            view.Window.MouseWheel(Over(view, scroller), new Vector(0, -1));
            view.Settle();
            var onward = runs();
            view.Window.MouseWheel(Over(view, scroller), new Vector(0, 1));
            view.Settle();

            Assert.Equal((0, 1), (onward, runs()));
        }, Cancellation);

    [Fact]
    public Task PageUpInTheConversationInterruptsAsync() =>
        ui.RunAsync(() =>
        {
            var runs = 0;
            var line = new Button { Name = "Line", Height = 2000, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
            var scroller = new ScrollViewer { Name = "Scroller", Height = 200, Content = line };
            var stage = new StackPanel { Children = { scroller } };
            Interruptions.SetCommand(stage, new RelayCommand(() => runs++));
            var view = ViewScript.Show(stage, new object());
            line.Focus();
            scroller.Offset = new Vector(0, 1000);
            view.Settle();

            view.Press(Key.PageUp);

            Assert.True(scroller.Offset.Y < 1000, $"the conversation is at {scroller.Offset.Y:0}");
            Assert.Equal(1, runs);
        }, Cancellation);

    [Fact]
    public Task ScrollingBackLongAfterTheLastGestureIsNoInterruptionAsync() =>
        ui.RunAsync(() =>
        {
            var (view, scroller, runs) = Interrupted();
            var clock = new FakeTimeProvider();
            Interruptions.SetClock(view.Window, clock);

            view.Window.MouseWheel(Over(view, scroller), new Vector(0, -1));
            clock.Advance(TimeSpan.FromSeconds(2));
            scroller.Offset = new Vector(0, 100);
            view.Settle();

            Assert.Equal(0, runs());
        }, Cancellation);

    [Fact]
    public Task PagingBackThroughALongDraftIsNoInterruptionAsync() =>
        ui.RunAsync(() =>
        {
            var runs = 0;
            var draft = new TextBox { Name = "Draft", AcceptsReturn = true, Height = 80, Text = string.Join('\n', Enumerable.Range(1, 60).Select(line => $"line {line}")) };
            var stage = new StackPanel { Children = { draft } };
            Interruptions.SetCommand(stage, new RelayCommand(() => runs++));
            var view = ViewScript.Show(stage, new object());
            draft.Focus();
            draft.CaretIndex = draft.Text.Length;
            view.Settle();
            var inner = draft.GetVisualDescendants().OfType<ScrollViewer>().First();
            var from = inner.Offset.Y;

            view.Press(Key.PageUp);

            Assert.True(inner.Offset.Y < from, $"the draft stayed at {inner.Offset.Y:0}");
            Assert.Equal(0, runs);
        }, Cancellation);

    [Fact]
    public Task TheContentMovingTheScrollBackByItselfIsNoInterruptionAsync() =>
        ui.RunAsync(() =>
        {
            var (view, scroller, runs) = Interrupted();

            scroller.Offset = new Vector(0, 400);
            view.Settle();
            ((Border)scroller.Content!).Height = 300;
            view.Settle();

            Assert.Equal(0, runs());
        }, Cancellation);

    [Fact]
    public Task WheelingToZoomAPictureIsNoInterruptionAsync() =>
        ui.RunAsync(() =>
        {
            var runs = 0;
            var picture = new ZoomSurface { Name = "Picture", Width = 300, Height = 200, Child = new Border { Width = 600, Height = 400 } };
            var stage = new StackPanel { Children = { picture } };
            Interruptions.SetCommand(stage, new RelayCommand(() => runs++));
            var view = ViewScript.Show(stage, new object());
            var before = picture.Scale;

            view.Window.MouseWheel(Over(view, picture), new Vector(0, 1));
            view.Settle();

            Assert.NotEqual(before, picture.Scale);
            Assert.Equal(0, runs);
        }, Cancellation);

    [Theory]
    [InlineData(Key.Home)]
    [InlineData(Key.Up)]
    public Task MovingTheCaretInADraftIsNoInterruptionAsync(Key key) =>
        ui.RunAsync(() =>
        {
            var runs = 0;
            var draft = new TextBox { Name = "Draft", AcceptsReturn = true, Text = "first line\nsecond line", CaretIndex = 22 };
            var stage = new StackPanel { Children = { draft } };
            Interruptions.SetCommand(stage, new RelayCommand(() => runs++));
            var view = ViewScript.Show(stage, new object());
            draft.Focus();

            view.Press(key);

            Assert.True(draft.CaretIndex < 22);
            Assert.Equal(0, runs);
        }, Cancellation);

    [Fact]
    public Task DraggingTheScrollBarBackInterruptsAsync() =>
        ui.RunAsync(() =>
        {
            var runs = 0;
            var scroller = new ScrollViewer { Name = "Scroller", Height = 200, AllowAutoHide = false, Content = new Border { Height = 2000 } };
            var stage = new StackPanel { Children = { scroller } };
            Interruptions.SetCommand(stage, new RelayCommand(() => runs++));
            var view = ViewScript.Show(stage, new object());
            scroller.Offset = new Vector(0, 1800);
            view.Settle();
            var thumb = scroller.GetVisualDescendants().OfType<Thumb>().First(found => found.IsEffectivelyVisible && found.Bounds.Height > 0 && found.FindAncestorOfType<ScrollBar>()?.Orientation == Avalonia.Layout.Orientation.Vertical);
            var from = Over(view, thumb);

            view.Window.MouseMove(from);
            view.Window.MouseDown(from, MouseButton.Left);
            view.Window.MouseMove(from + new Point(0, -40));
            view.Window.MouseMove(from + new Point(0, -80));
            view.Window.MouseUp(from + new Point(0, -80), MouseButton.Left);
            view.Settle();

            Assert.True(scroller.Offset.Y < 1700, $"the conversation is at {scroller.Offset.Y:0}");
            Assert.Equal(1, runs);
        }, Cancellation);
}
