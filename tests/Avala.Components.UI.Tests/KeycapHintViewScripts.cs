using Avala.Components.Keycaps;
using Avala.Testing.UI;
using Avalonia.Controls;

namespace Avala.Components.UI.Tests;

public sealed class KeycapHintViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AHintShowsItsKeysInTheCodeFontBeforeItsActionAsync() =>
        ui.RunAsync(() =>
        {
            var view = ViewScript.Show(new DesignKeycapHintViewModel());

            Assert.Equal(["J K", "move"], view.VisibleTexts);
            Assert.Equal("JetBrains Mono", view.Find<TextBlock>("Keys").FontFamily.FamilyNames[0]);
        }, Cancellation);

    [Fact]
    public Task AHintShowsTheKeysItIsGivenAsync() =>
        ui.RunAsync(() =>
        {
            var view = ViewScript.Show(new KeycapHintViewModel("esc", "close"));

            Assert.Equal(["esc", "close"], view.VisibleTexts);
        }, Cancellation);
}
