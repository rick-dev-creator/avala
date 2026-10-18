using Avala.Testing.UI;
using Avala.Workbench.Notes;
using Avalonia.Automation;
using Avalonia.Controls;

namespace Avala.Workbench.Tests.Views;

public sealed class NoteImageViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AThumbnailNamesItsImageAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignNoteImageViewModel());

            Assert.Equal("login.png", AutomationProperties.GetName(view.Find<Image>("Picture")));
            Assert.Equal("login.png", ToolTip.GetTip(view.Find("Frame")));
        }, TestContext.Current.CancellationToken);
}
