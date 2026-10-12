using Avala.Components.UI.Theme;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;

namespace Avala.Components.UI.Tests;

public sealed class ChromeScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ABarePageClearsTheWindowButtonsAboveAndBesideItsHeaderUnlessInFocusModeAsync() =>
        ui.RunAsync(() =>
        {
            var page = new Border();
            _ = ViewScript.Show(page, new object());

            Chrome.SetIsBare(page, true);
            var bare = Room(page);
            Chrome.SetIsFocusMode(page, true);

            Assert.Equal((new Thickness(0, Chrome.Clearance, 0, 0), new Thickness(24 + Chrome.Leading, 12, 24, 12)), bare);
            Assert.Equal((new Thickness(0), new Thickness(24, 12, 24, 12)), Room(page));
        }, Cancellation);

    private static (Thickness Clearance, Thickness HeaderPadding) Room(Control page) =>
        ((Thickness)page.Resources["ChromeClearance"]!, (Thickness)page.Resources["PageHeaderPadding"]!);
}
