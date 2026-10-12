using Avala.Components.UI.Theme;
using Avala.Testing.UI;
using Avalonia.Controls;

namespace Avala.Components.UI.Tests;

public sealed class ReadingScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AControlThatFollowsTheReadingWidthKeepsItFittedToItsOwnWidthAsync() =>
        ui.RunAsync(() =>
        {
            var (view, page) = Shown(1500);
            var wide = page.Resources[Reading.WidthKey];

            view.Window.Width = 1000;
            view.Settle();

            Assert.Equal((880d, 720d), (wide, page.Resources[Reading.WidthKey]));
        }, Cancellation);

    [Fact]
    public Task FocusModeWidensTheReadingWidthAsync() =>
        ui.RunAsync(() =>
        {
            var (view, page) = Shown(1500);

            Chrome.SetIsFocusMode(page, true);
            view.Settle();

            Assert.Equal(1200d, page.Resources[Reading.WidthKey]);
        }, Cancellation);

    private static (ViewScript View, Border Page) Shown(double width)
    {
        var page = new Border();
        Reading.SetFollows(page, true);
        var view = ViewScript.Show(page, new object());
        view.Window.Width = width;
        view.Settle();

        return (view, page);
    }
}
