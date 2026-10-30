using Avala.Components.UI.Theme;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;

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
    public Task PageUpInterruptsAsync() =>
        ui.RunAsync(() =>
        {
            var (view, _, runs) = Interrupted();
            view.Find("Draft").Focus();

            view.Press(Key.PageUp);

            Assert.Equal(1, runs());
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
}
