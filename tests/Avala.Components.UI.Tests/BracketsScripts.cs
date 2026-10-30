using Avala.Components.UI.Theme;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;

namespace Avala.Components.UI.Tests;

public sealed class BracketsScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task BracketsCloseInOnARowUnderThePointerAndLeaveWithItAsync() =>
        ui.RunAsync(() =>
        {
            var row = new Border { Width = 200, Height = 32, Margin = new Thickness(20), Background = Avalonia.Media.Brushes.Transparent, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
            Brackets.SetOnHover(row, true);
            var view = ViewScript.Show(new Panel { Children = { row } }, new object());
            var before = Brackets.AreShown(row);

            view.Window.MouseMove(new Point(100, 36));
            view.Settle();
            var hovered = Brackets.AreShown(row);
            view.Window.MouseMove(new Point(100, 200));
            view.Settle();

            Assert.Equal((false, true, false), (before, hovered, Brackets.AreShown(row)));
        }, Cancellation);
}
