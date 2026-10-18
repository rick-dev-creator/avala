using Avala.Testing.UI;
using Avala.Workbench.Conversation;
using Avalonia;

namespace Avala.Workbench.Tests.Views;

public sealed class MediaViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AShownImageHasItsCaptionBelowItAndNoCaptionLineWithoutOneAsync() =>
        ui.RunAsync(() =>
        {
            var captioned = Screen.Show(new DesignMediaViewModel());
            var bare = Screen.Show(new DesignMediaViewModel { Caption = string.Empty });

            Assert.Equal((true, "The checkout page after the rounding fix"), (captioned.Shows("Caption"), captioned.TextOf("Caption")));
            Assert.True(captioned.Find("Picture").TranslatePoint(default, captioned.Window)?.Y < captioned.Find("Caption").TranslatePoint(default, captioned.Window)?.Y);
            Assert.False(bare.Shows("Caption"));
        }, TestContext.Current.CancellationToken);
}
