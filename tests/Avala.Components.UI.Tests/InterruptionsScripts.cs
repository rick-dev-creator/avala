using Avala.Components.UI.Theme;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
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

    [Fact]
    public Task TypingInterruptsAsync() =>
        ui.RunAsync(() =>
        {
            var (view, _, runs) = Interrupted();
            var before = runs();

            view.Type("Draft", "x");

            Assert.Equal(1, runs() - before);
        }, Cancellation);

    [Fact]
    public Task ScrollingBackInterruptsAndScrollingOnDoesNotAsync() =>
        ui.RunAsync(() =>
        {
            var (view, scroller, runs) = Interrupted();
            var before = runs();

            scroller.Offset = new Vector(0, 1200);
            view.Settle();
            var onward = runs() - before;
            scroller.Offset = new Vector(0, 600);
            view.Settle();

            Assert.Equal(0, onward);
            Assert.Equal(1, runs() - before);
        }, Cancellation);
}
