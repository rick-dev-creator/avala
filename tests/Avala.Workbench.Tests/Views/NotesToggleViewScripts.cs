using Avala.Testing.UI;
using Avala.Workbench.Notes;
using Avalonia.Controls;

namespace Avala.Workbench.Tests.Views;

public sealed class NotesToggleViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheNotesGlyphShowsSelectedWhileThePaneIsOpenAsync() =>
        ui.RunAsync(() =>
        {
            var open = Screen.Show(new DesignNotesToggleViewModel());
            var closed = Screen.Show(new DesignNotesToggleViewModel { IsOpen = false });

            Assert.Contains("selected", open.Find<Button>("Notes").Classes);
            Assert.DoesNotContain("selected", closed.Find<Button>("Notes").Classes);
        }, TestContext.Current.CancellationToken);
}
